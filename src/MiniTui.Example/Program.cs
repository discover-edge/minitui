using MiniTui;

// === Demo Application ===
// Shows off the MiniTui framework with multiple views

var demoIndex = 0;
var demos = new (string Name, Action<Terminal> Run)[]
{
    ("Counter", RunCounter),
    ("List Selection", RunList),
    ("Progress Bars", RunProgress),
    ("Form Demo", RunForm),
    ("Dashboard", RunDashboard),
};

// Demo picker
using var terminal = new Terminal();
var running = true;

while (running)
{
    terminal.Draw(buf =>
    {
        buf.DrawBoxWithTitle(buf.Area, " MiniTui Demos ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

        var inner = buf.Area.Shrink(2);
        buf.DrawText(inner.X, inner.Y, "Select a demo:", ConsoleColor.White);

        for (var i = 0; i < demos.Length; i++)
        {
            var y = inner.Y + 2 + i;
            var prefix = i == demoIndex ? "► " : "  ";
            var fg = i == demoIndex ? ConsoleColor.Cyan : ConsoleColor.Gray;
            buf.DrawText(inner.X, y, $"{prefix}{demos[i].Name}", fg);
        }

        Widgets.StatusBar(buf, buf.Area, "[↑/↓] Navigate  [Enter] Select  [q] Quit");
    });

    var key = Input.Poll();
    if (key != null)
    {
        switch (key.Value.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                demoIndex = (demoIndex - 1 + demos.Length) % demos.Length;
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                demoIndex = (demoIndex + 1) % demos.Length;
                break;
            case ConsoleKey.Enter:
                demos[demoIndex].Run(terminal);
                terminal.Invalidate();
                break;
        }

        if (Input.IsQuit(key))
            running = false;
    }

    Thread.Sleep(16);
}

// === Demo Implementations ===

void RunCounter(Terminal term)
{
    var count = 0;
    
    while (true)
    {
        term.Draw(buf =>
        {
            var box = new Rect(2, 1, 40, 7);
            buf.DrawBoxWithTitle(box, " Counter ", ConsoleColor.Cyan, ConsoleColor.White);

            var inner = box.Shrink(2, 1);
            buf.DrawText(inner.X, inner.Y + 1, $"Count: {count}", ConsoleColor.Yellow);
            buf.DrawText(inner.X, inner.Y + 3, "[j/↓] -1   [k/↑] +1   [r] reset", ConsoleColor.DarkGray);

            Widgets.StatusBar(buf, buf.Area, "[q] Back to menu");
        });

        var key = Input.Poll();
        if (Input.IsQuit(key)) break;

        if (key != null)
        {
            switch (key.Value.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.K:
                    count++;
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.J:
                    count--;
                    break;
                case ConsoleKey.R:
                    count = 0;
                    break;
            }
        }

        Thread.Sleep(16);
    }
}

void RunList(Terminal term)
{
    var items = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon", "Mango", "Nectarine", "Orange", "Papaya", "Quince" };
    var selected = 0;
    var scroll = 0;
    
    while (true)
    {
        term.Draw(buf =>
        {
            buf.DrawText(2, 1, "Select a fruit:", ConsoleColor.White);
            
            var listArea = new Rect(2, 3, 30, 10);
            Widgets.List(buf, listArea, items, selected, scroll);

            buf.DrawText(35, 3, "Selected:", ConsoleColor.Gray);
            buf.DrawText(35, 4, items[selected], ConsoleColor.Cyan);

            Widgets.StatusBar(buf, buf.Area, "[↑/↓] Navigate  [q] Back");
        });

        var key = Input.Poll();
        if (Input.IsQuit(key)) break;

        if (key != null)
        {
            switch (key.Value.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.K:
                    if (selected > 0)
                    {
                        selected--;
                        if (selected < scroll) scroll = selected;
                    }
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.J:
                    if (selected < items.Length - 1)
                    {
                        selected++;
                        if (selected >= scroll + 8) scroll = selected - 7;
                    }
                    break;
            }
        }

        Thread.Sleep(16);
    }
}

void RunProgress(Terminal term)
{
    var progress = 0f;
    var autoRun = false;
    
    while (true)
    {
        if (autoRun)
        {
            progress += 0.01f;
            if (progress >= 1f)
            {
                progress = 1f;
                autoRun = false;
            }
        }

        term.Draw(buf =>
        {
            buf.DrawBoxWithTitle(buf.Area.Shrink(1), " Progress Demo ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

            var inner = buf.Area.Shrink(3);

            buf.DrawText(inner.X, inner.Y, "Download Progress:", ConsoleColor.White);
            buf.DrawProgressBar(new Rect(inner.X, inner.Y + 1, inner.Width, 1), progress, ConsoleColor.Green);
            buf.DrawText(inner.X, inner.Y + 2, $"{progress * 100:F0}%", ConsoleColor.Gray);

            buf.DrawText(inner.X, inner.Y + 4, "Upload Progress:", ConsoleColor.White);
            buf.DrawProgressBar(new Rect(inner.X, inner.Y + 5, inner.Width, 1), progress * 0.7f, ConsoleColor.Cyan);
            buf.DrawText(inner.X, inner.Y + 6, $"{progress * 70:F0}%", ConsoleColor.Gray);

            buf.DrawText(inner.X, inner.Y + 8, "CPU Usage:", ConsoleColor.White);
            var cpu = (float)(0.3 + 0.2 * Math.Sin(DateTime.Now.Ticks / 5000000.0));
            buf.DrawProgressBar(new Rect(inner.X, inner.Y + 9, inner.Width, 1), cpu, ConsoleColor.Yellow);

            Widgets.StatusBar(buf, buf.Area, $"[Space] {(autoRun ? "Pause" : "Start")}  [r] Reset  [q] Back");
        });

        var key = Input.Poll();
        if (Input.IsQuit(key)) break;

        if (key != null)
        {
            switch (key.Value.Key)
            {
                case ConsoleKey.Spacebar:
                    autoRun = !autoRun;
                    break;
                case ConsoleKey.R:
                    progress = 0f;
                    autoRun = false;
                    break;
            }
        }

        Thread.Sleep(16);
    }
}

void RunForm(Terminal term)
{
    var focus = new FocusManager(3);
    var name = "";
    var email = "";
    var subscribe = false;
    var cursorPos = 0;
    
    while (true)
    {
        term.Draw(buf =>
        {
            buf.DrawBoxWithTitle(buf.Area.Shrink(1), " Registration Form ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

            var inner = buf.Area.Shrink(3);

            // Name field
            buf.DrawText(inner.X, inner.Y, "Name:", ConsoleColor.White);
            Widgets.TextInput(buf, new Rect(inner.X, inner.Y + 1, 30, 3), name, focus.IsFocused(0), focus.IsFocused(0) ? cursorPos : -1);

            // Email field  
            buf.DrawText(inner.X, inner.Y + 5, "Email:", ConsoleColor.White);
            Widgets.TextInput(buf, new Rect(inner.X, inner.Y + 6, 30, 3), email, focus.IsFocused(1), focus.IsFocused(1) ? cursorPos : -1);

            // Checkbox
            Widgets.Checkbox(buf, inner.X, inner.Y + 10, "Subscribe to newsletter", subscribe, focus.IsFocused(2));

            Widgets.StatusBar(buf, buf.Area, "[Tab] Next  [Space] Toggle  [q] Back");
        });

        var key = Input.Poll();
        if (Input.IsQuit(key)) break;

        if (key != null)
        {
            if (!focus.HandleInput(key))
            {
                // Handle input for focused element
                if (focus.Current < 2) // Text inputs
                {
                    ref var text = ref (focus.Current == 0 ? ref name : ref email);
                    
                    switch (key.Value.Key)
                    {
                        case ConsoleKey.Backspace when cursorPos > 0:
                            text = text.Remove(cursorPos - 1, 1);
                            cursorPos--;
                            break;
                        case ConsoleKey.Delete when cursorPos < text.Length:
                            text = text.Remove(cursorPos, 1);
                            break;
                        case ConsoleKey.LeftArrow when cursorPos > 0:
                            cursorPos--;
                            break;
                        case ConsoleKey.RightArrow when cursorPos < text.Length:
                            cursorPos++;
                            break;
                        case ConsoleKey.Home:
                            cursorPos = 0;
                            break;
                        case ConsoleKey.End:
                            cursorPos = text.Length;
                            break;
                        default:
                            if (!char.IsControl(key.Value.KeyChar))
                            {
                                text = text.Insert(cursorPos, key.Value.KeyChar.ToString());
                                cursorPos++;
                            }
                            break;
                    }
                }
                else if (focus.Current == 2 && key.Value.Key == ConsoleKey.Spacebar)
                {
                    subscribe = !subscribe;
                }
            }
            else
            {
                // Focus changed, reset cursor
                cursorPos = focus.Current switch
                {
                    0 => name.Length,
                    1 => email.Length,
                    _ => 0
                };
            }
        }

        Thread.Sleep(16);
    }
}

void RunDashboard(Terminal term)
{
    var data = new[] { "Users: 1,234", "Revenue: $45,678", "Orders: 567", "Growth: +12.3%" };
    var logs = new List<string> { "System started", "Connected to database", "Cache initialized" };
    var startTime = DateTime.Now;
    
    while (true)
    {
        // Simulate new log entries
        if (Random.Shared.NextDouble() < 0.02)
        {
            var messages = new[] { "Request processed", "User logged in", "Data synced", "Cache miss", "API call completed" };
            logs.Add($"{DateTime.Now:HH:mm:ss} - {messages[Random.Shared.Next(messages.Length)]}");
            if (logs.Count > 8) logs.RemoveAt(0);
        }

        term.Draw(buf =>
        {
            var (top, bottom) = buf.Area.Shrink(1).SplitHorizontal(8);
            var (left, right) = bottom.SplitVertical(buf.Width / 2);

            // Stats boxes
            buf.DrawBoxWithTitle(top, " Statistics ", ConsoleColor.DarkGray, ConsoleColor.Cyan);
            var statsInner = top.Shrink(2, 1);
            var boxWidth = statsInner.Width / 4;
            
            for (var i = 0; i < data.Length; i++)
            {
                var boxRect = new Rect(statsInner.X + i * boxWidth, statsInner.Y, boxWidth - 1, 4);
                buf.DrawBox(boxRect, ConsoleColor.DarkGray, BoxStyle.Rounded);
                buf.DrawTextCentered(boxRect, data[i], ConsoleColor.Yellow);
            }

            // Logs panel
            buf.DrawBoxWithTitle(left.Shrink(0, 1), " Logs ", ConsoleColor.DarkGray, ConsoleColor.Green);
            var logsInner = left.Shrink(2, 2);
            for (var i = 0; i < Math.Min(logs.Count, logsInner.Height); i++)
            {
                var log = logs[i];
                if (log.Length > logsInner.Width)
                    log = log[..(logsInner.Width - 1)] + "…";
                buf.DrawText(logsInner.X, logsInner.Y + i, log, ConsoleColor.Gray);
            }

            // Activity panel
            buf.DrawBoxWithTitle(right.Shrink(0, 1), " Activity ", ConsoleColor.DarkGray, ConsoleColor.Magenta);
            var actInner = right.Shrink(2, 2);
            
            // Simple animated bar chart
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            for (var i = 0; i < 5; i++)
            {
                var value = (float)(0.3 + 0.6 * Math.Sin(elapsed / 2 + i));
                var barWidth = (int)(value * (actInner.Width - 10));
                buf.DrawText(actInner.X, actInner.Y + i * 2, $"Svc {i + 1}", ConsoleColor.Gray);
                buf.DrawProgressBar(new Rect(actInner.X + 7, actInner.Y + i * 2, actInner.Width - 8, 1), value, ConsoleColor.Cyan);
            }

            Widgets.StatusBar(buf, buf.Area, $"Uptime: {DateTime.Now - startTime:hh\\:mm\\:ss}   [q] Back");
        });

        if (Input.IsQuit(Input.Poll())) break;
        Thread.Sleep(50);
    }
}
