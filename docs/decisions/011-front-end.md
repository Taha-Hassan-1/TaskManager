# 11. Front end

**Status:** Accepted, with a version change

**Decision.** Angular, standalone components, signals, Angular CDK drag and drop, Angular Material.

**Why.** It matches the job postings, and signals keep state simple.

**Rejected.** NgRx for v1; React.

**What happened.** Built on **Angular 21, not the current stable 22**: Angular 22 needs Node 22.22.3 or newer and the development machine has 22.14. `ng update` makes the move when Node is upgraded.
