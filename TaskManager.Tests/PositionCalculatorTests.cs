using TaskManager.API.Infrastructure;

namespace TaskManager.Tests;

public class PositionCalculatorTests
{
    private readonly GapPositionCalculator _calc = new();

    [Fact]
    public void EmptyList_StartsAtOneStep() => Assert.Equal(1024, _calc.Between(null, null));

    [Fact]
    public void Append_AddsOneStep() => Assert.Equal(3072, _calc.Between(2048, null));

    [Fact]
    public void Prepend_SubtractsOneStep() => Assert.Equal(0, _calc.Between(null, 1024));

    [Fact]
    public void Between_TakesTheMidpoint() => Assert.Equal(1536, _calc.Between(1024, 2048));

    [Fact]
    public void GapTooSmall_AsksForRenumbering() => Assert.Null(_calc.Between(1024, 1024 + 1e-7));

    [Fact]
    public void RepeatedHalving_EventuallyAsksForRenumbering()
    {
        double lower = 1024, upper = 2048;
        var splits = 0;
        while (_calc.Between(lower, upper) is { } mid && splits < 100)
        {
            Assert.True(mid > lower && mid < upper);
            upper = mid; // always drop into the same gap
            splits++;
        }
        Assert.InRange(splits, 15, 40);
    }

    private static readonly Sibling[] Column = [new(1, 1024), new(2, 2048), new(3, 3072)];

    [Fact]
    public void Place_BeforeFirst_IsAboveIt() => Assert.Equal(0, _calc.Place(Column, beforeId: 1, afterId: null).Position);

    [Fact]
    public void Place_BeforeMiddle_SplitsTheGapAbove() => Assert.Equal(1536, _calc.Place(Column, beforeId: 2, afterId: null).Position);

    [Fact]
    public void Place_AfterMiddle_SplitsTheGapBelow() => Assert.Equal(2560, _calc.Place(Column, beforeId: null, afterId: 2).Position);

    [Fact]
    public void Place_AfterLast_AppendsAStep() => Assert.Equal(4096, _calc.Place(Column, beforeId: null, afterId: 3).Position);

    [Fact]
    public void Place_WithoutHint_GoesToTheEnd() => Assert.Equal(4096, _calc.Place(Column, null, null).Position);

    [Fact]
    public void Place_IntoEmptyColumn_StartsAtOneStep() => Assert.Equal(1024, _calc.Place([], null, null).Position);

    [Fact]
    public void Place_UnknownReference_IsInvalid() => Assert.False(_calc.Place(Column, beforeId: 99, afterId: null).Valid);
}
