using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ReVision.App;

public class PluginLoadContext : AssemblyLoadContext
{
    private static IntPtr _hidApiLibraryHandle = IntPtr.Zero;
    
    private AssemblyDependencyResolver _assemblyResolver;

    private Assembly _revisionCore;
    private Assembly _loggingAbstractions;

    public PluginLoadContext(Assembly revisionCore, Assembly loggingAbstractions)
    {
        _assemblyResolver = new AssemblyDependencyResolver(Path.GetFullPath("Plugins"));
        _revisionCore = revisionCore;
        _loggingAbstractions = loggingAbstractions;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        //Console.WriteLine($"Loading assembly {assemblyName.Name}");

        if (assemblyName.Name == "ReVision.Core")
            return _revisionCore;

        if (assemblyName.Name == "Microsoft.Extensions.Logging.Abstractions")
            return _loggingAbstractions;
        
        string? assemblyPath = _assemblyResolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (unmanagedDllName == "HidApi")
        {
            if (_hidApiLibraryHandle != IntPtr.Zero)
                return _hidApiLibraryHandle;

            foreach (var library in NativeHidApiLibrary.GetNames())
                if (NativeLibrary.TryLoad(library, out _hidApiLibraryHandle))
                    return _hidApiLibraryHandle;
            
            return IntPtr.Zero;
        }
        
        //Console.WriteLine($"Loading unmanaged dll {unmanagedDllName}");
        string libraryPath = _assemblyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}