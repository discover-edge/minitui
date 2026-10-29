namespace MiniTui;

/// <summary>
/// Splits one axis among constraints (§7.2). Deterministic integer arithmetic, no solver.
/// </summary>
internal static class LayoutSolver
{
    /// <summary>
    /// Offsets (relative to the axis start) and sizes for each constraint.
    /// </summary>
    public static (int Offset, int Size)[] Solve(int axisLength, IReadOnlyList<Constraint> constraints, int spacing, Flex flex)
    {
        var n = constraints.Count;
        var result = new (int Offset, int Size)[n];
        if (n == 0) return result;

        axisLength = Math.Max(0, axisLength);
        var gaps = n - 1;

        // Spacing that doesn't fit is reduced, so regions stay inside the area
        spacing = gaps > 0 ? Math.Clamp(spacing, 0, axisLength / gaps) : 0;
        Int128 total = axisLength - spacing * gaps;

        // 1. Base sizes
        var sizes = new Int128[n];
        for (var i = 0; i < n; i++)
        {
            sizes[i] = constraints[i] switch
            {
                Constraint.LengthConstraint c => c.Cells,
                Constraint.PercentageConstraint c => total * c.Percent / 100,
                Constraint.RatioConstraint c => total * c.Numerator / c.Denominator,
                Constraint.MinConstraint c => c.Cells,
                _ => 0,
            };
        }

        // 2. Overflow: shrink Length/Percentage/Ratio first, then Min
        if (Sum(sizes) > total)
        {
            var isMin = constraints.Select(c => c is Constraint.MinConstraint).ToArray();
            var isFixed = constraints.Select(c => c is Constraint.LengthConstraint or Constraint.PercentageConstraint
                                                    or Constraint.RatioConstraint).ToArray();
            var minSum = Sum(sizes, isMin);
            Scale(sizes, isFixed, Int128.Max(0, total - minSum));
            if (Sum(sizes) > total) Scale(sizes, isMin, total - Sum(sizes, isFixed));
        }

        // 3. Growth: Fill(w), Min and Max share the leftover by weight; Max is capped
        var leftover = total - Sum(sizes);
        var weights = new Int128[n];
        for (var i = 0; i < n; i++)
        {
            weights[i] = constraints[i] switch
            {
                Constraint.FillConstraint c => c.Weight,
                Constraint.MinConstraint or Constraint.MaxConstraint => 1,
                _ => 0,
            };
        }

        if (leftover > 0)
        {
            var capped = true;
            while (capped)
            {
                capped = false;
                var weightSum = Sum(weights);
                if (weightSum == 0) break;
                for (var i = 0; i < n; i++)
                {
                    if (weights[i] == 0 || constraints[i] is not Constraint.MaxConstraint max) continue;
                    if (leftover * weights[i] <= max.Cells * weightSum) continue;

                    sizes[i] = max.Cells;
                    leftover -= max.Cells;
                    weights[i] = 0;
                    capped = true;
                    break;
                }
            }

            var growable = weights.Select(w => w > 0).ToArray();
            if (growable.Any(g => g))
            {
                var shares = Distribute(leftover, weights, tiesToLater: false);
                for (var i = 0; i < n; i++) sizes[i] += shares[i];
                leftover = 0;
            }
        }

        // 4. Flex places what nothing could grow into
        Int128 lead = 0;
        var extraGaps = new Int128[Math.Max(0, gaps)];
        if (leftover > 0)
        {
            switch (flex)
            {
                case Flex.End:
                    lead = leftover;
                    break;
                case Flex.Center:
                    lead = leftover / 2;
                    break;
                case Flex.SpaceBetween when gaps > 0:
                    extraGaps = Distribute(leftover, Enumerable.Repeat((Int128)1, gaps).ToArray(), tiesToLater: false);
                    break;
            }
        }

        // 5. Positions
        var position = lead;
        for (var i = 0; i < n; i++)
        {
            result[i] = ((int)position, (int)sizes[i]);
            position += sizes[i];
            if (i < gaps) position += spacing + extraGaps[i];
        }
        return result;
    }

    private static Int128 Sum(Int128[] values, bool[]? include = null)
    {
        Int128 sum = 0;
        for (var i = 0; i < values.Length; i++)
            if (include is null || include[i]) sum += values[i];
        return sum;
    }

    /// <summary>Scale the included sizes down proportionally so they sum to <paramref name="target"/>.</summary>
    private static void Scale(Int128[] sizes, bool[] include, Int128 target)
    {
        var current = Sum(sizes, include);
        if (current <= target) return;

        var weights = new Int128[sizes.Length];
        for (var i = 0; i < sizes.Length; i++) weights[i] = include[i] ? sizes[i] : 0;
        var scaled = Distribute(target, weights, tiesToLater: true);
        for (var i = 0; i < sizes.Length; i++)
            if (include[i]) sizes[i] = scaled[i];
    }

    /// <summary>
    /// Split <paramref name="amount"/> in proportion to <paramref name="weights"/>, rounding by largest remainder.
    /// </summary>
    private static Int128[] Distribute(Int128 amount, Int128[] weights, bool tiesToLater)
    {
        var n = weights.Length;
        var shares = new Int128[n];
        var weightSum = Sum(weights);
        if (weightSum == 0 || amount <= 0) return shares;

        var remainders = new Int128[n];
        Int128 given = 0;
        for (var i = 0; i < n; i++)
        {
            var exact = amount * weights[i];
            shares[i] = exact / weightSum;
            remainders[i] = exact % weightSum;
            given += shares[i];
        }

        var order = Enumerable.Range(0, n)
            .Where(i => weights[i] > 0)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => tiesToLater ? -i : i);
        foreach (var i in order)
        {
            if (given >= amount) break;
            shares[i]++;
            given++;
        }
        return shares;
    }
}
