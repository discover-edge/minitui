namespace MiniTui;

/// <summary>
/// How much space a layout region gets along the layout's axis (§7).
/// </summary>
public abstract record Constraint
{
    private protected Constraint() { }

    /// <summary>Exactly <paramref name="cells"/>, shrunk only if the fixed sizes don't fit.</summary>
    public static Constraint Length(int cells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cells);
        return new LengthConstraint(cells);
    }

    /// <summary>A percentage of the available space, rounded down. Clamped to 0–100.</summary>
    public static Constraint Percentage(int percent) => new PercentageConstraint(Math.Clamp(percent, 0, 100));

    /// <summary><paramref name="numerator"/>/<paramref name="denominator"/> of the available space, rounded down.</summary>
    public static Constraint Ratio(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        return new RatioConstraint(numerator, denominator);
    }

    /// <summary>At least <paramref name="cells"/>, and grows like <c>Fill(1)</c>.</summary>
    public static Constraint Min(int cells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cells);
        return new MinConstraint(cells);
    }

    /// <summary>Grows like <c>Fill(1)</c>, up to <paramref name="cells"/>.</summary>
    public static Constraint Max(int cells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cells);
        return new MaxConstraint(cells);
    }

    /// <summary>Shares the leftover space with other growable regions, in proportion to <paramref name="weight"/>.</summary>
    public static Constraint Fill(int weight = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
        return new FillConstraint(weight);
    }

    internal sealed record LengthConstraint(int Cells) : Constraint
    {
        public override string ToString() => $"Length({Cells})";
    }

    internal sealed record PercentageConstraint(int Percent) : Constraint
    {
        public override string ToString() => $"Percentage({Percent})";
    }

    internal sealed record RatioConstraint(int Numerator, int Denominator) : Constraint
    {
        public override string ToString() => $"Ratio({Numerator}, {Denominator})";
    }

    internal sealed record MinConstraint(int Cells) : Constraint
    {
        public override string ToString() => $"Min({Cells})";
    }

    internal sealed record MaxConstraint(int Cells) : Constraint
    {
        public override string ToString() => $"Max({Cells})";
    }

    internal sealed record FillConstraint(int Weight) : Constraint
    {
        public override string ToString() => $"Fill({Weight})";
    }
}

/// <summary>
/// Where leftover space goes when no region can grow into it.
/// </summary>
public enum Flex
{
    /// <summary>Regions are packed at the start; the leftover is at the end.</summary>
    Start,

    /// <summary>Regions are packed at the end; the leftover is at the start.</summary>
    End,

    /// <summary>The leftover is split before and after the regions (the extra cell goes after).</summary>
    Center,

    /// <summary>The leftover is spread across the gaps between regions.</summary>
    SpaceBetween,
}

/// <summary>
/// The axis a layout splits along.
/// </summary>
public enum Direction
{
    /// <summary>Regions stacked top to bottom.</summary>
    Vertical,

    /// <summary>Regions side by side, left to right.</summary>
    Horizontal,
}
