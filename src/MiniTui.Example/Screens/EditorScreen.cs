using MiniTui;

namespace MiniTui.Example;

/// <summary>A text area set up as an editor (1.x spec §16): indentation, line numbers, a scrollbar and find.</summary>
internal sealed class EditorScreen : Screen
{
    private const string Sample = """
        # A sample config: Tab and Shift+Tab indent and outdent the selected lines
        server:
          host: 0.0.0.0
          port: 8080
          tls:
            enabled: true
            cert: /etc/app/cert.pem
            key: /etc/app/key.pem

        database:
          url: postgres://localhost:5432/app
          pool:
            min: 2
            max: 16
          timeouts:
            connect: 5s
            query: 30s

        logging:
          level: info
          format: json
          outputs:
            - stdout
            - file: /var/log/app.log

        features:
          search: true
          exports: false
          beta:
            - dark-mode
            - command-palette
        """;

    private readonly EditorField _editor;

    public override string Title => "Editor";

    public EditorScreen()
    {
        _editor = AddChild(new EditorField(Sample));
        Focus.Add(_editor);
        Keys.Add("ctrl+s", "Save", () => App.Toasts.Push("Saved (nothing was written)", ToastLevel.Success));
    }

    public override void Render(Buffer buf, Rect area, RenderContext ctx)
    {
        var rows = Layout.Vertical(area.Inner(new Margin(1, 0)))
            .Add("editor", Constraint.Min(3))
            .Add("help", Constraint.Length(1))
            .Build();
        ctx.Render(_editor, buf, rows["editor"]);
        buf.SetString(rows["help"].X, rows["help"].Y, "Tab / Shift+Tab indent · Ctrl+F find, F3 / Shift+F3 again · Ctrl+S save · Esc back", ctx.Theme.TextMuted);
    }

    private sealed class EditorField(string text) : Component
    {
        private readonly TextAreaState _state = new() { TabIndents = true, AutoIndent = true, IndentSize = 2, Text = text, Cursor = default };

        public override bool CapturesText => true;
        public override bool HandleKey(ConsoleKeyInfo key) => _state.HandleKey(key);
        public override bool HandlePaste(string text) => _state.Paste(text);
        public override bool HandleMouse(MouseEvent mouse) => _state.HandleMouse(mouse);

        public override void Render(Buffer buf, Rect area, RenderContext ctx)
        {
            var cursor = _state.Cursor;
            Widget.TextArea()
                .State(_state)
                .LineNumbers()
                .Block(Widget.Block().Title($" config.yaml — Ln {cursor.Line + 1}, Col {cursor.Index + 1} "))
                .Focused(ctx.Focused)
                .Draw(buf, area);
        }
    }
}
