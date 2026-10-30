namespace MiniTui;

/// <summary>Mouse hit testing and routing (1.x spec §4.7).</summary>
internal static class MouseRouter
{
    // A parent cycle is a bug in the app; don't loop forever on one
    private const int MaxDepth = 64;

    /// <summary>A hit target recorded while a frame rendered.</summary>
    internal readonly record struct Hit(Component Component, Rect Rect);

    /// <summary>
    /// Steps 3–5: find the target in the last frame's hits, focus it on a press, then offer the event to it and
    /// its ancestors. Returns true if one handled it.
    /// </summary>
    public static bool Route(App app, MouseEvent mouse, List<Hit> hits, Rect frame)
    {
        if (!frame.Contains(mouse.X, mouse.Y)) return false;
        Component? root = app.Overlays.Top is { } overlay ? overlay : app.Screens.Top;
        if (root is null || Target(hits, root, mouse) is not { } target) return false;

        if (mouse.Action == MouseAction.Press) FocusPath(target, root);
        var depth = 0;
        for (var c = target; c is not null && depth++ < MaxDepth; c = ReferenceEquals(c, root) ? null : c.Parent)
            if (c.HandleMouse(mouse)) return true;
        return false;
    }

    /// <summary>
    /// The last-rendered hit containing the point whose component is still mounted under <paramref name="root"/>.
    /// Children render after their parents, and overlays after screens, so the last match is on top.
    /// </summary>
    private static Component? Target(List<Hit> hits, Component root, MouseEvent mouse)
    {
        for (var i = hits.Count - 1; i >= 0; i--)
        {
            var (component, rect) = hits[i];
            if (rect.Contains(mouse.X, mouse.Y) && component.IsMounted && IsUnder(component, root)) return component;
        }
        return null;
    }

    private static bool IsUnder(Component component, Component root)
    {
        var depth = 0;
        for (var c = component; c is not null && depth++ < MaxDepth; c = c.Parent)
            if (ReferenceEquals(c, root)) return true;
        return false;
    }

    /// <summary>Focus each component on the path from the target up to the root that its parent's scope holds.</summary>
    private static void FocusPath(Component target, Component root)
    {
        var depth = 0;
        for (var c = target; !ReferenceEquals(c, root) && c.Parent is { } parent && depth++ < MaxDepth; c = parent)
            if (parent.FocusScope.Contains(c)) parent.FocusScope.Focus(c);
    }
}
