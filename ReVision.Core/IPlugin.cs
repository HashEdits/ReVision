using Microsoft.Extensions.Logging;

namespace ReVision.Core;

public interface IPlugin
{
    public Version Version { get; }
    
    public ILogger Logger { get; set; }
    
    public Task<bool> Initialize();
    
    public Task Shutdown();
}