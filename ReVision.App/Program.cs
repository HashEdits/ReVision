using System.Reflection;
using Microsoft.Extensions.Logging;
using ReVision.Core;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ReVision.App;

class Program
{
    private static ILoggerFactory _loggerFactory = new LoggerFactory()
        .AddSerilog(new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger()
        );
    
    private static ILogger _logger = _loggerFactory.CreateLogger(typeof(Program));
    
    private static PluginLoadContext _loadContext;
    private static List<IPlugin> _plugins = new List<IPlugin>();
    private static IImageProvider? _imageProvider;
    private static IImageStreamer? _imageStreamer;

    private static async Task Main(string[] args)
    {
        _loadContext = new PluginLoadContext(typeof(IPlugin).Assembly, typeof(ILogger).Assembly);

        foreach (var file in Directory.EnumerateFiles(
                     Path.GetFullPath("Plugins"), "*.dll"))
        {
            try
            {
                Assembly loadedAssembly = _loadContext.LoadFromAssemblyPath(file);
                foreach (var type in loadedAssembly.GetTypes()
                             .Where(t => typeof(IPlugin).IsAssignableFrom(t)))
                {
                    IPlugin plugin = (IPlugin)Activator.CreateInstance(type);

                    if (plugin == null)
                        continue;

                    plugin.Logger = _loggerFactory.CreateLogger(type);

                    if (await plugin.Initialize())
                    {
                        _plugins.Add(plugin);
                        _logger.LogInformation("Loaded {Type}", plugin.GetType().FullName);

                        if (plugin is IImageProvider imageProvider && _imageProvider == null)
                        {
                            _imageProvider = imageProvider;
                            _imageProvider.OnUpdateImageData += UpdateImageData;
                            _logger.LogInformation("Using image provider {ImageProvider}", imageProvider.GetType().FullName);
                        }

                        if (plugin is IImageStreamer imageStreamer && _imageStreamer == null)
                        {
                            _imageStreamer = imageStreamer;
                            _logger.LogInformation("Using image streamer {ImageStreamer}", imageStreamer.GetType().FullName);
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to load {Type}", plugin.GetType().FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize a plugin");
            }
        }
        
        TaskCompletionSource taskCompletionSource = new TaskCompletionSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            taskCompletionSource.SetResult();
        };
        
        await taskCompletionSource.Task;

        foreach (IPlugin plugin in _plugins)
        {
            _logger.LogInformation("Stopping plugin {PluginName}", plugin.GetType().FullName);
            await plugin.Shutdown();
        }
    }

    private static void UpdateImageData(Eye targetEye, RGBAImage imageSource)
    {
        _imageStreamer?.UpdateImageData(targetEye, imageSource);
    }
}