using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using CommunityToolkit.HighPerformance;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using ReVision.Core;
using ReVision.EyeDevice.Platform;
using ReVision.EyeDevice.Platform.Client;
using ReVision.EyeDevice.Platform.Server;

namespace ReVision.EyeDevice;

public class EyeDevice : IDisposable
{
    private bool _disposed;
    
    private CancellationTokenSource _cancellationTokenSource;
    private CancellationToken _cancellationToken;

    private uint currentMessageId;
    private Channel<(BasePacket, TaskCompletionSource<BasePacket>)> _outgoingMessageQueue
        = Channel.CreateUnbounded<(BasePacket, TaskCompletionSource<BasePacket>)>();
    private ConcurrentDictionary<uint, TaskCompletionSource<BasePacket>> _pendingRequests = new();

    private static readonly byte[] HmacKey = new byte[]
    {
        0x70, 0xF0, 0x5E, 0xCF, 0x0A, 0x28, 0x66, 0x0C, 0xAD, 0x5A, 0x69, 0x85, 0xAA, 0x9F, 0x85, 0x82
    };
    
    private EyeDevicePlugin _plugin;

    private IUsbDevice _usbDevice;
    private UsbEndpointReader _reader;
    private UsbEndpointWriter _writer;

    private static readonly IReadOnlyDictionary<uint, Type> PacketTypeMap = new Dictionary<uint, Type>
    {
        { 1000, typeof(SUpgradeChannelProtocol) },
        { 1420, typeof(SUnitInformation) },
        { 1900, typeof(SAuthorizeChallenge) },
        { 1291, typeof(SImageCollection) }
    };

    public EyeDevice(EyeDevicePlugin plugin, IUsbDevice usbDevice)
    {
        _plugin = plugin;
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        _usbDevice = usbDevice;

        _usbDevice.Open();
        _usbDevice.ClaimInterface(0);

        _reader = _usbDevice.OpenEndpointReader(ReadEndpointID.Ep03);
        _writer = _usbDevice.OpenEndpointWriter(WriteEndpointID.Ep05);

        Task.Run(MessageReceiveQueue);
        Task.Run(MessageSendQueue);
        Task.Run(CleanupPendingRequests);

        Task.Run(InitializeDevice)
            .ContinueWith(async t =>
            {
                if (t.IsFaulted)
                    return;

                await GetDeviceInfo();
                await UnknownCommands();
                await UpgradeChannelProtocol();
                await GetUnitInformation();
                await AuthorizeTracker();
                await ExtendedDataStreamSubscribe(1291);
                await InternalCommandFeatureLock(1001);
            });
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        _cancellationTokenSource.Cancel();
        _usbDevice.Dispose();
    }

    private async Task MessageReceiveQueue()
    {
        Memory<byte> receiveBuffer = new Memory<byte>(new byte[1024 * 16]);
        IMemoryOwner<byte> fragmentBufferOwner = MemoryPool<byte>.Shared.Rent(1024 * 16);
        Error transferError = Error.Success;
        int transferLength = 0;
        
        int totalTransferred = 0;
        int incomingMessageLength = 0;

        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                (transferError, transferLength) = await _reader.ReadAsync(receiveBuffer, 0, receiveBuffer.Length, 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            
            if (transferError != Error.Success)
            {
                await _cancellationTokenSource.CancelAsync();
                break;
            }

            BasePacket? incomingPacket;
            
            if (incomingMessageLength > 0)
            {
                try
                {
                    receiveBuffer[..transferLength].CopyTo(fragmentBufferOwner.Memory[totalTransferred..]);
                    totalTransferred += transferLength;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{receiveBuffer[..transferLength].Length} > {fragmentBufferOwner.Memory[totalTransferred..].Length}");
                    Console.WriteLine(ex);
                }

                if (totalTransferred != incomingMessageLength)
                    continue;
                
                incomingPacket = ToPacket(fragmentBufferOwner.Memory[..incomingMessageLength]);

                incomingMessageLength = 0;
                totalTransferred = 0;
            }
            else
            {
                LengthHeader lengthHeader = MemoryMarshal.Read<LengthHeader>(receiveBuffer.Span);

                if (lengthHeader.MessageLength != transferLength)
                {
                    incomingMessageLength = (int)lengthHeader.MessageLength;
                    totalTransferred = transferLength;
                
                    fragmentBufferOwner.Dispose();
                    fragmentBufferOwner = MemoryPool<byte>.Shared.Rent(incomingMessageLength);
                    receiveBuffer[..transferLength].CopyTo(fragmentBufferOwner.Memory);
                    continue;
                }

                incomingPacket = ToPacket(receiveBuffer[..transferLength]);
            }
            
            if (incomingPacket == null)
            {
                Console.WriteLine("Failed to convert incoming message");
                continue;
            }
            
            HandleIncomingPacket(incomingPacket);
        }
    }

