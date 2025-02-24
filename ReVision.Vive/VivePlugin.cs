using HidApi;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using Microsoft.Extensions.Logging;
using ReVision.Core;

namespace ReVision.Vive;

public class VivePlugin : IPlugin
{
    public Version Version => new(1, 0, 0, 0);
    public ILogger Logger { get; set; }

    private CancellationTokenSource _cancellationTokenSource = null!;
    private CancellationToken _cancellationToken;
    
    private UsbContext _usbContext = null!;
    private DeviceManager _deviceManager = null!;
    private UsbDeviceFinder _usbFinder = null!;

    private ViveDevice? _viveDevice;

    public async Task<bool> Initialize()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        
        _usbContext = new UsbContext();
        _deviceManager = new DeviceManager(_usbContext);
        _usbFinder = new UsbDeviceFinder
        {
            Vid = 0x0BB4,
            Pid = 0x0309
        };
        
        _deviceManager.Start();
        _ = Task.Run(DetectViveDevice, _cancellationToken);

        return true;
    }

    public async Task Shutdown()
    {
        _viveDevice?.Dispose();
        
        await _cancellationTokenSource.CancelAsync();
        
        _deviceManager.Dispose();
        _usbContext.Dispose();
    }

    private async Task DetectViveDevice()
    {
        try
        {
            var ctr = new CancellationTokenSource();
            
            UsbDevice? usbDevice =
                await _deviceManager.WaitForDeviceArrival(_usbFinder, TimeSpan.FromSeconds(5), ctr.Token);

            while (!_cancellationToken.IsCancellationRequested)
            {
                ctr = new CancellationTokenSource();
                usbDevice ??=
                    await _deviceManager.WaitForNewDeviceArrival(_usbFinder, TimeSpan.FromSeconds(5),
                        ctr.Token, TimeSpan.FromSeconds(5));

                if (usbDevice == null)
                    continue;

                _viveDevice?.Dispose();

                DeviceInfo? deviceInfo = Hid.Enumerate(usbDevice.VendorId, usbDevice.ProductId).FirstOrDefault();
                usbDevice = null;

                if (deviceInfo == null)
                    continue;

                _viveDevice = new ViveDevice(deviceInfo.ConnectToDevice());
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to detect vive device");
        }
    }
}