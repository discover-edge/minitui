using Buffer = MiniTui.Buffer;

namespace MiniTui.Tests;

public class SelectionStateTests
{
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
    private static ConsoleKeyInfo Char(char c) => new(c, 0, false, false, false);

    [Fact]
    public void NextAndPrevious_StopAtTheEnds()
    {
        var s = new SelectionState();

        s.Previous(3);
        Assert.Equal(0, s.Selected);
        s.Next(3); s.Next(3); s.Next(3);
        Assert.Equal(2, s.Selected);
    }

    [Fact]
    public void HandleKey_CoversTheNavigationKeys()
    {
        var s = new SelectionState();

        Assert.True(s.HandleKey(Char('j'), 10));
        Assert.Equal(1, s.Selected);
        Assert.True(s.HandleKey(Key(ConsoleKey.End), 10));
        Assert.Equal(9, s.Selected);
        Assert.True(s.HandleKey(Char('k'), 10));
        Assert.Equal(8, s.Selected);
        Assert.True(s.HandleKey(Key(ConsoleKey.Home), 10));
        Assert.Equal(0, s.Selected);
        Assert.False(s.HandleKey(Char('x'), 10));
        Assert.False(s.HandleKey(new ConsoleKeyInfo('j', ConsoleKey.J, false, false, control: true), 10));
    }

    [Fact]
    public void PageDown_MovesByTheViewportFromTheLastDraw()
    {
        var s = new SelectionState();
        var items = Enumerable.Range(0, 50).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Widget.List().Items(items).State(s).Draw(new Buffer(10, 7), new Rect(0, 0, 10, 7));

        s.PageDown(items.Count);

        Assert.Equal(5, s.ViewportHeight);
        Assert.Equal(5, s.Selected);
    }

    [Fact]
    public void Invariant_HoldsAfterEveryDraw()
    {
        var random = new Random(5);
        var s = new SelectionState();
        for (var i = 0; i < 2000; i++)
        {
            var count = random.Next(0, 40);
            var height = random.Next(0, 15);
            s.Selected = random.Next(-5, 50);
            s.Offset = random.Next(-5, 50);
            var items = Enumerable.Repeat("x", count).ToList();

            Widget.List().Items(items).State(s).Block(Widget.Block().Borders(Borders.None))
                .Draw(new Buffer(10, 20), new Rect(0, 0, 10, height));

            if (count == 0) Assert.Equal(0, s.Selected);
            else Assert.InRange(s.Selected, 0, count - 1);
            if (height > 0 && count > 0) Assert.InRange(s.Selected, s.Offset, s.Offset + height - 1);
            Assert.True(s.Offset >= 0);
        }
    }
}

public class ScrollAndTabStateTests
{
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    [Fact]
    public void ScrollState_IsClampedToTheContentAtDraw()
    {
        var s = new ScrollState { Offset = 100 };

        Widget.Paragraph().Text("1\n2\n3\n4\n5").State(s).Draw(new Buffer(5, 2), new Rect(0, 0, 5, 2));

        Assert.Equal(3, s.Offset);
        Assert.Equal(5, s.ContentHeight);
        Assert.Equal(2, s.ViewportHeight);
        s.HandleKey(Key(ConsoleKey.Home));
        Assert.Equal(0, s.Offset);
        s.HandleKey(Key(ConsoleKey.PageDown));
        Assert.Equal(2, s.Offset);
    }

    [Fact]
    public void TabBarState_ClampsAndWritesCount()
    {
        var s = new TabBarState { Selected = 9 };

        Widget.TabBar().Tabs("a", "b", "c").State(s).Draw(new Buffer(20, 2), new Rect(0, 0, 20, 2));

        Assert.Equal(3, s.Count);
        Assert.Equal(2, s.Selected);
        Assert.True(s.HandleKey(Key(ConsoleKey.RightArrow)));
        Assert.Equal(2, s.Selected);
        Assert.True(s.HandleKey(new ConsoleKeyInfo('h', ConsoleKey.H, false, false, false)));
        Assert.Equal(1, s.Selected);
    }
}

public class TextFieldStateTests
{
    private static ConsoleKeyInfo Char(char c) => new(c, 0, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey key, bool control = false) => new('\0', key, false, false, control);

    private static TextFieldState Type(string text)
    {
        var s = new TextFieldState();
        foreach (var c in text) s.HandleKey(Char(c));
        return s;
    }

    [Theory]
    [InlineData("héllo")]
    [InlineData("日本語")]
    [InlineData("😀👍")]
    [InlineData("Ünïcödé")]
    public void D8_AcceptsAnyNonControlCharacter_IncludingSurrogatePairs(string text)
    {
        Assert.Equal(text, Type(text).Text);
    }

    [Fact]
    public void D8_LoneSurrogatesAreDropped()
    {
        var s = new TextFieldState();

        s.HandleKey(Char('\uDC00'));
        s.HandleKey(Char('\uD83D'));
        s.HandleKey(Char('a'));

        Assert.Equal("a", s.Text);
    }

