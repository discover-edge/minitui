using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiniTui;

/// <summary>
/// Windows console mode P/Invokes (§5.5). Source-generated, so they're AOT-safe.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsConsole
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint DisableNewlineAutoReturn = 0x0008;

    /// <summary>
    /// OR in ENABLE_VIRTUAL_TERMINAL_PROCESSING and DISABLE_NEWLINE_AUTO_RETURN on the output handle.
    /// </summary>
    /// <returns>False if the handle isn't a console or the console doesn't support VT.</returns>
    public static bool TryEnableVirtualTerminal(out uint originalMode)
    {
        originalMode = 0;
        var handle = GetStdHandle(StdOutputHandle);
        if (handle == 0 || handle == -1 || !GetConsoleMode(handle, out originalMode)) return false;
        return SetConsoleMode(handle, originalMode | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn);
    }

    /// <summary>Set the output handle's console mode. Failures are ignored.</summary>
    public static void SetOutputMode(uint mode)
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (handle != 0 && handle != -1) SetConsoleMode(handle, mode);
    }

    /// <summary>True if the console accepts VT processing. Leaves the mode unchanged.</summary>
    public static bool SupportsVirtualTerminal()
    {
        if (!TryEnableVirtualTerminal(out var original)) return false;
        SetOutputMode(original);
        return true;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
