
namespace MiniTui.Tests;

/// <summary>The text area's editor features: indentation, line numbers and the scrollbar (1.x spec §16.3, §16.4).</summary>
public class TextAreaEditorTests
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

    private static TextAreaState Editor(string text, int indent = 4) => new() { TabIndents = true, IndentSize = indent, Text = text };

    private static MouseEvent M(MouseAction action, int x, int y, MouseButton button = MouseButton.Left) => new(action, button, x, y);

    // Tab and Shift+Tab

    [Theory]
    [InlineData(0, "    ab", 4)]
    [InlineData(1, "a   b", 4)]
    [InlineData(2, "ab  ", 4)]
    public void Tab_InsertsSpacesToTheNextStop(int index, string text, int cursor)
    {
        var state = Editor("ab");
        state.Cursor = P(0, index);

        Press(state, "tab");

        Assert.Equal(text, state.Text);
        Assert.Equal(P(0, cursor), state.Cursor);
    }

    [Fact]
    public void Tab_UsesTheIndentSize()
    {
        var state = Editor("x", indent: 2);
        state.Cursor = P(0, 1);

        Press(state, "tab");

        Assert.Equal("x ", state.Text);
    }

    [Fact]
    public void Tab_ReplacesASelectionWithinOneLine()
    {
        var state = Editor("abcd");
        state.Select(P(0, 1), P(0, 3));

        Press(state, "tab");

        Assert.Equal("a   d", state.Text);
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void Tab_OverSeveralLines_IndentsEach_LeavingEmptyLinesAndALastLineAtColumnZero()
    {
        var state = Editor("one\n\ntwo\nthree");
        state.Select(P(0, 1), P(3, 0));

        Press(state, "tab");

        Assert.Equal("    one\n\n    two\nthree", state.Text);
        Assert.Equal(P(0, 5), state.SelectionAnchor);
        Assert.Equal(P(3, 0), state.Cursor);
    }

    [Fact]
    public void ShiftTab_OutdentsToThePreviousStop()
    {
        var state = Editor("      six\n  two\nnone\n    four");
        state.Select(P(0, 7), P(3, 6));

        Press(state, "shift+tab");

        Assert.Equal("    six\ntwo\nnone\nfour", state.Text);
        Assert.Equal(P(0, 5), state.SelectionAnchor);
        Assert.Equal(P(3, 2), state.Cursor);
    }

    [Fact]
    public void ShiftTab_OnTheCursorLine_MovesTheCursorWithTheText_OrToTheStart()
    {
        var state = Editor("    abc");
        state.Cursor = P(0, 2);

        Press(state, "shift+tab");
        Assert.Equal("abc", state.Text);
        Assert.Equal(P(0, 0), state.Cursor);

        Press(state, "shift+tab");
        Assert.Equal("abc", state.Text);
        Assert.False(state.CanRedo);
    }

    [Fact]
    public void WithoutTabIndents_TabAndShiftTabArentHandled()
    {
        var state = new TextAreaState("ab");

        Assert.False(state.HandleKey(KeyBinding.Parse("tab").ToKeyInfo()));
        Assert.False(state.HandleKey(KeyBinding.Parse("shift+tab").ToKeyInfo()));
        Assert.False(Editor("ab").HandleKey(KeyBinding.Parse("ctrl+tab").ToKeyInfo()));
        Assert.Equal("ab", state.Text);
    }

    [Fact]
    public void EachIndent_IsItsOwnUndoGroup()
    {
        var state = Editor("a\nb");
        state.SelectAll();

        Press(state, "tab", "tab", "shift+tab");
        Assert.Equal("    a\n    b", state.Text);

        state.Undo();
        Assert.Equal("        a\n        b", state.Text);
        state.Undo();
        Assert.Equal("    a\n    b", state.Text);
        state.Undo();
        Assert.Equal("a\nb", state.Text);
        Assert.Equal(P(0, 0), state.SelectionAnchor);
        Assert.Equal(P(1, 1), state.Cursor);
    }

    // Backspace

    [Theory]
    [InlineData("        x", 8, "    x", 4)]
    [InlineData("      x", 6, "    x", 4)]
    [InlineData("   x", 3, "x", 0)]
    [InlineData(" x", 1, "x", 0)]
    public void Backspace_InTheIndentation_DeletesToThePreviousStop(string text, int index, string after, int cursor)
    {
        var state = Editor(text);
        state.Cursor = P(0, index);

        Press(state, "backspace");

        Assert.Equal(after, state.Text);
        Assert.Equal(P(0, cursor), state.Cursor);
    }

    [Fact]
    public void Backspace_AfterText_OrWithoutTabIndents_DeletesOneCluster()
    {
        var editor = Editor("    a    ");
        Press(editor, "backspace");
        Assert.Equal("    a   ", editor.Text);

        var plain = new TextAreaState("        ");
        Press(plain, "backspace");
        Assert.Equal("       ", plain.Text);
    }

    // Auto-indent

    [Fact]
    public void AutoIndent_CopiesTheIndentation()
    {
        var state = new TextAreaState("    if x:") { AutoIndent = true };

        Press(state, "enter");
        Type(state, "y");

        Assert.Equal("    if x:\n    y", state.Text);
    }

    [Theory]
    [InlineData(2, "  \n    x", 1, 2)]
    [InlineData(0, "\n    x", 1, 0)]
    [InlineData(6, "    x \n    ", 1, 4)]
    public void AutoIndent_TakesNoMoreThanIsBeforeTheCursor(int index, string text, int line, int cursor)
    {
        var state = new TextAreaState(index == 6 ? "    x " : "    x") { AutoIndent = true };
        state.Cursor = P(0, index);

        Press(state, "enter");

        Assert.Equal(text, state.Text);
        Assert.Equal(P(line, cursor), state.Cursor);
    }

    [Fact]
    public void AutoIndent_IsOneUndoGroup_AndPasteDoesntAutoIndent()
    {
        var state = new TextAreaState("  a") { AutoIndent = true };

        Press(state, "enter");
        state.Undo();
        Assert.Equal("  a", state.Text);

        state.Paste("\nb");
        Assert.Equal("  a\nb", state.Text);
    }

    [Fact]
    public void IndentSize_IsLimited_AndSetsTabExpansion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextAreaState { IndentSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextAreaState { IndentSize = 9 });

        var state = new TextAreaState { IndentSize = 2 };
        state.Text = "a\tb";
        Assert.Equal("a b", state.Text);
        state.Paste("\tc");
        Assert.Equal("a b c", state.Text);
    }

    // Focus

    private sealed class Field(TextAreaState state) : Component
    {
        public override bool CapturesText => true;
        public override bool HandleKey(ConsoleKeyInfo key) => state.HandleKey(key);
        public override void Render(CellBuffer buf, Rect area, RenderContext ctx) => Widget.TextArea().State(state).Focused(ctx.Focused).Draw(buf, area);
    }

    private sealed class Form : Screen
    {
        public Field First { get; }
        public Field Second { get; }
        public bool FirstFocused => Focus.IsFocused(First);

        public Form(TextAreaState state)
        {
            First = AddChild(new Field(state));
            Second = AddChild(new Field(new TextAreaState()));
            Focus.Add(First).Add(Second);
        }

        public override void Render(CellBuffer buf, Rect area, RenderContext ctx)
        {
            ctx.Render(First, buf, area with { Height = 3 });
            ctx.Render(Second, buf, area with { Y = 4, Height = 3 });
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tab_StaysInTheTextArea_OnlyWithTabIndents(bool tabIndents)
    {
        var state = new TextAreaState { TabIndents = tabIndents };
        var form = new Form(state);
        using var harness = new ComponentHarness(form, 30, 10);
        harness.RunUntilIdle();

        harness.Press("tab");

        Assert.Equal(tabIndents, form.FirstFocused);
        Assert.Equal(tabIndents ? "    " : "", state.Text);
    }

    // Line numbers

    private static CellBuffer DrawEditor(TextAreaState state, int width, int height, bool lineNumbers = true, BlockBuilder? block = null, WrapMode wrap = WrapMode.Word)
    {
        var buf = new CellBuffer(width, height);
        var builder = Widget.TextArea().State(state).LineNumbers(lineNumbers).Wrap(wrap).Focused();
        if (block is not null) builder.Block(block);
        builder.Draw(buf, buf.Area);
        return buf;
    }

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => $"l{i}"));

    [Theory]
    [InlineData(9, 3)]
    [InlineData(10, 3)]
    [InlineData(99, 3)]
    [InlineData(100, 4)]
    public void TheGutter_IsAsWideAsTheLastNumber(int count, int width)
    {
        var state = new TextAreaState(Lines(count)) { Cursor = default };

        var lines = DrawEditor(state, 20, 3).ToLines();

        Assert.Equal(new string(' ', width - 2) + "1 l1", lines[0].TrimEnd());
        Assert.Equal(new string(' ', width - 2) + "2 l2", lines[1].TrimEnd());
    }

    [Fact]
    public void TheCursorsLineNumber_UsesTheTextStyle_AndTheOthersAreMuted()
    {
        var state = new TextAreaState("a\nb") { Cursor = P(1, 0) };

        var buf = DrawEditor(state, 10, 2);

        Assert.Equal(Theme.Current.TextMuted.Fg ?? Color.Reset, buf.Get(1, 0).Fg);
        Assert.Equal(Theme.Current.Text.Fg ?? Theme.Current.InputFocused.Fg ?? Color.Reset, buf.Get(1, 1).Fg);
    }

    [Fact]
    public void WrappedRows_HaveABlankGutter_AndWrapInTheNarrowerWidth()
    {
        var state = new TextAreaState("abcdefgh\nx") { Cursor = default };

        var lines = DrawEditor(state, 7, 4).ToLines().Select(l => l.TrimEnd()).ToArray();

        Assert.Equal([" 1 abcd", "   efgh", " 2 x", ""], lines);
    }

    [Fact]
    public void WithoutWrapping_TheGutterStaysWhileTheTextScrolls()
    {
        var state = new TextAreaState("abcdefghij");

        var lines = DrawEditor(state, 8, 1, wrap: WrapMode.None).ToLines();

        Assert.Equal(" 1 ghij", lines[0].TrimEnd());
    }

    [Fact]
    public void TooNarrow_TheGutterIsLeftOut()
    {
        var state = new TextAreaState("ab") { Cursor = default };

        Assert.Equal("ab", DrawEditor(state, 3, 1).ToLines()[0].TrimEnd());
    }

    [Fact]
    public void APressInTheGutter_GoesToTheStartOfTheRow_AndADragFromThereSelects()
    {
        var state = new TextAreaState("abcdefgh\nxyz") { Cursor = default };
        DrawEditor(state, 7, 4);

        Assert.True(state.HandleMouse(M(MouseAction.Press, 1, 1)));
        Assert.Equal(P(0, 4), state.Cursor);

        Assert.True(state.HandleMouse(M(MouseAction.Drag, 5, 2)));
        Assert.Equal(P(0, 4), state.SelectionAnchor);
        Assert.Equal(P(1, 2), state.Cursor);
    }

    // The scrollbar

    [Fact]
    public void TheScrollbar_IsDrawnOnTheBorder_OnlyWhenTheTextDoesntFit()
    {
        var full = DrawEditor(new TextAreaState(Lines(20)), 12, 6, lineNumbers: false, block: Widget.Block());
        var fits = DrawEditor(new TextAreaState(Lines(3)), 12, 6, lineNumbers: false, block: Widget.Block());
        var bare = DrawEditor(new TextAreaState(Lines(20)), 12, 6, lineNumbers: false);

        Assert.All(Enumerable.Range(1, 4), y => Assert.NotEqual("│", full.Get(11, y).Symbol));
        Assert.All(Enumerable.Range(1, 4), y => Assert.Equal("│", fits.Get(11, y).Symbol));
        Assert.DoesNotContain(bare.ToLines(), l => l.Contains('█', StringComparison.Ordinal));
    }

    [Fact]
    public void DraggingTheThumb_Scrolls_WithoutMovingTheCursor_UntilTheCursorMoves()
    {
        // 20 lines in 4 rows: the thumb is 1 cell on a 4-cell track, rows 1–4 of column 11
        var state = new TextAreaState(Lines(20)) { Cursor = default };
        DrawEditor(state, 12, 6, lineNumbers: false, block: Widget.Block());

        Assert.True(state.HandleMouse(M(MouseAction.Press, 11, 1)));
        Assert.True(state.HandleMouse(M(MouseAction.Drag, 11, 4)));
        Assert.True(state.HandleMouse(M(MouseAction.Release, 11, 4)));
        DrawEditor(state, 12, 6, lineNumbers: false, block: Widget.Block());
        Assert.Equal(16, state.ScrollOffset);
        Assert.Equal(P(0, 0), state.Cursor);

        Press(state, "down");
        DrawEditor(state, 12, 6, lineNumbers: false, block: Widget.Block());
        Assert.Equal(1, state.ScrollOffset);
    }

    [Fact]
    public void APressOnTheTrack_Pages()
    {
        var state = new TextAreaState(Lines(20)) { Cursor = default };
        DrawEditor(state, 12, 6, lineNumbers: false, block: Widget.Block());

        Assert.True(state.HandleMouse(M(MouseAction.Press, 11, 4)));
        DrawEditor(state, 12, 6, lineNumbers: false, block: Widget.Block());

        Assert.Equal(4, state.ScrollOffset);
        Assert.Equal(P(0, 0), state.Cursor);
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void Snapshot_GutterAndScrollbar()
    {
        var state = new TextAreaState(Lines(12)) { Cursor = P(1, 1) };

        var lines = DrawEditor(state, 12, 5, block: Widget.Block()).ToLines();

        Assert.Equal(
        [
            "┌──────────┐",
            "│  1 l1    █",
            "│  2 l2    ░",
            "│  3 l3    ░",
            "└──────────┘",
        ], lines);
    }
}
