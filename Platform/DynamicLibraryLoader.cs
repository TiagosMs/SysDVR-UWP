using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SysDVR.Client.Platform
{
    internal static class DynamicLibraryLoader
    {
        [DllImport("api-ms-win-core-libraryloader-l2-1-0.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadPackagedLibrary(string lpLibFileName, int reserved = 0);

        readonly static ConcurrentDictionary<string, IntPtr> LibraryCache = new();

        static IntPtr CachedLibraryLoader(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (LibraryCache.TryGetValue(libraryName, out var result))
                return result;

            var loaded = TryLoadLibrary(libraryName);

            if (loaded == IntPtr.Zero && Program.Options.Debug.DynLib)
                Debug.WriteLine($"CachedLibraryLoader failed to resolve {libraryName}");

            LibraryCache.TryAdd(libraryName, loaded);
            return loaded;
        }

        public static IntPtr TryLoadLibrary(string libraryName)
        {
            var debug = Program.Options.Debug.DynLib;

            if (!libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                libraryName += ".dll";

            try
            {
                var handle = LoadPackagedLibrary(libraryName, 0);
                if (handle != IntPtr.Zero)
                {
                    if (debug) Debug.WriteLine($"Loaded via LoadPackagedLibrary: {libraryName}");
                    return handle;
                }
            }
            catch (Exception ex)
            {
                if (debug) Debug.WriteLine($"LoadPackagedLibrary failed for {libraryName}: {ex.Message}");
            }

            try
            {
                if (NativeLibrary.TryLoad(libraryName, out var handle))
                {
                    if (debug) Debug.WriteLine($"Loaded via NativeLibrary.TryLoad: {libraryName}");
                    return handle;
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        public static void Initialize()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), CachedLibraryLoader);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set DllImportResolver: {ex.Message}");
            }
        }
    }
}
