namespace MiniTui.Tests;

public class ColorTests
{
    [Fact]
    public void Default_IsReset()
    {
        Assert.Equal(Color.Reset, default);
        Assert.Equal(ColorKind.Reset, Color.Reset.Kind);
    }

    [Theory]
    [InlineData(ConsoleColor.Black, 0)]
    [InlineData(ConsoleColor.DarkRed, 1)]
    [InlineData(ConsoleColor.DarkGreen, 2)]
    [InlineData(ConsoleColor.DarkYellow, 3)]
    [InlineData(ConsoleColor.DarkBlue, 4)]
    [InlineData(ConsoleColor.DarkMagenta, 5)]
    [InlineData(ConsoleColor.DarkCyan, 6)]
    [InlineData(ConsoleColor.Gray, 7)]
    [InlineData(ConsoleColor.DarkGray, 8)]
    [InlineData(ConsoleColor.Red, 9)]
    [InlineData(ConsoleColor.Green, 10)]
    [InlineData(ConsoleColor.Yellow, 11)]
    [InlineData(ConsoleColor.Blue, 12)]
    [InlineData(ConsoleColor.Magenta, 13)]
    [InlineData(ConsoleColor.Cyan, 14)]
    [InlineData(ConsoleColor.White, 15)]
    public void ConsoleColor_ConvertsToNamedColorWithSgrOrder(ConsoleColor consoleColor, int ansiIndex)
    {
        Color color = consoleColor;

        Assert.Equal(ColorKind.Named, color.Kind);
        Assert.Equal(ansiIndex, color.Index);
        Assert.Equal(consoleColor, color.ToConsoleColor());
        Assert.Equal(consoleColor.ToString(), color.ToString());
    }

    [Fact]
    public void NamedProperties_MatchConsoleColors()
    {
        Assert.Equal((Color)ConsoleColor.DarkYellow, Color.DarkYellow);
        Assert.Equal((Color)ConsoleColor.Cyan, Color.Cyan);
        Assert.Equal((Color)ConsoleColor.White, Color.White);
    }

    [Theory]
    [InlineData("#1A2B3C")]
    [InlineData("1a2b3c")]
    public void FromHex_ParsesRgb(string hex)
    {
        Assert.Equal(Color.Rgb(0x1A, 0x2B, 0x3C), Color.FromHex(hex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData(" 123456")]
    public void FromHex_ThrowsFormatException_ForInvalidInput(string hex)
    {
        Assert.Throws<FormatException>(() => Color.FromHex(hex));
    }

    [Fact]
    public void Equality_DistinguishesKinds()
    {
        Assert.NotEqual(Color.Indexed(1), Color.DarkRed);
        Assert.NotEqual(Color.Rgb(0, 0, 0), Color.Black);
        Assert.NotEqual(Color.Reset, Color.Black);
        Assert.Equal(Color.Indexed(200), Color.Indexed(200));
    }

    [Theory]
    [InlineData(0, 0, 0, 16)]
    [InlineData(255, 255, 255, 231)]
    [InlineData(255, 0, 0, 196)]
    [InlineData(95, 135, 175, 67)]
    [InlineData(128, 128, 128, 244)]
    [InlineData(8, 8, 8, 232)]
    [InlineData(238, 238, 238, 255)]
    public void RgbToIndexed_PicksNearestCubeOrGray(byte r, byte g, byte b, byte expected)
    {
        Assert.Equal(Color.Indexed(expected), Color.Rgb(r, g, b).ToMode(ColorMode.Ansi256));
    }

    [Fact]
    public void RgbToIndexed_MatchesBruteForceOverCubeAndGrayRamp()
    {
        var random = new Random(42);
        for (var n = 0; n < 2000; n++)
        {
            var (r, g, b) = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            var best = int.MaxValue;
            for (var i = 16; i < 256; i++)
            {
                var (cr, cg, cb) = Color.IndexedToRgb((byte)i);
                best = Math.Min(best, (r - cr) * (r - cr) + (g - cg) * (g - cg) + (b - cb) * (b - cb));
            }

            var (ar, ag, ab) = Color.IndexedToRgb(Color.NearestIndexed(r, g, b));
            Assert.Equal(best, (r - ar) * (r - ar) + (g - ag) * (g - ag) + (b - ab) * (b - ab));
        }
    }

    [Theory]
    [InlineData(3, ConsoleColor.DarkYellow)]
    [InlineData(9, ConsoleColor.Red)]
    [InlineData(196, ConsoleColor.Red)]
    [InlineData(16, ConsoleColor.Black)]
    [InlineData(231, ConsoleColor.White)]
    [InlineData(244, ConsoleColor.DarkGray)]
    public void IndexedToAnsi16_MapsLowIndicesDirectlyAndOthersByNearestRgb(byte index, ConsoleColor expected)
    {
        Assert.Equal((Color)expected, Color.Indexed(index).ToMode(ColorMode.Ansi16));
    }

    [Fact]
    public void RgbToAnsi16_GoesThroughIndexed()
    {
        Assert.Equal(Color.Red, Color.Rgb(250, 10, 10).ToMode(ColorMode.Ansi16));
        Assert.Equal(Color.Black, Color.Rgb(0, 0, 0).ToMode(ColorMode.Ansi16));
    }

    [Theory]
    [InlineData(ColorMode.None)]
    [InlineData(ColorMode.Ansi16)]
    [InlineData(ColorMode.Ansi256)]
    [InlineData(ColorMode.TrueColor)]
    public void Downsampling_KeepsReset(ColorMode mode)
    {
        Assert.Equal(Color.Reset, Color.Reset.ToMode(mode));
    }

    [Fact]
    public void Downsampling_None_EmitsNoColor()
    {
        Assert.Equal(Color.Reset, Color.Red.ToMode(ColorMode.None));
        Assert.Equal(Color.Reset, Color.Rgb(1, 2, 3).ToMode(ColorMode.None));
    }

    [Fact]
    public void Downsampling_LeavesColorsTheModeSupports()
    {
        Assert.Equal(Color.Rgb(1, 2, 3), Color.Rgb(1, 2, 3).ToMode(ColorMode.TrueColor));
        Assert.Equal(Color.Indexed(100), Color.Indexed(100).ToMode(ColorMode.Ansi256));
        Assert.Equal(Color.Cyan, Color.Cyan.ToMode(ColorMode.Ansi16));
    }
}
