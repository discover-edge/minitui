# Layout

Divide the screen with constraints, and split it into resizable panes.

Fluent builder for creating named layout regions. Sizes are computed with exact integer
arithmetic: regions never overlap, never leave the area, and rounding never drops a cell.

```csharp
var layout = Layout.Vertical(screen)
    .Add("header", Constraint.Length(3))    // Exactly 3 rows (shrunk only if fixed sizes don't fit)
    .Add("content", Constraint.Fill())      // Fill remaining space
    .Add("footer", Constraint.Length(1))
    .Spacing(1)                             // Optional: empty cells between regions
    .Build();                               // Optional: the indexer builds on first use

buf.SetString(layout["header"].X, layout["header"].Y, "Title", Style.Default.Bold());
Widget.List().Items(items).Draw(buf, layout["content"]);

var columns = Layout.Split(area, Direction.Horizontal, Constraint.Percentage(30), Constraint.Fill());
```

| Constraint | Size |
|---|---|
| `Length(n)` | n cells |
| `Percentage(p)`, `Ratio(a, b)` | that share of the space, rounded down |
| `Min(n)` | at least n, then grows like `Fill(1)` |
| `Max(n)` | grows like `Fill(1)`, up to n |
| `Fill(weight)` | shares the leftover in proportion to `weight` |

When the fixed sizes don't fit, `Length`, `Percentage` and `Ratio` shrink proportionally first,
then `Min`. Space that nothing can grow into is placed by `.Flex(Flex.Start | End | Center |
SpaceBetween)`. `.Margin(new Margin(1))` insets the area first.

## Split panes

`Widget.Split(state)` divides an area into two panes with a one-cell divider and returns them for you to draw
into. The user drags the divider (with `WithMouse()`) or moves it with Alt+Shift and the arrows. A
[`SplitState`](xref:MiniTui.SplitState) is sized by `Ratio` (the panes scale with the terminal) or, with `SplitState.Fixed(direction,
cells, pane)`, keeps one pane at a size in cells, such as a sidebar. `MinFirst` and `MinSecond` (3 by default)
limit how small the user can make each pane, and `Changed` fires when they move it. Splits nest:

```csharp
private readonly SplitState _sidebar = SplitState.Fixed(Direction.Horizontal, 24);

public override bool HandleMouse(MouseEvent mouse) => _sidebar.HandleMouse(mouse);
public override bool HandleKey(ConsoleKeyInfo key) => _sidebar.HandleKey(key);

public override void Render(Buffer buf, Rect area, RenderContext ctx)
{
    var (left, right) = Widget.Split(_sidebar).Draw(buf, area);
    ctx.Render(_files, buf, left);
    ctx.Render(_preview, buf, right);
}
```

Focused children see keys first, and the list, scroll and tree states take arrows with any modifier, so a pane
built on them passes Alt keys on to keep Alt+Shift+arrows for the split.
