using System.Text;

namespace MiniTui.Tests;

/// <summary>A console for backend tests. Output goes to a stream that counts writes.</summary>
internal sealed class FakeConsoleHost(int width = 20, int height = 5) : IConsoleHost
{
    public CountingStream Output { get; } = new();
    public int Width { get; set; } = width;
    public int Height { get; set; } = height;
    public bool IsOutputRedirected { get; set; }
    public bool IsInputRedirected { get; set; }
    public bool TreatControlCAsInput { get; set; }
    public Encoding OutputEncoding { get; set; } = Encoding.Latin1;
    public bool IsWindows { get; set; }
    public bool VirtualTerminalSupported { get; set; } = true;
    public uint OutputMode { get; set; } = 0x0003;

    public (int Width, int Height) GetWindowSize() => (Width, Height);
    public Stream OpenStandardOutput() => Output;

    public bool TryEnableVirtualTerminal(out uint originalMode)
    {
        originalMode = OutputMode;
        if (!VirtualTerminalSupported) return false;
        OutputMode |= 0x000C;
        return true;
    }

    public void RestoreOutputMode(uint mode) => OutputMode = mode;
}

/// <summary>A memory stream that records how many times it was written and flushed.</summary>
internal sealed class CountingStream : MemoryStream
{
    public int Writes { get; private set; }
    public int Flushes { get; private set; }
    public bool FailWrites { get; set; }

    // MemoryStream's span overload calls the array overload, so count only there
    public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (FailWrites) throw new IOException("write failed");
        Writes++;
        base.Write(buffer, offset, count);
    }

    public override void Flush()
    {
        Flushes++;
        base.Flush();
    }

    public string Text => Encoding.UTF8.GetString(ToArray());
}
