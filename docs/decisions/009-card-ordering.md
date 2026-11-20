# 9. Card ordering

**Status:** Accepted

**Decision.** A numeric `Position` per card with occasional rebalancing.

**Why.** A move updates one row instead of renumbering a column.

**Rejected.** Renumbering the whole column on each move.

**What happened.** Done as `IPositionCalculator` (gap midpoint, renumber only when neighbours are less than 1e-6 apart). Two quick adds to one column produced equal positions, so ordering is by `(Position, Id)` everywhere; see DEBUGGING.md, bug 3.
