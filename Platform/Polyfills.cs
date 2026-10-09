using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace System
{
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
            _value = fromEnd ? ~value : value;
        }

        public static Index Start => new Index(0);
        public static Index End => new Index(~0);
        public static Index FromStart(int value) => new Index(value);
        public static Index FromEnd(int value) => new Index(value, true);

        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

        public override bool Equals(object? value) => value is Index index && _value == index._value;
        public bool Equals(Index other) => _value == other._value;
        public override int GetHashCode() => _value;
        public static implicit operator Index(int value) => FromStart(value);
        public override string ToString() => IsFromEnd ? "^" + Value.ToString() : Value.ToString();
    }

    public readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End { get; }

        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public static Range All => new Range(Index.Start, Index.End);
        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end) => new Range(Index.Start, end);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public override bool Equals(object? value) => value is Range r && r.Start.Equals(Start) && r.End.Equals(End);
        public bool Equals(Range other) => other.Start.Equals(Start) && other.End.Equals(End);
        public override int GetHashCode() => Start.GetHashCode() * 397 ^ End.GetHashCode();
        public override string ToString() => $"{Start}..{End}";
    }
}

namespace System.Runtime.InteropServices
{
    public delegate IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath);

    public static class NativeLibrary
    {
        [DllImport("api-ms-win-core-libraryloader-l2-1-0.dll", EntryPoint = "LoadPackagedLibrary", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadPackagedLibrary(string lpLibFileName, int reserved = 0);

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        public static bool TryLoad(string libraryName, out IntPtr handle)
        {
            try
            {
                handle = LoadPackagedLibrary(libraryName, 0);
                if (handle != IntPtr.Zero) return true;
            }
            catch { }

            try
            {
                handle = LoadLibraryW(libraryName);
                if (handle != IntPtr.Zero) return true;
            }
            catch { }

            handle = IntPtr.Zero;
            return false;
        }

        public static IntPtr Load(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            TryLoad(libraryName, out var handle);
            return handle;
        }

        public static bool TryGetExport(IntPtr handle, string name, out IntPtr address)
        {
            try
            {
                address = GetProcAddress(handle, name);
                return address != IntPtr.Zero;
            }
            catch
            {
                address = IntPtr.Zero;
                return false;
            }
        }

        public static void SetDllImportResolver(Assembly assembly, DllImportResolver resolver)
        {
            // Packaged UWP DLL resolution handled natively
        }
    }
}
