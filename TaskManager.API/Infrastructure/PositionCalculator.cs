namespace TaskManager.API.Infrastructure;

/// <summary>
/// Strategy for ordering cards and columns. Ordering rules can change without touching the move handlers.
/// </summary>
public interface IPositionCalculator
{
    /// <summary>Gap used when appending, and when renumbering a column.</summary>
    double Step { get; }

    /// <summary>
    /// A position strictly between the neighbours (either may be missing: top or bottom of the list).
    /// Returns null when the gap is too small to split, meaning the list must be renumbered first.
    /// </summary>
    double? Between(double? lower, double? upper);
}

public sealed class GapPositionCalculator : IPositionCalculator
{
    private const double MinGap = 1e-6;

    public double Step => 1024;

    public double? Between(double? lower, double? upper)
    {
        if (lower is null && upper is null) return Step;
        if (lower is null) return upper!.Value - Step;
        if (upper is null) return lower.Value + Step;

        if (upper.Value - lower.Value < MinGap) return null;
        return (lower.Value + upper.Value) / 2;
    }
}

public readonly record struct Sibling(int Id, double Position);

public readonly record struct Placement(bool Valid, double? Position);

public static class PlacementExtensions
{
    /// <summary>
    /// Where an item lands among <paramref name="siblings"/> (already ordered, excluding the item itself):
    /// before one of them, after one of them, or at the end when neither is given.
    /// <see cref="Placement.Valid"/> is false when the referenced sibling is not in the list;
    /// a null <see cref="Placement.Position"/> means "renumber the list and ask again".
    /// </summary>
    public static Placement Place(
        this IPositionCalculator calc, IReadOnlyList<Sibling> siblings, int? beforeId, int? afterId)
    {
        double? lower, upper;
        if (beforeId is { } before)
        {
            var i = IndexOf(siblings, before);
            if (i < 0) return new Placement(false, null);
            upper = siblings[i].Position;
            lower = i > 0 ? siblings[i - 1].Position : null;
        }
        else if (afterId is { } after)
        {
            var i = IndexOf(siblings, after);
            if (i < 0) return new Placement(false, null);
            lower = siblings[i].Position;
            upper = i < siblings.Count - 1 ? siblings[i + 1].Position : null;
        }
        else
        {
            lower = siblings.Count > 0 ? siblings[^1].Position : null;
            upper = null;
        }

        return new Placement(true, calc.Between(lower, upper));
    }

    private static int IndexOf(IReadOnlyList<Sibling> siblings, int id)
    {
        for (var i = 0; i < siblings.Count; i++)
            if (siblings[i].Id == id) return i;
        return -1;
    }
}
