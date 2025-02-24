using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using Microsoft.Extensions.Logging;
using ReVision.Core;

namespace ReVision.EyeDevice;

public class EyeDevicePlugin : IPlugin, IImageProvider
{
    public Version Version => new(1, 0, 0, 0);
    public ILogger Logger { get; set; }

    public event IImageProvider.UpdateImageData? OnUpdateImageData;
    
    private CancellationTokenSource _cancellationTokenSource = null!;
    private CancellationToken _cancellationToken;
    
    private UsbContext _usbContext = null!;
    private DeviceManager _deviceManager = null!;
    private UsbDeviceFinder _usbFinder = null!;

    private EyeDevice? _eyeDevice;

    public async Task<bool> Initialize()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationToken = _cancellationTokenSource.Token;
        
        _usbContext = new UsbContext();
        _deviceManager = new DeviceManager(_usbContext);
        _usbFinder = new UsbDeviceFinder
        {
            Vid = 0x2104,
            Pid = 0x020F
        };
        
        _deviceManager.Start();
        _ = Task.Run(DetectEyeDevice, _cancellationToken);

        return true;
    }

    public async Task Shutdown()
    {
        _eyeDevice?.Dispose();
        
        await _cancellationTokenSource.CancelAsync();
        
        _deviceManager.Dispose();
        _usbContext.Dispose();
    }

    private async Task DetectEyeDevice()
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
                    await _deviceManager.WaitForNewDeviceArrival(_usbFinder, TimeSpan.FromSeconds(5), ctr.Token);

                if (usbDevice == null)
                    continue;

                _eyeDevice?.Dispose();
                _eyeDevice = new EyeDevice(this, usbDevice);

                usbDevice = null;
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to detect eye device");
        }
    }
    
    public void RaiseUpdateImageData(Eye eye, RGBAImage rgbaImage) => OnUpdateImageData?.Invoke(eye, rgbaImage);
}