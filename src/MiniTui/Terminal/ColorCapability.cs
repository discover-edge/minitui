namespace MiniTui;

/// <summary>
/// Detects how many colors the terminal supports from the environment (§4.2).
/// </summary>
public static class ColorCapability
{
    /// <summary>
    /// Detect the color mode. An explicit mode wins; otherwise <c>NO_COLOR</c>, <c>COLORTERM</c>,
    /// <c>WT_SESSION</c>, <c>TERM_PROGRAM</c> and <c>TERM</c> are checked in that order.
    /// </summary>
    public static ColorMode Detect(ColorMode? explicitMode = null) =>
        Detect(Environment.GetEnvironmentVariable, explicitMode);

    internal static ColorMode Detect(Func<string, string?> getEnv, ColorMode? explicitMode = null)
    {
        if (explicitMode is { } mode) return mode;
        if (!string.IsNullOrEmpty(getEnv("NO_COLOR"))) return ColorMode.None;

        var colorTerm = getEnv("COLORTERM");
        if (string.Equals(colorTerm, "truecolor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(colorTerm, "24bit", StringComparison.OrdinalIgnoreCase))
            return ColorMode.TrueColor;

        if (getEnv("WT_SESSION") is not null) return ColorMode.TrueColor;
        if (getEnv("TERM_PROGRAM") is "iTerm.app" or "WezTerm" or "vscode") return ColorMode.TrueColor;
        if (getEnv("TERM")?.Contains("256color", StringComparison.Ordinal) == true) return ColorMode.Ansi256;
        return ColorMode.Ansi16;
    }
}