    [Fact]
    public void ControlAndCommandKeysAreNotTyped()
    {
        var s = new TextFieldState();

        Assert.False(s.HandleKey(Char('\t')));
        Assert.False(s.HandleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, alt: true, false)));
        Assert.True(s.HandleKey(new ConsoleKeyInfo('@', ConsoleKey.Q, false, alt: true, control: true))); // AltGr
        Assert.Equal("@", s.Text);
    }

    [Fact]
    public void CursorMovesAndDeletesByGraphemeCluster()
    {
        var s = Type("aéb"); // a, e + combining acute, b

        s.HandleKey(Key(ConsoleKey.LeftArrow));
        s.HandleKey(Key(ConsoleKey.LeftArrow));
        Assert.Equal(1, s.Cursor);
        s.HandleKey(Key(ConsoleKey.Delete));
        Assert.Equal("ab", s.Text);

        var emoji = Type("x👍🏽y");
        emoji.HandleKey(Key(ConsoleKey.LeftArrow));
        emoji.HandleKey(Key(ConsoleKey.Backspace));
        Assert.Equal("xy", emoji.Text);
        Assert.Equal(1, emoji.Cursor);
    }

    [Fact]
    public void CursorSetter_SnapsToAClusterBoundary()
    {
        var s = new TextFieldState("a👍b") { Cursor = 2 };

        Assert.Equal(3, s.Cursor);
    }

    [Fact]
    public void CtrlArrows_MoveByWord()
    {
        var s = new TextFieldState("one two  three");

        s.HandleKey(Key(ConsoleKey.LeftArrow, control: true));
        Assert.Equal(9, s.Cursor);
        s.HandleKey(Key(ConsoleKey.LeftArrow, control: true));
        Assert.Equal(4, s.Cursor);
        s.HandleKey(Key(ConsoleKey.RightArrow, control: true));
        Assert.Equal(7, s.Cursor);
    }

    [Theory]
    [InlineData('\b', ConsoleKey.Backspace, true)]   // Ctrl+Backspace on Unix (^H)
    [InlineData('\x7f', ConsoleKey.Backspace, true)] // Ctrl+Backspace on Windows
    [InlineData('\x17', ConsoleKey.W, true)]         // Ctrl+W
    [InlineData('\x17', (ConsoleKey)0, false)]       // Ctrl+W as a bare control character
    public void DeletePreviousWord(char c, ConsoleKey key, bool control)
    {
        var s = new TextFieldState("git commit  ");

        s.HandleKey(new ConsoleKeyInfo(c, key, false, false, control));

        Assert.Equal("git ", s.Text);
    }

    [Fact]
    public void PlainBackspace_DeletesOneCharacter_OnEveryPlatform()
    {
        var unix = new TextFieldState("ab");
        unix.HandleKey(new ConsoleKeyInfo('\x7f', ConsoleKey.Backspace, false, false, false));
        var windows = new TextFieldState("ab");
        windows.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("a", unix.Text);
        Assert.Equal("a", windows.Text);
    }

    [Fact]
    public void CtrlU_AndCtrlK_DeleteToTheStartAndEnd()
    {
        var s = new TextFieldState("hello world") { Cursor = 5 };

        s.HandleKey(new ConsoleKeyInfo('\x0b', ConsoleKey.K, false, false, true));
        Assert.Equal("hello", s.Text);
        s.Cursor = 2;
        s.HandleKey(new ConsoleKeyInfo('\x15', ConsoleKey.U, false, false, true));
        Assert.Equal("llo", s.Text);
        Assert.Equal(0, s.Cursor);
    }

    [Fact]
    public void Widget_ScrollsByDisplayWidth_AndShowsTheCursor()
    {
        var s = new TextFieldState("日本語テキスト");
        var buf = new Buffer(6, 1);

        Widget.TextField().State(s).Focused().Draw(buf, buf.Area);

        // 14 columns of text with the cursor after them: columns 9-14 are visible, and キ (8-9) is cut, so blank
        Assert.Equal(9, s.ScrollOffset);
        Assert.Equal(" スト ", buf.ToLines()[0]);
        Assert.Equal((5, 0), buf.CursorRequest);
        Assert.True(buf.Get(5, 0).Modifiers.HasFlag(Modifiers.Reversed));
    }
}

public class CommandLineStateTests
{
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static ParsedCommand? Submit(CommandLineState s, string text)
    {
        s.Text = text;
        s.HandleKey(Key(ConsoleKey.Enter), out var command);
        return command;
    }

    [Fact]
    public void D13_HistoryIsImmutable_AndSeedingCopies()
    {
        var seed = new List<string> { ":a" };
        var s = new CommandLineState(seed);
        var before = s.History;

        Submit(s, ":b");
        seed.Add(":c");

        Assert.Equal([":a"], before);
        Assert.Equal([":a", ":b"], s.History);
    }

    [Fact]
    public void Enter_SubmitsParsedCommand_AndClears()
    {
        var s = new CommandLineState();

        var command = Submit(s, ":open file.txt");

        Assert.Equal("open", command?.Name);
        Assert.Equal("", s.Text);
    }

    [Fact]
    public void ConsecutiveDuplicatesAreNotAdded_AndHistoryIsCapped()
    {
        var s = new CommandLineState { MaxHistory = 3 };

        foreach (var line in new[] { ":a", ":a", ":b", ":c", ":d" }) Submit(s, line);

        Assert.Equal([":b", ":c", ":d"], s.History);
    }

    [Fact]
    public void UpAndDown_BrowseHistory()
    {
        var s = new CommandLineState([":one", ":two"]);

        s.HandleKey(Key(ConsoleKey.UpArrow));
        Assert.Equal(":two", s.Text);
        s.HandleKey(Key(ConsoleKey.UpArrow));
        s.HandleKey(Key(ConsoleKey.UpArrow));
        Assert.Equal(":one", s.Text);
        s.HandleKey(Key(ConsoleKey.DownArrow));
        s.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal("", s.Text);
    }
}
