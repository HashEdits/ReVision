using System.Collections.Concurrent;
using System.Text;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using ReVision.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Swan.Logging;

namespace ReVision.MJPEG;

public class WebService
{
    private static readonly SemaphoreSlim _leftImageAvailable = new(0, maxCount: 1);
    private static int LeftFrameIdx = 0;
    public static byte[]? LeftEye;
    private static readonly SemaphoreSlim _rightImageAvailable = new(0, maxCount: 1);
    private static int RightFrameIdx = 0;
    public static byte[]? RightEye;
    
    
    private static ConcurrentDictionary<Stream, Eye> _registeredClients = new();
    
    public static void Init()
    {
        var server = new WebServer(o => o
            .WithUrlPrefix("http://127.0.0.1:4442/")
            .WithMode(HttpListenerMode.Microsoft)
        ).WithWebApi("/", m => m.WithController<ImageController>());
        
        server.StateChanged += (s, e) => $"WebServer New State - {e.NewState}".Info();
        
        server.RunAsync();
        Task.Run(ProcessLeft);
        Task.Run(ProcessRight);
    }

    private static async Task ProcessLeft()
    {
        while (true)
        {
            try
            {
                await _leftImageAvailable.WaitAsync();

                if (LeftEye == null)
                    continue;
                
                string leftBoundary = CreateBoundary(LeftEye.Length);
                byte[] leftBoundaryBytes = Encoding.ASCII.GetBytes(leftBoundary);
                
                List<Stream> toRemove = new();

                foreach (var kvp in _registeredClients)
                {
                    try
                    {
                        if (kvp.Value != Eye.Left)
                            continue;
                        
                        kvp.Key.Write(leftBoundaryBytes);
                        kvp.Key.Write(LeftEye);

                        await kvp.Key.FlushAsync();
                    }
                    catch (Exception e)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                
                toRemove.ForEach(s =>
                {
                    s.Dispose();
                    _registeredClients.TryRemove(s, out _);
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
    
    private static async Task ProcessRight()
    {
        while (true)
        {
            try
            {
                await _rightImageAvailable.WaitAsync();
                
                if (RightEye == null)
                    continue;
                
                string rightBoundary = CreateBoundary(RightEye.Length);
                byte[] rightBoundaryBytes = Encoding.ASCII.GetBytes(rightBoundary);
                
                List<Stream> toRemove = new();

                foreach (var kvp in _registeredClients)
                {
                    try
                    {
                        if (kvp.Value != Eye.Right)
                            continue;
                        
                        kvp.Key.Write(rightBoundaryBytes);
                        kvp.Key.Write(RightEye);

                        await kvp.Key.FlushAsync();
                    }
                    catch (Exception e)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                
                toRemove.ForEach(s =>
                {
                    s.Dispose();
                    _registeredClients.TryRemove(s, out _);
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
    
    private static string CreateBoundary(int contentLength)
    {
        var builder = new StringBuilder();

        builder.Append("--");
        builder.Append("frame-boundary");
        builder.Append("\r\n");

        builder.Append("Content-Type: image/jpeg");
        builder.Append("\r\n");

        builder.AppendFormat("Content-Length: {0}", contentLength);
        builder.Append("\r\n");
        builder.Append("\r\n");

        return builder.ToString();
    }

    public static void UpdateImageData(Eye targetEye, RGBAImage rgbaImage)
    {
        if (targetEye == Eye.Left && ++LeftFrameIdx % 2 == 0)
            return;
        
        if (targetEye == Eye.Right && ++RightFrameIdx % 2 == 0)
            return;
        
        using Image<Rgba32> imageData =
            Image.LoadPixelData<Rgba32>(rgbaImage.Pixels.Span, rgbaImage.Width, rgbaImage.Height);
        using MemoryStream memoryStream = new MemoryStream();
        
        imageData.SaveAsJpeg(memoryStream);
        
        switch (targetEye)
        {
            case Eye.Left:
                LeftEye = memoryStream.ToArray();
                if (_leftImageAvailable.CurrentCount == 0)
                    _leftImageAvailable.Release();
                break;
            case Eye.Right:
                RightEye = memoryStream.ToArray();
                if (_rightImageAvailable.CurrentCount == 0)
                    _rightImageAvailable.Release();
                break;
        }
    }

    internal class ImageController : WebApiController
    {
        [Route(HttpVerbs.Get, "/left")]
        public async Task GetLeft()
        {
            HttpContext.Response.SendChunked = true;
            HttpContext.Response.ContentType = "multipart/x-mixed-replace; boundary=frame-boundary";
            var stream = HttpContext.OpenResponseStream(false, false);
            _registeredClients.TryAdd(stream, Eye.Left);
            await Task.Delay(Timeout.Infinite);
        }
        
        [Route(HttpVerbs.Get, "/right")]
        public async Task GetRight()
        {
            HttpContext.Response.SendChunked = true;
            HttpContext.Response.ContentType = "multipart/x-mixed-replace; boundary=frame-boundary";
            var stream = HttpContext.OpenResponseStream(false, false);
            _registeredClients.TryAdd(stream, Eye.Right);
            await Task.Delay(Timeout.Infinite);
        }
    }
}