    private BasePacket? ToPacket(Memory<byte> buffer)
    {
        try
        {
            BasePacket incomingPacket = new BasePacket(buffer);

            if (PacketTypeMap.TryGetValue(incomingPacket.PacketId, out var packetType))
                incomingPacket = (BasePacket)Activator.CreateInstance(packetType, buffer)!;
            
            //Console.WriteLine($"Incoming Packet: {incomingPacket.GetType().FullName} ({incomingPacket.PacketId})");

            return incomingPacket;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        return null;
    }

    private void HandleIncomingPacket(BasePacket incomingPacket)
    {
        // These are messages without explicit requests such as track data
        if (incomingPacket.MessageId == 0)
        {
            switch (incomingPacket.PacketId)
            {
                case 1291:
                    SImageCollection imageCollection = (SImageCollection)incomingPacket;
                    Eye eye = imageCollection.Region == SImageCollection.EyeRegion.Left ? Eye.Left : Eye.Right;
                    
                    // Convert 8bpp to rgba greyscale
                    ReadOnlyMemory<byte> imageBytes = imageCollection.ImageBlobs[0];
                    ReadOnlySpan<byte> imageData = imageBytes.Span;
                    using (IMemoryOwner<byte> rgbaData = MemoryPool<byte>.Shared.Rent(200 * 200 * 4))
                    {
                        Span<byte> rgbaSpan = rgbaData.Memory.Span;
                        for (int y = 0; y < 200; y++)
                        {
                            for (int x = 0; x < 200; x++)
                            {
                                byte grayscaleValue = imageData[y * 200 + x];
                                
                                int index = (y * 200 + x) * 4;
                                rgbaSpan[index + 0] = grayscaleValue; // R
                                rgbaSpan[index + 1] = grayscaleValue; // G
                                rgbaSpan[index + 2] = grayscaleValue; // B
                                rgbaSpan[index + 3] = 255; // A (fully opaque)
                            }
                        }

                        _plugin.RaiseUpdateImageData(eye, new RGBAImage
                        {
                            Width = 200,
                            Height = 200,
                            Pixels = rgbaData.Memory
                        });
                    }

                    break;
            }
            return;
        }

        if (!_pendingRequests.TryRemove(incomingPacket.MessageId, out TaskCompletionSource<BasePacket>? tcs))
            return;

        tcs.TrySetResult(incomingPacket);
    }

    private async Task MessageSendQueue()
    {
        Memory<byte> sendBuffer = new Memory<byte>(new byte[1024 * 16]);
        Error transferError = Error.Success;
        int transferLength = 0;

        await foreach ((BasePacket outgoingPacket, TaskCompletionSource<BasePacket> tcs) 
                       in _outgoingMessageQueue.Reader.ReadAllAsync(_cancellationToken))
        {
            uint messageId = ++currentMessageId;
            int outgoingPacketLength = outgoingPacket.AsOutgoingMessage(messageId, ref sendBuffer);
            _pendingRequests.TryAdd(messageId, tcs);

            try
            {
                (transferError, transferLength) = await _writer.WriteAsync(sendBuffer, 0, outgoingPacketLength, 30000);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            if (transferError != Error.Success)
            {
                await _cancellationTokenSource.CancelAsync();
                break;
            }
        }
    }

    private async Task CleanupPendingRequests()
    {
        List<uint> pendingRequestIds = new();
        
        while (!_cancellationToken.IsCancellationRequested)
        {
            foreach (var kvp in _pendingRequests)
            {
                if (kvp.Value.Task.IsCompleted)
                    pendingRequestIds.Add(kvp.Key);
            }
            
            foreach (var pendingId in pendingRequestIds)
                _pendingRequests.TryRemove(pendingId, out _);
                
            pendingRequestIds.Clear();
            await Task.Delay(TimeSpan.FromSeconds(30), _cancellationToken);
        }
    }
    
    private async Task InitializeDevice()
    {
        // I'm not sure *what* this actually does, I'm *assuming* this is a magic sequence that
        // causes the device to reset or initialize
        byte[] resetBuffer = new byte[]
        {
            0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x04, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        await _usbDevice.ControlTransferAsync(new UsbSetupPacket
        {
            RequestType = (byte)(UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_Out),
            Request = 48,
            Value = 0,
            Index = 0,
            Length = (short)resetBuffer.Length
        }, resetBuffer, 0, resetBuffer.Length);
    }
    
    private async Task GetDeviceInfo()
    {
        byte[] deviceInfoBuffer = new byte[512];
        
        await _usbDevice.ControlTransferAsync(new UsbSetupPacket
        {
            RequestType = (byte)(UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_In),
            Request = 48,
            Value = 0,
            Index = 0,
            Length = (short)deviceInfoBuffer.Length
        }, deviceInfoBuffer, 0, deviceInfoBuffer.Length);
        
        int stringLength = BitConverter.ToInt32(deviceInfoBuffer, 0x14);
        string deviceInfo = Encoding.ASCII.GetString(deviceInfoBuffer, 0x18, stringLength);
        
        string[] deviceInfoSplit = deviceInfo.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        
        Dictionary<string, string> deviceInfoDictionary = new Dictionary<string, string>();
        
        // Why are we using java properties format???
        foreach (string deviceInfoLine in deviceInfoSplit)
        {
            string[] deviceInfoLineSplit = deviceInfoLine.Split("=", StringSplitOptions.RemoveEmptyEntries);

            if (deviceInfoLineSplit.Length != 2)
                continue;
            
            deviceInfoDictionary.Add(deviceInfoLineSplit[0], deviceInfoLineSplit[1]);
        }
        
        ValidateValidDevice(deviceInfoDictionary);
    }

    private void ValidateValidDevice(Dictionary<string, string> deviceInfo)
    {
        if (deviceInfo.TryGetValue("/hw/platformtype", out string? platformTypeString))
        {
            if (platformTypeString != "VR4U2P2")
                throw new InvalidDeviceException("VR4U2P2 devices are the only supported devices");
        }
        else
        {
            throw new InvalidDeviceException("Missing device type");
        }
        
        if (deviceInfo.TryGetValue("/fw/t2serverversion", out string? serverVersionString))
        {
            if (serverVersionString != "3.4.0-ee6bcb43")
                throw new InvalidDeviceException("Only server version 3.4.0-ee6bcb43 is supported");
        }
        else
        {
            throw new InvalidDeviceException("Missing server version");
        }
        
        if (deviceInfo.TryGetValue("/fw/fwversion", out string? fwVersionString))
        {
            if (fwVersionString != "2.41.0-942e3e4")
                throw new InvalidDeviceException("Only fw version 2.41.0-942e3e4 is supported");
        }
        else
        {
            throw new InvalidDeviceException("Missing fw version");
        }
    }
    
    private async Task UnknownCommands()
    {
        byte[] buffer = new byte[8];
        
        await _usbDevice.ControlTransferAsync(new UsbSetupPacket
        {
            RequestType = (byte)(UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_In),
            Request = 70,
            Value = 0x0001,
            Index = 0,
            Length = 8
        }, buffer, 0, 0);

        buffer = [];
        
        await _usbDevice.ControlTransferAsync(new UsbSetupPacket
        {
            RequestType = (byte)(UsbCtrlFlags.RequestType_Vendor | UsbCtrlFlags.Recipient_Interface | UsbCtrlFlags.Direction_Out),
            Request = 65,
            Value = 0,
            Index = 0,
            Length = 0
        }, buffer, 0, 0);
    }
    
    private Task<T> QueueMessageWithResponse<T>(BasePacket basePacket, int timeout = 30000) where T : BasePacket
    {
        TaskCompletionSource<BasePacket> tcs = new();
        new CancellationTokenSource(timeout).Token.Register(() => tcs.TrySetCanceled());
        _outgoingMessageQueue.Writer.TryWrite((basePacket, tcs));

        return tcs.Task.ContinueWith(t => (T)t.Result, _cancellationToken);
    }

    private async Task UpgradeChannelProtocol()
    {
        SUpgradeChannelProtocol upgradeChannelProtocol =
            await QueueMessageWithResponse<SUpgradeChannelProtocol>(new CUpgradeChannelProtocol(
            [
                65536,
                65537,
                65538,
                65539,
                65540,
                65541,
                65542,
                65543,
                65544
            ]));

        if (upgradeChannelProtocol.NegotiatedVersion != 65544)
            throw new InvalidChannelVersion();
    }

    private async Task GetUnitInformation()
    {
        SUnitInformation unitInformation = await QueueMessageWithResponse<SUnitInformation>(new CGetUnitInformation());
        
        Console.WriteLine(unitInformation.SerialNumber);
    }

    private async Task AuthorizeTracker()
    {
        SAuthorizeChallenge challenge = await QueueMessageWithResponse<SAuthorizeChallenge>(new CAuthorizeChallenge());
        
        using HMAC hmac = new HMACMD5(HmacKey);
        byte[] challengeKey = await hmac.ComputeHashAsync(challenge.ChallengeData.AsStream(), _cancellationToken);
        
        await QueueMessageWithResponse<BasePacket>(new CAuthorizeResponse(challengeKey,
            [0x99, 0x30, 0xc7, 0x98, 0x19, 0x93, 0xda, 0x87, 0x3a, 0xde, 0x2b, 0x0a, 0x62, 0x19, 0xeb, 0x31]));
    }

    private async Task ExtendedDataStreamSubscribe(uint streamId)
    {
        await QueueMessageWithResponse<BasePacket>(new CExtendedDataStreamSubscribe(streamId));
    }

    private async Task InternalCommandFeatureLock(uint featureId)
    {
        await QueueMessageWithResponse<BasePacket>(new CInternalCommandFeatureLock(featureId));
    }
}