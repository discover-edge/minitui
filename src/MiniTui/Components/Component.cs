namespace MiniTui;

/// <summary>
/// A piece of UI that holds state, handles keys and renders itself every frame (§9.1). Components stay
/// immediate-mode: a parent renders its children explicitly with <c>ctx.Render(child, buf, rect)</c>, which also
/// records where the child is for the mouse.
/// </summary>
/// <remarks>
/// Every callback runs on the UI thread. A parent mounts before its children; children unmount before their
/// parent, in reverse add order, and <see cref="Lifetime"/> is cancelled before <see cref="OnUnmount"/> runs.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The lifetime source is disposed at unmount, which the screen and overlay stacks control.")]
public abstract class Component
{
    private readonly List<Component> _children = [];
    private App? _app;
    private CancellationTokenSource? _lifetime;
    private bool _active;

    /// <summary>Create a component.</summary>
    protected Component() => Focus = new FocusScope(this);

    /// <summary>This component's key bindings, tried after <see cref="HandleKey"/>.</summary>
    public KeyMap Keys { get; } = new();

    /// <summary>The heading for this component's bindings in the help overlay.</summary>
    public virtual string KeysGroup => GetType().Name;

    /// <summary>
    /// True for text inputs: while this component is the innermost focused one, its ancestors' and the app's
    /// single-character bindings (without Ctrl or Alt) are skipped, so typing <c>q</c> doesn't quit.
    /// </summary>
    public virtual bool CapturesText => false;

    /// <summary>Handle a key before <see cref="Keys"/>. Return true if handled.</summary>
    public virtual bool HandleKey(ConsoleKeyInfo key) => false;

    /// <summary>
    /// Handle pasted text, such as with <see cref="TextFieldState.Paste"/>. Return true if handled; otherwise the
    /// paste goes on to the component's parent, and is dropped if nothing handles it.
    /// </summary>
    public virtual bool HandlePaste(string text) => false;

    /// <summary>
    /// Handle a mouse event inside this component's <see cref="Area"/> (1.x spec §4.7). Return true if handled;
    /// otherwise the event goes on to the component's parent, and is dropped if nothing handles it. A press has
    /// already focused the component when this runs.
    /// </summary>
    public virtual bool HandleMouse(MouseEvent mouse) => false;

    /// <summary>
    /// The area the component was last rendered in through <see cref="RenderContext.Render"/>, or by the app (the
    /// top screen and overlays). <c>default</c> (an empty rect) until then.
    /// </summary>
    public Rect Area { get; internal set; }

    /// <summary>Draw the component. Called every frame its parent renders it.</summary>
    public abstract void Render(Buffer buf, Rect area, RenderContext ctx);

    /// <summary>The app, from mount to unmount.</summary>
    /// <exception cref="InvalidOperationException">The component isn't mounted.</exception>
    protected App App => _app ?? throw new InvalidOperationException($"{GetType().Name} isn't mounted.");

    /// <summary>Cancelled when the component unmounts.</summary>
    /// <exception cref="InvalidOperationException">The component isn't mounted.</exception>
    protected CancellationToken Lifetime => _lifetime?.Token ?? throw new InvalidOperationException($"{GetType().Name} isn't mounted.");

    /// <summary>This component's focusable children, in tab order. Empty by default.</summary>
    protected FocusScope Focus { get; }

    /// <summary>
    /// Add a child. If this component is mounted, the child mounts now (and activates, if this one is active).
    /// </summary>
    /// <exception cref="InvalidOperationException">The child already has a parent.</exception>
    protected T AddChild<T>(T child) where T : Component
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null || ReferenceEquals(child, this))
            throw new InvalidOperationException($"{child.GetType().Name} already has a parent.");
        child.Parent = this;
        _children.Add(child);
        if (_app is { } app) child.Mount(app);
        if (_active) child.Activate();
        return child;
    }

    /// <summary>Remove a child: it deactivates, unmounts and leaves <see cref="Focus"/>.</summary>
    /// <exception cref="InvalidOperationException">It isn't a child of this component.</exception>
    protected void RemoveChild(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.Parent, this))
            throw new InvalidOperationException($"{child.GetType().Name} isn't a child of {GetType().Name}.");
        child.Deactivate();
        child.Unmount();
        Focus.Remove(child);
        _children.Remove(child);
        child.Parent = null;
    }

    /// <summary>Start a load that's cancelled, and whose result is ignored, once this component unmounts.</summary>
    protected void Load<T>(Loadable<T> target, Func<CancellationToken, Task<T>> fetch)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Load(App, fetch, Lifetime);
    }

    /// <summary>Called when the component is mounted, before its children.</summary>
    protected virtual void OnMount() { }

    /// <summary>Called when the component unmounts, after its children and after <see cref="Lifetime"/> is cancelled.</summary>
    protected virtual void OnUnmount() { }

    /// <summary>Called when its screen (or overlay) becomes the top one.</summary>
    protected virtual void OnActivated() { }

    /// <summary>Called when its screen is covered or popped (or its overlay closes).</summary>
    protected virtual void OnDeactivated() { }

    /// <summary>
    /// An inline component, for small apps that want focus handling without writing a class.
    /// </summary>
    public static Component From(Action<Buffer, Rect, RenderContext> render,
                                 Func<ConsoleKeyInfo, bool>? handleKey = null, bool capturesText = false)
    {
        ArgumentNullException.ThrowIfNull(render);
        return new InlineComponent(render, handleKey, capturesText);
    }

    internal Component? Parent { get; private set; }

    internal bool IsMounted => _app is not null;

    internal FocusScope FocusScope => Focus;

    internal void Mount(App app)
    {
        if (_app is not null) return;
        _app = app;
        _lifetime = new CancellationTokenSource();
        OnMount();
        foreach (var child in _children.ToArray())
            if (ReferenceEquals(child.Parent, this)) child.Mount(app);
    }

    internal void Unmount()
    {
        if (_app is null) return;
        for (var i = _children.Count - 1; i >= 0; i--) _children[i].Unmount();

        // Lifetime is cancelled before OnUnmount, and still readable (cancelled) inside it
        var lifetime = _lifetime;
        try
        {
            lifetime?.Cancel();
        }
        finally
        {
            try
            {
                OnUnmount();
            }
            finally
            {
                _lifetime = null;
                lifetime?.Dispose();
                _app = null;
            }
        }
    }

    internal void Activate()
    {
        if (_active) return;
        _active = true;
        OnActivated();
        foreach (var child in _children.ToArray()) child.Activate();
    }

    internal void Deactivate()
    {
        if (!_active) return;
        for (var i = _children.Count - 1; i >= 0; i--) _children[i].Deactivate();
        _active = false;
        OnDeactivated();
    }

    private sealed class InlineComponent(Action<Buffer, Rect, RenderContext> render, Func<ConsoleKeyInfo, bool>? handleKey, bool capturesText)
        : Component
    {
        public override bool CapturesText => capturesText;
        public override string KeysGroup => "Component";
        public override bool HandleKey(ConsoleKeyInfo key) => handleKey?.Invoke(key) == true;
        public override void Render(Buffer buf, Rect area, RenderContext ctx) => render(buf, area, ctx);
    }
}
