namespace MiniTui.Tests;

public class TextTests
{
    [Fact]
    public void Span_ImplicitFromString_IsUnstyled()
    {
        Span span = "hi";

        Assert.Equal(new Span("hi", Style.Default), span);
    }

    [Fact]
    public void Line_FromString_HasOneSpan()
    {
        Line line = "abc";

        Assert.Single(line.Spans);
        Assert.Equal(Alignment.Left, line.Alignment);
        Assert.Equal(3, line.Width);
    }

    [Fact]
    public void Line_Width_SumsSpansByDisplayWidth()
    {
        var line = new Line([new Span("日本", default), "ab", "👍"]);

        Assert.Equal(8, line.Width);
    }

    [Fact]
    public void Line_Width_MatchesDrawnWidth_ForControlCharacters()
    {
        Line line = "a\tb\x1b";
        var buf = new MiniTui.Buffer(10, 1);

        Assert.Equal(line.Width, buf.SetLine(0, 0, line, 10));
    }

    [Fact]
    public void Text_From_SplitsOnNewlines()
    {
        var text = Text.From("one\ntwo\r\nthree");

        Assert.Equal(["one", "two", "three"], text.Lines.Select(l => l.Spans[0].Content));
        Assert.Equal(5, text.Width);
        Assert.Equal(3, text.Height);
    }

    [Fact]
    public void Text_ImplicitConversions()
    {
        Text fromString = "a\nb";
        Text fromLine = new Line("x");

        Assert.Equal(2, fromString.Lines.Count);
        Assert.Single(fromLine.Lines);
    }
}
