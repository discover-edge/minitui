namespace MiniTui;

/// <summary>
/// The focusable children of a component, in tab order (§9.2). The focus chain starts at the top screen and
/// follows <see cref="Focused"/> from each component until it reaches one whose scope is empty.
/// </summary>
public sealed class FocusScope
{
    private readonly Component _owner;
    private readonly List<Component> _members = [];
    private int _focused;

    internal FocusScope(Component owner) => _owner = owner;

    /// <summary>The focused member: the first one added, until focus moves. Null when the scope is empty.</summary>
    public Component? Focused => _members.Count == 0 ? null : _members[_focused];

    /// <summary>How many members the scope has.</summary>
    public int Count => _members.Count;

    /// <summary>Add a member. Tab order is add order.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="child"/> isn't a child of the scope's owner.</exception>
    public FocusScope Add(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.Parent, _owner))
            throw new InvalidOperationException($"{child.GetType().Name} isn't a child of {_owner.GetType().Name}; call AddChild first.");
        if (!_members.Contains(child)) _members.Add(child);
        return this;
    }

    /// <summary>Focus a member.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="child"/> isn't in the scope.</exception>
    public void Focus(Component child)
    {
        var index = _members.IndexOf(child);
        if (index < 0) throw new InvalidOperationException($"{child?.GetType().Name} isn't in this focus scope.");
        _focused = index;
    }

    /// <summary>True if <paramref name="child"/> is the focused member.</summary>
    public bool IsFocused(Component child) => ReferenceEquals(Focused, child);

    /// <summary>Focus the next member, wrapping around.</summary>
    public void Next()
    {
        if (_members.Count > 0) _focused = (_focused + 1) % _members.Count;
    }

    /// <summary>Focus the previous member, wrapping around.</summary>
    public void Previous()
    {
        if (_members.Count > 0) _focused = (_focused - 1 + _members.Count) % _members.Count;
    }

    internal bool Contains(Component child) => _members.Contains(child);

    internal void Remove(Component child)
    {
        var index = _members.IndexOf(child);
        if (index < 0) return;
        _members.RemoveAt(index);
        if (_focused > index || _focused >= _members.Count) _focused = Math.Max(0, _focused - 1);
    }
}
