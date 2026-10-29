using MiniTui;

// === Demo Application ===
// Shows off the MiniTui framework with multiple views

var demos = new (string Name, Action Run)[]
{
    ("Counter", RunCounter),
    ("List Selection", RunList),
    ("Progress Bars", RunProgress),
    ("Form Demo", RunForm),
    ("Dashboard", RunDashboard),
    ("Command Registry", RunCommandDemo),
    ("Tabs Demo", RunTabsDemo),
};

var demoNames = demos.Select(d => d.Name).ToList();
var listState = ListState.Empty;

var app = App.Create()
    .Render((buf, screen) =>
    {
        buf.DrawBoxWithTitle(screen, " MiniTui Demos ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

        var inner = screen.Shrink(2);
        buf.DrawText(inner.X, inner.Y, "Select a demo:", ConsoleColor.White);

        var listArea = new Rect(inner.X, inner.Y + 2, 30, demos.Length + 2);
        Widget.List()
            .Items(demoNames)
            .State(listState)
            .Focused()
            .Draw(buf, listArea);

        Widget.StatusBar()
            .Left("[↑/↓] Navigate  [Enter] Select  [q] Quit")
            .Draw(buf, screen);
    });

app.RunWhile(() =>
{
    var key = Input.Poll();
    if (key == null) return true;
    if (Input.IsQuit(key)) return false;

    if (InputHelpers.IsListNavigationKey(key.Value))
    {
        listState = InputHelpers.HandleListNavigation(key.Value, listState, demos.Length, demos.Length);
    }
    else if (key.Value.Key == ConsoleKey.Enter)
    {
        demos[listState.SelectedIndex].Run();
    }

    return true;
});

// === Demo Implementations ===

void RunCounter()
{
    var count = 0;

    App.Create()
        .Render((buf, screen) =>
        {
            var box = new Rect(2, 1, 40, 7);
            buf.DrawBoxWithTitle(box, " Counter ", ConsoleColor.Cyan, ConsoleColor.White);

            var inner = box.Shrink(2, 1);
            buf.DrawText(inner.X, inner.Y + 1, $"Count: {count}", ConsoleColor.Yellow);
            buf.DrawText(inner.X, inner.Y + 3, "[j/↓] -1   [k/↑] +1   [r] reset", ConsoleColor.DarkGray);

            Widget.StatusBar()
                .Left("[q] Back to menu")
                .Draw(buf, screen);
        })
        .RunWhile(() =>
        {
            var key = Input.Poll();
            if (Input.IsQuit(key)) return false;

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

            return true;
        });
}

void RunList()
{
    var items = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape", "Honeydew", "Kiwi", "Lemon", "Mango", "Nectarine", "Orange", "Papaya", "Quince" };
    var listState = ListState.Empty;
    var visibleHeight = 8;

    App.Create()
        .Render((buf, screen) =>
        {
            buf.DrawText(2, 1, "Select a fruit:", ConsoleColor.White);

            var listArea = new Rect(2, 3, 30, 10);
            Widget.List()
                .Items(items)
                .State(listState)
                .Focused()
                .Draw(buf, listArea);

            buf.DrawText(35, 3, "Selected:", ConsoleColor.Gray);
            buf.DrawText(35, 4, items[listState.SelectedIndex], ConsoleColor.Cyan);

            Widget.StatusBar()
                .Left("[↑/↓] Navigate  [q] Back")
                .Draw(buf, screen);
        })
        .RunWhile(() =>
        {
            var key = Input.Poll();
            if (Input.IsQuit(key)) return false;

            if (key != null && InputHelpers.IsListNavigationKey(key.Value))
            {
                listState = InputHelpers.HandleListNavigation(key.Value, listState, items.Length, visibleHeight);
            }

            return true;
        });
}

void RunProgress()
{
    var progress = 0f;
    var autoRun = false;

    App.Create()
        .WithTickRate(TimeSpan.FromMilliseconds(30))
        .OnUpdate(() =>
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
        })
        .Render((buf, screen) =>
        {
            buf.DrawBoxWithTitle(screen.Shrink(1), " Progress Demo ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

            var inner = screen.Shrink(3);

            buf.DrawText(inner.X, inner.Y, "Download Progress:", ConsoleColor.White);
            buf.DrawProgressBar(new Rect(inner.X, inner.Y + 1, inner.Width, 1), progress, ConsoleColor.Green);
            buf.DrawText(inner.X, inner.Y + 2, $"{progress * 100:F0}%", ConsoleColor.Gray);

            buf.DrawText(inner.X, inner.Y + 4, "Upload Progress:", ConsoleColor.White);
            buf.DrawProgressBar(new Rect(inner.X, inner.Y + 5, inner.Width, 1), progress * 0.7f, ConsoleColor.Cyan);
            buf.DrawText(inner.X, inner.Y + 6, $"{progress * 70:F0}%", ConsoleColor.Gray);

            buf.DrawText(inner.X, inner.Y + 8, "CPU Usage:", ConsoleColor.White);
            var cpu = (float)(0.3 + 0.2 * Math.Sin(DateTime.Now.Ticks / 5000000.0));
            buf.DrawProgressBar(new Rect(inner.X, inner.Y + 9, inner.Width, 1), cpu, ConsoleColor.Yellow);

            Widget.StatusBar()
                .Left($"[Space] {(autoRun ? "Pause" : "Start")}  [r] Reset  [q] Back")
                .Draw(buf, screen);
        })
        .RunWhile(() =>
        {
            var key = Input.Poll();
            if (Input.IsQuit(key)) return false;

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

            return true;
        });
}

void RunForm()
{
    var nameState = TextInputState.Empty;
    var emailState = TextInputState.Empty;
    var subscribe = false;

    InputRouter router = null!;
    router = new InputRouter()
        .Add("name", key =>
        {
            nameState = InputHelpers.HandleTextInput(key, nameState);
        })
        .Add("email", key =>
        {
            emailState = InputHelpers.HandleTextInput(key, emailState);
        })
        .Add("subscribe", key =>
        {
            if (key.Key == ConsoleKey.Spacebar)
                subscribe = !subscribe;
        })
        .StartWith("name");

    App.Create()
        .UseRouter(router)
        .Render((buf, screen) =>
        {
            buf.DrawBoxWithTitle(screen.Shrink(1), " Registration Form ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

            var inner = screen.Shrink(3);

            // Name field
            buf.DrawText(inner.X, inner.Y, "Name:", ConsoleColor.White);
            Widgets.TextInput(buf, new Rect(inner.X, inner.Y + 1, 30, 3), nameState.Value, router.IsFocused("name"), nameState.CursorPos);

            // Email field
            buf.DrawText(inner.X, inner.Y + 5, "Email:", ConsoleColor.White);
            Widgets.TextInput(buf, new Rect(inner.X, inner.Y + 6, 30, 3), emailState.Value, router.IsFocused("email"), emailState.CursorPos);

            // Checkbox
            Widgets.Checkbox(buf, inner.X, inner.Y + 10, "Subscribe to newsletter", subscribe, router.IsFocused("subscribe"));

            Widget.StatusBar()
                .Left("[Tab] Next  [Space] Toggle  [q] Back")
                .Draw(buf, screen);
        })
        .RunWhile(() =>
        {
            var key = Input.Poll();
            if (Input.IsQuit(key)) return false;

            if (key != null)
                router.Handle(key.Value);

            return true;
        });
}

void RunDashboard()
{
    var data = new[] { "Users: 1,234", "Revenue: $45,678", "Orders: 567", "Growth: +12.3%" };
    var logs = new List<string> { "System started", "Connected to database", "Cache initialized" };
    var startTime = DateTime.Now;

    App.Create()
        .WithTickRate(TimeSpan.FromMilliseconds(50))
        .OnUpdate(() =>
        {
            // Simulate new log entries
            if (Random.Shared.NextDouble() < 0.02)
            {
                var messages = new[] { "Request processed", "User logged in", "Data synced", "Cache miss", "API call completed" };
                logs.Add($"{DateTime.Now:HH:mm:ss} - {messages[Random.Shared.Next(messages.Length)]}");
                if (logs.Count > 8) logs.RemoveAt(0);
            }
        })
        .Render((buf, screen) =>
        {
            var layout = Layout.Vertical(screen.Shrink(1))
                .Add("stats", 8)
                .AddFill("panels")
                .Build();

            var panelsLayout = Layout.Horizontal(layout["panels"])
                .AddFill("logs")
                .AddFill("activity")
                .Build();

            // Stats boxes
            buf.DrawBoxWithTitle(layout["stats"], " Statistics ", ConsoleColor.DarkGray, ConsoleColor.Cyan);
            var statsInner = layout["stats"].Shrink(2, 1);
            var boxWidth = statsInner.Width / 4;

            for (var i = 0; i < data.Length; i++)
            {
                var boxRect = new Rect(statsInner.X + i * boxWidth, statsInner.Y, boxWidth - 1, 4);
                buf.DrawBox(boxRect, ConsoleColor.DarkGray, BoxStyle.Rounded);
                buf.DrawTextCentered(boxRect, data[i], ConsoleColor.Yellow);
            }

            // Logs panel
            var logsArea = panelsLayout["logs"].Shrink(0, 1);
            buf.DrawBoxWithTitle(logsArea, " Logs ", ConsoleColor.DarkGray, ConsoleColor.Green);
            var logsInner = logsArea.Shrink(2, 1);
            for (var i = 0; i < Math.Min(logs.Count, logsInner.Height); i++)
            {
                var log = logs[i];
                if (log.Length > logsInner.Width)
                    log = log[..(logsInner.Width - 1)] + "…";
                buf.DrawText(logsInner.X, logsInner.Y + i, log, ConsoleColor.Gray);
            }

            // Activity panel
            var actArea = panelsLayout["activity"].Shrink(0, 1);
            buf.DrawBoxWithTitle(actArea, " Activity ", ConsoleColor.DarkGray, ConsoleColor.Magenta);
            var actInner = actArea.Shrink(2, 1);

            // Simple animated bar chart
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            for (var i = 0; i < 5; i++)
            {
                var value = (float)(0.3 + 0.6 * Math.Sin(elapsed / 2 + i));
                buf.DrawText(actInner.X, actInner.Y + i * 2, $"Svc {i + 1}", ConsoleColor.Gray);
                buf.DrawProgressBar(new Rect(actInner.X + 7, actInner.Y + i * 2, actInner.Width - 8, 1), value, ConsoleColor.Cyan);
            }

            Widget.StatusBar()
                .Left($"Uptime: {DateTime.Now - startTime:hh\\:mm\\:ss}   [q] Back")
                .Draw(buf, screen);
        })
        .RunWhile(() => !Input.IsQuit(Input.Poll()));
}

void RunCommandDemo()
{
    var notes = new List<string>();
    var commandState = CommandInputState.Empty;
    var listState = ListState.Empty;
    var status = TimedMessage.Empty;
    var showHelp = false;
    var running = true;

    // Build the command registry
    var commands = CommandRegistry.Create()
        .Add("add", "a")
            .Usage("<text>")
            .Description("Add a note")
            .Handler(cmd =>
            {
                var text = cmd.ArgsAsString;
                if (string.IsNullOrWhiteSpace(text))
                    return CommandResult.Error("Usage: :add <text>");
                notes.Insert(0, $"[{DateTime.Now:HH:mm}] {text}");
                return CommandResult.Ok("Note added");
            })
        .Add("clear", "c")
            .Description("Clear all notes")
            .Handler(_ =>
            {
                notes.Clear();
                listState = ListState.Empty;
                return CommandResult.Ok("Notes cleared");
            })
        .Add("delete", "d", "rm")
            .Usage("<index>")
            .Description("Delete note by index")
            .Handler(cmd =>
            {
                if (!int.TryParse(cmd.GetArg(0), out var index) || index < 1 || index > notes.Count)
                    return CommandResult.Error($"Invalid index. Use 1-{notes.Count}");
                notes.RemoveAt(index - 1);
                return CommandResult.Ok($"Deleted note {index}");
            })
        .Add("help", "h", "?")
            .Description("Show help")
            .Handler(_ =>
            {
                showHelp = true;
                return CommandResult.Ok();
            })
        .Add("quit", "q", "exit")
            .Description("Exit the demo")
            .Handler(_ => CommandResult.Quit())
        .Build();

    // Generate help from registry
    var helpLines = new List<string> { "Commands:" };
    helpLines.AddRange(commands.GetHelp());
    helpLines.Add("");
    helpLines.Add("Navigation: Tab to switch, Up/Down to navigate");

    // Set up router
    InputRouter router = null!;
    router = new InputRouter()
        .Add("list", key =>
        {
            if (InputHelpers.IsListNavigationKey(key))
                listState = InputHelpers.HandleListNavigation(key, listState, notes.Count, 10);
            else if (InputHelpers.IsTextEditKey(key))
            {
                router.SetFocus("command");
                commandState = InputHelpers.HandleCommandInput(key, commandState).State;
            }
        })
        .Add("command", key =>
        {
            var result = InputHelpers.HandleCommandInput(key, commandState);
            commandState = result.State;

            if (result.Command is { } cmd)
            {
                var cmdResult = commands.Execute(cmd);
                if (cmdResult == null)
                    status = TimedMessage.Show($"Unknown command: {cmd.Name}");
                else if (cmdResult.ShouldQuit)
                    running = false;
                else if (cmdResult.Message != null)
                    status = TimedMessage.Show(cmdResult.Message);
            }
        })
        .WithModal(() => showHelp, _ => showHelp = false)
        .StartWith("command");

    App.Create()
        .UseRouter(router)
        .Render((buf, screen) =>
        {
            var layout = Layout.Vertical(screen)
                .AddFill("list")
                .Add("bottom", 3)
                .Build();

            Widget.List()
                .Items(notes.Count > 0 ? notes : ["No notes yet. Try :add Hello world!"])
                .State(listState)
                .Focused(router.IsFocused("list"))
                .Title("Notes")
                .Draw(buf, layout["list"]);

            var inputArea = layout["bottom"].Shrink(1, 0);
            inputArea = new Rect(inputArea.X, inputArea.Y + 1, inputArea.Width, 1);

            Widget.CommandInput()
                .State(commandState)
                .Focused(router.IsFocused("command"))
                .Draw(buf, inputArea);

            Widget.StatusBar()
                .Message(status, "Tab: switch | :h help | :q quit")
                .Right($"{notes.Count} notes")
                .Draw(buf, layout["bottom"]);

            if (showHelp)
                Widget.Modal().Title("Help").Lines(helpLines).Draw(buf);
        })
        .RunWhile(() =>
        {
            var key = Input.Poll();
            if (key != null)
                router.Handle(key.Value);
            return running;
        });
}

void RunTabsDemo()
{
    var tabsState = TabsState.Create(3);
    var dailyItems = new List<string> { "Morning standup", "Code review", "Lunch break" };
    var weeklyItems = new List<string> { "Sprint planning", "Retrospective", "Demo day", "1-on-1 meetings" };
    var allItems = new List<string> { "Q1 Goals", "Annual review", "Team offsite", "Conference", "Training" };
    var listState = ListState.Empty;

    InputRouter router = null!;
    router = new InputRouter()
        .Add("tabs", key =>
        {
            var newState = InputHelpers.HandleTabsNavigation(key, tabsState);
            if (newState.SelectedIndex != tabsState.SelectedIndex)
            {
                tabsState = newState;
                listState = ListState.Empty; // Reset list when switching tabs
            }
        })
        .Add("list", key =>
        {
            var items = GetCurrentItems();
            listState = InputHelpers.HandleListNavigation(key, listState, items.Count, 8);
        })
        .StartWith("tabs");

    List<string> GetCurrentItems() => tabsState.SelectedIndex switch
    {
        0 => dailyItems,
        1 => weeklyItems,
        _ => allItems
    };

    App.Create()
        .UseRouter(router)
        .Render((buf, screen) =>
        {
            buf.DrawBoxWithTitle(screen.Shrink(1), " Tasks ", ConsoleColor.DarkGray, ConsoleColor.Cyan);

            var inner = screen.Shrink(2);

            // Draw tabs
            var tabsArea = new Rect(inner.X, inner.Y, inner.Width, 2);
            var tabsHeight = Widget.Tabs()
                .Tab("Daily")
                .Tab("Weekly")
                .Tab("All")
                .State(() => tabsState)
                .Focused(router.IsFocused("tabs"))
                .Draw(buf, tabsArea);

            // Draw list below tabs
            var listArea = new Rect(inner.X, inner.Y + tabsHeight, inner.Width, inner.Height - tabsHeight - 1);
            var items = GetCurrentItems();
            Widget.List()
                .Items(items.Count > 0 ? items : ["No items"])
                .State(listState)
                .Focused(router.IsFocused("list"))
                .Draw(buf, listArea);

            Widget.StatusBar()
                .Left("[Tab] Switch focus  [←/→] Change tab  [↑/↓] Navigate  [q] Back")
                .Draw(buf, screen);
        })
        .RunWhile(() =>
        {
            var key = Input.Poll();
            if (Input.IsQuit(key)) return false;
            if (key != null)
                router.Handle(key.Value);
            return true;
        });
}
