using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiniTui;

/// <summary>
/// VT input mode, bracketed paste, non-blocking reads and the clipboard on Windows (1.x spec §4.4–4.5).
/// Source-generated P/Invokes, so they're AOT-safe.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsConsoleInput
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint WaitObject0 = 0;
    private const uint CfUnicodeText = 13;
    private const int ClipboardTries = 3;

    private static readonly Lock Gate = new();
    private static nint s_input;
    private static nint s_output;
    private static uint s_savedMode;
    private static bool s_active;
    private static bool s_exitHooked;

    /// <summary>True while the console is in VT input mode.</summary>
    public static bool IsActive
    {
        get
        {
            lock (Gate) return s_active;
        }
    }

    /// <summary>
    /// Save the input mode and switch to VT input: no line editing, echo or Ctrl+C processing, and window size
    /// events on. Then turn on bracketed paste, if stdout is a console with VT processing.
    /// </summary>
    /// <returns>False if stdin isn't a console or the console refuses VT input; the mode is then unchanged.</returns>
    public static bool TryEnterVtInput()
    {
        lock (Gate)
        {
            if (s_active) return true;
            try
            {
                var input = GetStdHandle(StdInputHandle);
                if (!IsHandle(input) || !GetConsoleMode(input, out var saved)) return false;
                var mode = (saved | EnableVirtualTerminalInput | EnableWindowInput)
                           & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput);
                if (!SetConsoleMode(input, mode)) return false;

                s_input = input;
                s_savedMode = saved;
                s_active = true;
                var output = GetStdHandle(StdOutputHandle);
                s_output = IsHandle(output) && GetConsoleMode(output, out var outputMode)
                           && (outputMode & EnableVirtualTerminalProcessing) != 0 ? output : 0;
                Write("\e[?2004h"u8);

                // A backstop for sources used without a console session, which restores on exit itself
                if (!s_exitHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
                    s_exitHooked = true;
                }
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Turn bracketed paste off and put back the saved input mode. Idempotent, and never throws.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Runs on exit and signal paths, which must not throw.")]
    public static void Restore()
    {
        lock (Gate)
        {
            if (!s_active) return;
            try
            {
                Write("\e[?2004l"u8);
            }
            catch (Exception) { }
            try
            {
                SetConsoleMode(s_input, s_savedMode);
            }
            catch (Exception) { }
            s_active = false;
            s_output = 0;
        }
    }

    /// <summary>Wait up to <paramref name="timeoutMs"/> for input records.</summary>
    /// <returns>True if records are ready to read.</returns>
    public static bool Wait(int timeoutMs)
    {
        nint input;
        lock (Gate) input = s_input;
        return WaitForSingleObject(input, (uint)Math.Max(0, timeoutMs)) == WaitObject0;
    }

    /// <summary>Read the pending records into <paramref name="records"/>, without blocking.</summary>
    /// <returns>Records read; 0 if none were pending; −1 on an error.</returns>
    public static int Read(Span<InputRecord> records)
    {
        nint input;
        lock (Gate) input = s_input;
        if (!GetNumberOfConsoleInputEvents(input, out var pending)) return -1;
        if (pending == 0) return 0;
        fixed (InputRecord* p = records)
        {
            var count = (uint)Math.Min(records.Length, (int)Math.Min(pending, int.MaxValue));
            return ReadConsoleInputW(input, p, count, out var read) ? (int)read : -1;
        }
    }

    /// <summary>The clipboard's text, or null if it has none or stays locked by another program.</summary>
    public static string? ReadClipboardText()
    {
        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText)) return null;
            for (var attempt = 0; attempt < ClipboardTries; attempt++)
            {
                if (attempt > 0) Thread.Sleep(10);
                if (!OpenClipboard(0)) continue;
                try
                {
                    var data = GetClipboardData(CfUnicodeText);
                    if (data == 0) return null;
                    var text = GlobalLock(data);
                    if (text == 0) return null;
                    try
                    {
                        return new string((char*)text);
                    }
                    finally
                    {
                        GlobalUnlock(data);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static bool IsHandle(nint handle) => handle != 0 && handle != -1;

    private static void Write(ReadOnlySpan<byte> bytes)
    {
        if (s_output == 0) return;
        fixed (byte* p = bytes)
        {
            var offset = 0;
            while (offset < bytes.Length && WriteFile(s_output, p + offset, (uint)(bytes.Length - offset), out var written, 0) && written > 0)
                offset += (int)written;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNumberOfConsoleInputEvents(nint hConsoleInput, out uint lpcNumberOfEvents);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadConsoleInputW(nint hConsoleInput, InputRecord* lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(nint hFile, byte* lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, nint lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();
}
