using SysDVR.Client.Core;
using SysDVR.Client.Platform;
using System;
using System.Diagnostics;

namespace SysDVR.Client
{
    public static class Program
    {
        public static readonly bool IsWindows = true;
        public static readonly bool IsMacOs = false;
        public static readonly bool IsLinux = false;
        public static readonly bool IsContainerApp = false;
        public static readonly bool IsAndroid = false;

        public static SDLContext SdlCtx = null!;
        public static Options Options = new();
        internal static StringTable Strings = new();

        public static void DebugLog(string message)
        {
            if (Options.Debug.Log)
                Debug.WriteLine(message);
        }

        public static void AddInitializationError(string error)
        {
            Debug.WriteLine($"[INIT ERROR] {error}");
        }
    }
}

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // Required for C# 9+ init properties in .NET Standard 2.0 / UWP
    internal static class IsExternalInit { }
}
#endif
