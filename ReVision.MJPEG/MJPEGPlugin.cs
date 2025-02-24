using Microsoft.Extensions.Logging;
using ReVision.Core;

namespace ReVision.MJPEG;

public class MJPEGPlugin : IPlugin, IImageStreamer
{
    public Version Version { get; }
    public ILogger Logger { get; set; }
    
    public async Task<bool> Initialize()
    {
        WebService.Init();
        return true;
    }

    public async Task Shutdown()
    {
        
    }

    public void UpdateImageData(Eye targetEye, RGBAImage rgbaImage)
    {
        WebService.UpdateImageData(targetEye, rgbaImage);
    }
}