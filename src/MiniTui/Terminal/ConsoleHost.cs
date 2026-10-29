using System.Text;

namespace MiniTui;

/// <summary>
/// The parts of <see cref="Console"/> the backends depend on, so they can be tested without a real terminal.
/// </summary>
internal interface IConsoleHost
{
    bool IsOutputRedirected { get; }
    bool IsInputRedirected { get; }
    bool TreatControlCAsInput { get; set; }
    Encoding OutputEncoding { get; set; }
    (int Width, int Height) GetWindowSize();
    Stream OpenStandardOutput();

    /// <summary>True when the Windows console modes below apply.</summary>
    bool IsWindows { get; }

    /// <summary>Enable VT processing on the output handle. Returns false if the console refused.</summary>
    bool TryEnableVirtualTerminal(out uint originalMode);

    void RestoreOutputMode(uint mode);
}

/// <summary>
/// The real <see cref="Console"/>.
/// </summary>
internal sealed class SystemConsoleHost : IConsoleHost
{
    public static SystemConsoleHost Instance { get; } = new();

    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool TreatControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }

    public Encoding OutputEncoding
    {
        get => Console.OutputEncoding;
        set => Console.OutputEncoding = value;
    }

    public (int Width, int Height) GetWindowSize()
    {
        try
        {
            var (width, height) = (Console.WindowWidth, Console.WindowHeight);
            if (width > 0 && height > 0) return (width, height);
        }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }
        return (80, 24);
    }

    public Stream OpenStandardOutput() => Console.OpenStandardOutput();

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool TryEnableVirtualTerminal(out uint originalMode)
    {
        originalMode = 0;
        return OperatingSystem.IsWindows() && WindowsConsole.TryEnableVirtualTerminal(out originalMode);
    }

    public void RestoreOutputMode(uint mode)
    {
        if (OperatingSystem.IsWindows()) WindowsConsole.SetOutputMode(mode);
    }
}
