using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using HidApi;

namespace ReVision.Vive;

public class ViveDevice : IDisposable
{
    private bool _disposed;
    
    private Device _hidDevice;
    
    private CancellationTokenSource _cancellationTokenSource;
    private CancellationToken _cancellationToken;

    private Crc32 _crc32;
    private byte[] _deviceSerial;

    public ViveDevice(Device hidDevice)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        _hidDevice = hidDevice;
        
        _deviceSerial = Encoding.UTF8.GetBytes(_hidDevice.GetSerialNumber());
        _crc32 = new Crc32();

        Task.Run(EyeChipKeepAlive);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        _cancellationTokenSource.Cancel();
        _hidDevice.Dispose();
    }

    private async void EyeChipKeepAlive()
    {
        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                bool hasValidStatus = false;
                ReadOnlySpan<byte> validHidReport = default;

                for (int i = 0; i < 10; ++i)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlySpan<byte> hidReport = _hidDevice.Read(64);

                    // Are we status report?
                    if (hidReport[0] != 0x03 || hidReport[1] != 0xD0 || hidReport[2] != 0x2C)
                        continue;

                    hasValidStatus = true;
                    validHidReport = hidReport;
                }

                if (!hasValidStatus)
                    continue;

                _crc32.Reset();
                _crc32.Append(_deviceSerial);
                _crc32.Append(validHidReport.Slice(validHidReport.Length - 2, 2));
                uint crc = _crc32.GetCurrentHashAsUInt32();

                ViveControl viveControl = new ViveControl();
                viveControl.ReportId = 0x04;
                viveControl.Command = 0x2978;
                viveControl.Length = 0x38;
                viveControl.Unknown2 = 0x08;
                viveControl.Crc = new byte[3];
                viveControl.Crc[0] = (byte)((crc >> 25) | 0x80);
                viveControl.Crc[1] = (byte)(crc >> 17);
                viveControl.Crc[2] = (byte)(crc >> 9);

                int structSize = Marshal.SizeOf(typeof(ViveControl));
                byte[] outgoingData = new byte[structSize];

                IntPtr dataPtr = Marshal.AllocHGlobal(structSize);
                Marshal.StructureToPtr(viveControl, dataPtr, false);
                Marshal.Copy(dataPtr, outgoingData, 0, structSize);
                Marshal.FreeHGlobal(dataPtr);
                
                _hidDevice.SendFeatureReport(outgoingData);
                await Task.Delay(500, _cancellationToken);
            }
        }
        catch (HidException ex)
        {
            Console.WriteLine(ex);
            
            if (ex.HResult == 0x0000048F)
                Dispose();
        }
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ViveControl
    {
        public byte ReportId;
        public ushort Command;
        public byte Length;

        public byte Unknown6;
    
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public byte[] Unknown1;
    
        public byte Unknown2;

        public byte Unknown7;
        
        public byte Unknown8;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
        public byte[] Unknown3;
        
        public byte Unknown4;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Crc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
        public byte[] Unknown5;
    }
}