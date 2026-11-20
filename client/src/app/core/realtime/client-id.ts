import { Injectable } from '@angular/core';

/**
 * Names this browser tab. Every API request carries it (X-Client-Id) and the server copies it into the live
 * messages about that change, so the tab that made a change can recognise, and skip, the echo of its own.
 * Two tabs of the same user have different ids, so each still sees the other's changes.
 */
@Injectable({ providedIn: 'root' })
export class ClientId {
  readonly value =
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID()
      : Math.random().toString(36).slice(2) + Date.now().toString(36);
}
