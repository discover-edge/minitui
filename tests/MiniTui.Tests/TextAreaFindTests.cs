
namespace MiniTui.Tests;

/// <summary>Find in a text area (1.x spec §16.5).</summary>
public class TextAreaFindTests
{
    private static void Press(TextAreaState state, params string[] keys)
    {
        foreach (var key in keys) Assert.True(state.HandleKey(KeyBinding.Parse(key).ToKeyInfo()), key);
    }

    private static void Type(TextAreaState state, string text)
    {
        foreach (var c in text) Assert.True(state.HandleKey(new ConsoleKeyInfo(c, 0, false, false, false)));
    }

    private static TextPosition P(int line, int index) => new(line, index);

    private static (TextPosition Start, TextPosition End) Selection(TextAreaState state) => (state.SelectionStart, state.SelectionEnd);

    private static CellBuffer Draw(TextAreaState state, int width, int height, bool focused = true)
    {
        var buf = new CellBuffer(width, height);
        Widget.TextArea().State(state).Focused(focused).Draw(buf, buf.Area);
        return buf;
    }

    private const string Sample = "one cat\ntwo cats\nthree Cat";

    // Opening and typing

    [Fact]
    public void CtrlF_OpensTheBar_AndTypingFindsFromTheCursor()
    {
        var state = new TextAreaState(Sample) { Cursor = P(1, 0) };

        Press(state, "ctrl+f");
        Assert.True(state.IsFinding);
        Type(state, "cat");

        Assert.Equal((P(1, 4), P(1, 7)), Selection(state));
        Assert.Equal(1, state.MatchIndex);
        Assert.Equal(3, state.MatchCount);
    }

    [Fact]
    public void Typing_SearchesAgainFromWhereTheSearchStarted_Wrapping()
    {
        var state = new TextAreaState(Sample) { Cursor = P(2, 8) };
        Press(state, "ctrl+f");

        Type(state, "c");
        Assert.Equal((P(0, 4), P(0, 5)), Selection(state));
        Type(state, "a");
        Assert.Equal((P(0, 4), P(0, 6)), Selection(state));
        Press(state, "backspace");
        Assert.Equal((P(0, 4), P(0, 5)), Selection(state));
    }

    [Fact]
    public void NoMatches_ClearsTheSelection_AndPutsTheCursorBack()
    {
        var state = new TextAreaState(Sample) { Cursor = P(1, 2) };
        Press(state, "ctrl+f");
        Type(state, "ca");
        Assert.True(state.HasSelection);

        Type(state, "x");

        Assert.False(state.HasSelection);
        Assert.Equal(P(1, 2), state.Cursor);
        Assert.Equal(0, state.MatchCount);
        Assert.Equal(-1, state.MatchIndex);
    }

    [Fact]
    public void OpeningWithASelection_FindsIt_AndKeepsIt()
    {
        var state = new TextAreaState(Sample);
        state.Select(P(1, 0), P(1, 3));

        Press(state, "ctrl+f");

        Assert.Equal("two", state.FindText);
        Assert.Equal((P(1, 0), P(1, 3)), Selection(state));
        Assert.Equal(0, state.MatchIndex);

        // All of the find text is selected, so typing replaces it
        Type(state, "one");
        Assert.Equal("one", state.FindText);
        Assert.Equal((P(0, 0), P(0, 3)), Selection(state));
    }

    [Fact]
    public void OpeningWithASelectionOverLines_KeepsTheLastFindText()
    {
        var state = new TextAreaState(Sample) { FindText = "two" };
        state.Select(P(0, 0), P(2, 1));

        state.OpenFind();

        Assert.Equal("two", state.FindText);
        Assert.Equal((P(1, 0), P(1, 3)), Selection(state));
    }

    // Moving between matches

    [Theory]
    [InlineData("enter")]
    [InlineData("down")]
    [InlineData("f3")]
    public void Next_Wraps(string key)
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");
        Type(state, "cat");

