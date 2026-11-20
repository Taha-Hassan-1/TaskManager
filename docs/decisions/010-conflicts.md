# 10. Conflicts

**Status:** Accepted

**Decision.** Optimistic concurrency with a `RowVersion`; a stale edit or move returns 409 and the client reloads the card.

**Why.** Two people dragging the same card is the realistic bug to handle.

**Rejected.** Last write wins.

**What happened.** Done for edits and moves, including a labels-only edit (which would otherwise not touch the card row and so skip the check). The screen offers 'discard mine' or 'keep mine on the latest version'.
