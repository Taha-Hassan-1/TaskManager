# 7. Token storage in the browser

**Status:** Accepted

**Decision.** The access token in memory; the refresh token in an HttpOnly, Secure, SameSite=Strict cookie scoped to `/api/auth`.

**Why.** It keeps credentials out of `localStorage`, where any script on the page could read them.

**Rejected.** `localStorage`.

**What happened.** Done. A reload starts without a token and restores the session from the cookie. The two cookie-authenticated endpoints also require a custom header, as a second guard against cross-site requests.