        Press(state, key);
        Assert.Equal(1, state.MatchIndex);
        Press(state, key, key);
        Assert.Equal(0, state.MatchIndex);
    }

    [Theory]
    [InlineData("shift+enter")]
    [InlineData("up")]
    [InlineData("shift+f3")]
    public void Previous_Wraps(string key)
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");
        Type(state, "cat");

        Press(state, key);
        Assert.Equal(2, state.MatchIndex);
        Press(state, key);
        Assert.Equal(1, state.MatchIndex);
    }

    [Fact]
    public void Esc_ClosesTheBar_KeepingTheMatchSelected()
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");
        Type(state, "two");

        Press(state, "esc");

        Assert.False(state.IsFinding);
        Assert.Equal((P(1, 0), P(1, 3)), Selection(state));
        Assert.Equal(P(1, 3), state.Cursor);
        // The next Esc clears the match's selection (1.x spec §10.3), and the one after goes on
        Assert.True(state.HandleKey(KeyBinding.Parse("esc").ToKeyInfo()));
        Assert.False(state.HasSelection);
        Assert.False(state.HandleKey(KeyBinding.Parse("esc").ToKeyInfo()));
    }

    [Fact]
    public void WithTheBarClosed_F3FindsAgain()
    {
        var state = new TextAreaState(Sample) { Cursor = default, FindText = "cat" };
        Assert.False(state.IsFinding);

        Press(state, "f3");
        Assert.Equal(1, state.MatchIndex);
        Press(state, "shift+f3");
        Assert.Equal(0, state.MatchIndex);

        // Typing goes to the text, not the find text
        Type(state, "x");
        Assert.Equal("one x\ntwo cats\nthree Cat", state.Text);
    }

    [Fact]
    public void CtrlF_InTheBar_SelectsTheFindText()
    {
        var state = new TextAreaState(Sample);
        Press(state, "ctrl+f");
        Type(state, "two");

        Press(state, "ctrl+f");
        Type(state, "one");

        Assert.Equal("one", state.FindText);
    }

    [Fact]
    public void KeysTheBarDoesntTake_ArentHandled()
    {
        var state = new TextAreaState(Sample);
        Press(state, "ctrl+f");

        Assert.False(state.HandleKey(KeyBinding.Parse("tab").ToKeyInfo()));
        Assert.False(state.HandleKey(KeyBinding.Parse("ctrl+s").ToKeyInfo()));
        Assert.True(state.IsFinding);
    }

    [Fact]
    public void Paste_GoesToTheFindText_WhileTheBarIsOpen()
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");

        state.Paste("cats");

        Assert.Equal("cats", state.FindText);
        Assert.Equal(Sample, state.Text);
        Assert.Equal((P(1, 4), P(1, 8)), Selection(state));
    }

    [Fact]
    public void FindEnabledFalse_LeavesCtrlFAndF3ToTheApp()
    {
        var state = new TextAreaState(Sample) { FindEnabled = false, FindText = "cat" };

        Assert.False(state.HandleKey(KeyBinding.Parse("ctrl+f").ToKeyInfo()));
        Assert.False(state.HandleKey(KeyBinding.Parse("f3").ToKeyInfo()));
        Assert.False(state.IsFinding);
    }

    // Matching

    [Fact]
    public void SmartCase_IgnoresCaseUntilThePatternHasAnUppercaseLetter()
    {
        var state = new TextAreaState(Sample) { FindText = "cat" };
        Assert.Equal(3, state.MatchCount);

        state.FindText = "Cat";
        Assert.Equal(1, state.MatchCount);
        Assert.Equal((P(2, 6), P(2, 9)), Selection(state));
    }

    [Fact]
    public void Matches_DontOverlap()
    {
        var state = new TextAreaState("aaaa") { FindText = "aa" };

        Assert.Equal(2, state.MatchCount);
    }

    [Fact]
    public void AMatchInsideACluster_IsSkipped()
    {
        // "e" followed by a combining acute accent is one cluster
        var state = new TextAreaState("é e") { FindText = "e" };

        Assert.Equal(1, state.MatchCount);
        Assert.Equal((P(0, 3), P(0, 4)), Selection(state));
    }

    [Fact]
    public void Matches_FollowTheText()
    {
        var state = new TextAreaState(Sample) { FindText = "cat" };
        Assert.Equal(3, state.MatchCount);

        state.Cursor = P(0, 0);
        state.Insert("cat ");
        Assert.Equal(4, state.MatchCount);
        state.Undo();
        Assert.Equal(3, state.MatchCount);
        state.Text = "dog";
        Assert.Equal(0, state.MatchCount);
    }

    // The mouse

    [Fact]
    public void APressOnTheBar_PlacesTheFindCursor_AndAPressInTheTextKeepsTheBarOpen()
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");
        Type(state, "two");
        Draw(state, 30, 4);

        // The field starts after "Find: " on the last row
        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 7, 3)));
        Type(state, "x");
        Assert.Equal("txwo", state.FindText);

        Assert.True(state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 2, 0)));
        Assert.True(state.IsFinding);
        Assert.Equal(P(0, 2), state.Cursor);
    }

    // Drawing

    [Fact]
    public void TheBar_TakesTheLastRow_WithTheCount()
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");
        Type(state, "cat");

        var lines = Draw(state, 30, 4).ToLines().Select(l => l.TrimEnd()).ToArray();

        Assert.Equal(["one cat", "two cats", "three Cat", "Find: cat               1 of 3"], lines);
    }

    [Theory]
    [InlineData("", "Find:")]
    [InlineData("dog", "Find: dog           No matches")]
    public void TheCount_IsEmptyOrNoMatches(string pattern, string bar)
    {
        var state = new TextAreaState(Sample);
        Press(state, "ctrl+f");
        Type(state, pattern);

        Assert.Equal(bar, Draw(state, 30, 4).ToLines()[3].TrimEnd());
    }

    [Fact]
    public void WithoutACurrentMatch_TheCountSaysHowMany()
    {
        var state = new TextAreaState(Sample);
        Press(state, "ctrl+f");
        Type(state, "cat");
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 0, 0));
        Draw(state, 30, 4);
        state.HandleMouse(new MouseEvent(MouseAction.Press, MouseButton.Left, 0, 0));

        Assert.EndsWith("3 matches", Draw(state, 30, 4).ToLines()[3].TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOtherMatches_AreHighlighted_AndTheCurrentOneIsTheSelection()
    {
        var state = new TextAreaState(Sample) { Cursor = default };
        Press(state, "ctrl+f");
        Type(state, "cat");

        var buf = Draw(state, 30, 4);

        Assert.Equal(Theme.Current.Selection.Bg, buf.Get(4, 0).Bg);
        Assert.Equal(Theme.Current.SearchMatch.Bg, buf.Get(4, 1).Bg);
        Assert.Equal(Theme.Current.SearchMatch.Bg, buf.Get(8, 2).Bg);
        Assert.NotEqual(Theme.Current.SearchMatch.Bg, buf.Get(3, 1).Bg);

        Press(state, "esc");
        Assert.NotEqual(Theme.Current.SearchMatch.Bg, Draw(state, 30, 4).Get(4, 1).Bg);
    }

    [Fact]
    public void WhileFinding_TheTerminalCursorIsInTheBar()
    {
        var state = new TextAreaState(Sample);
        Press(state, "ctrl+f");
        Type(state, "ab");

        var buf = Draw(state, 30, 4);

        Assert.Equal((8, 3), buf.CursorRequest);
    }

    [Fact]
    public void BuiltInThemes_SetSearchMatch()
    {
        Assert.All([Theme.Default, Theme.Dark, Theme.Light, Theme.Monochrome], t => Assert.NotEqual(default, t.SearchMatch));
    }
}
