# 8. Real time

**Status:** Accepted

**Decision.** A SignalR hub with one group per board. The hub only pushes; every change goes through REST.

**Why.** Validation and permissions stay in one place.

**Rejected.** Sending changes over the hub.

**What happened.** Done in M5. Clients send nothing but `JoinBoard`. Each message names the tab that caused it so that tab can skip its own echo, and a client reloads the board once after a reconnect.
