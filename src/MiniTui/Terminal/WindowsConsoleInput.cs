using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiniTui;

/// <summary>
/// VT input mode, bracketed paste, mouse reporting, non-blocking reads and the clipboard on Windows (1.x spec §4.4–4.6).
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
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint WaitObject0 = 0;
    private const uint CfUnicodeText = 13;
    private const int ClipboardTries = 3;
    private const uint GmemMoveable = 0x0002;

    private static readonly Lock Gate = new();
    private static nint s_input;
    private static nint s_output;
    private static uint s_savedMode;
    private static uint s_vtMode;
    private static bool s_mouse;
    private static KeyboardProtocol s_keyboard;
    private static bool s_active;
    private static bool s_suspended;
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
    /// <remarks>After <see cref="Suspend"/>, the mode saved before it is kept (1.x spec §5.3).</remarks>
    public static bool TryEnterVtInput()
    {
        lock (Gate)
        {
            if (s_active) return true;
            try
            {
                var input = GetStdHandle(StdInputHandle);
                if (!IsHandle(input) || !GetConsoleMode(input, out var saved)) return false;
                if (s_suspended) saved = s_savedMode;
                var mode = (saved | EnableVirtualTerminalInput | EnableWindowInput)
                           & ~(EnableProcessedInput | EnableLineInput | EnableEchoInput);
                if (!SetConsoleMode(input, mode)) return false;

                s_input = input;
                s_savedMode = saved;
                s_vtMode = mode;
                s_active = true;
                s_suspended = false;
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

    /// <summary>
    /// Turn mouse reporting on or off, while in VT input mode: the mouse sequences for consoles that report the
    /// mouse as VT, and <c>ENABLE_MOUSE_INPUT</c> with quick edit off for those that report it as records.
    /// </summary>
    public static void SetMouse(bool enabled)
    {
        lock (Gate)
        {
            if (!s_active || s_mouse == enabled) return;
            s_mouse = enabled;
            var mode = enabled
                ? (s_vtMode | EnableMouseInput | EnableExtendedFlags) & ~EnableQuickEditMode
                : s_vtMode | EnableExtendedFlags | (s_savedMode & EnableQuickEditMode);
            SetConsoleMode(s_input, mode);
            Write(enabled ? "\e[?1002h\e[?1006h"u8 : "\e[?1006l\e[?1002l"u8);
        }
    }

    /// <summary>Switch the keyboard protocol (1.x spec §12.5), while in VT input mode. Turned off again on every restore path.</summary>
    public static void SetKeyboard(KeyboardProtocol protocol)
    {
        lock (Gate)
        {
            if (!s_active || s_keyboard == protocol) return;
            Write(KeyboardModes.Off(s_keyboard));
            Write(KeyboardModes.On(protocol));
            s_keyboard = protocol;
        }
    }

    /// <summary>The console window's top-left cell in buffer coordinates, which mouse records are relative to.</summary>
    public static Vec2 WindowOrigin()
    {
        nint output;
        lock (Gate) output = s_output;
        if (output == 0) output = GetStdHandle(StdOutputHandle);
        return GetConsoleScreenBufferInfo(output, out var info) ? new Vec2(info.WindowLeft, info.WindowTop) : default;
    }

    /// <summary>Turn mouse reporting and bracketed paste off and put back the saved input mode. Idempotent, and never throws.</summary>
    public static void Restore() => RestoreCore(keepSaved: false);

    /// <summary>Like <see cref="Restore"/>, but the next <see cref="TryEnterVtInput"/> reuses the saved mode.</summary>
    public static void Suspend() => RestoreCore(keepSaved: true);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Runs on exit and signal paths, which must not throw.")]
    private static void RestoreCore(bool keepSaved)
    {
        lock (Gate)
        {
            if (!s_active)
            {
                if (!keepSaved) s_suspended = false;
                return;
            }
            try
            {
                if (s_mouse) Write("\e[?1006l\e[?1002l"u8);
                Write(KeyboardModes.Off(s_keyboard));
                Write("\e[?2004l"u8);
            }
            catch (Exception) { }
            try
            {
                // With the extended flag, the saved quick edit setting is restored too
                SetConsoleMode(s_input, s_savedMode | EnableExtendedFlags);
            }
            catch (Exception) { }
            s_mouse = false;
            s_keyboard = KeyboardProtocol.Legacy;
            s_active = false;
            s_suspended = keepSaved;
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

    /// <summary>Put text on the clipboard as <c>CF_UNICODETEXT</c>, with <c>\r\n</c> line breaks. Failures are ignored.</summary>
    public static void WriteClipboardText(string text)
    {
        var data = text.ReplaceLineEndings("\r\n");
        try
        {
            for (var attempt = 0; attempt < ClipboardTries; attempt++)
            {
                if (attempt > 0) Thread.Sleep(10);
                if (!OpenClipboard(0)) continue;
                try
                {
                    if (!EmptyClipboard()) return;
                    var bytes = (nuint)((data.Length + 1) * sizeof(char));
                    var memory = GlobalAlloc(GmemMoveable, bytes);
                    if (memory == 0) return;
                    var target = GlobalLock(memory);
                    if (target == 0)
                    {
                        GlobalFree(memory);
                        return;
                    }
                    fixed (char* source = data)
                        System.Buffer.MemoryCopy(source, (void*)target, (long)bytes, (long)bytes);
                    GlobalUnlock(memory);

                    // The clipboard owns the memory once SetClipboardData succeeds
                    if (SetClipboardData(CfUnicodeText, memory) == 0) GlobalFree(memory);
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
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

    [StructLayout(LayoutKind.Explicit, Size = 22)]
    private struct ScreenBufferInfo
    {
        [FieldOffset(10)] public short WindowLeft;
        [FieldOffset(12)] public short WindowTop;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(nint hConsoleOutput, out ScreenBufferInfo lpConsoleScreenBufferInfo);

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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);
}
