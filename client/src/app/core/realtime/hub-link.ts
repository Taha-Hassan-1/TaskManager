import { InjectionToken, inject } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';

/**
 * The little of SignalR the app uses. Everything else depends on this, not on the library, so tests can
 * drive "messages arrive" and "the connection drops" with a plain fake.
 */
export interface HubLink {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(method: string, ...args: unknown[]): Promise<unknown>;
  on(method: string, handler: (body: never) => void): void;
  onReconnecting(callback: () => void): void;
  onReconnected(callback: () => void): void;
  onClose(callback: () => void): void;
}

export type HubLinkFactory = () => HubLink;

/** Quick at first, then every 30 seconds, for as long as it takes: a laptop can sleep for hours. */
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000];
const MAX_RECONNECT_DELAY_MS = 30_000;

export const HUB_LINK_FACTORY = new InjectionToken<HubLinkFactory>('HUB_LINK_FACTORY', {
  providedIn: 'root',
  factory: () => {
    const auth = inject(AuthService);
    return () =>
      signalRLink(async () => {
        // Asked for on every connect and reconnect, so a long-lived tab never sends an expired token.
        const token = await auth.freshAccessToken();
        if (!token) {
          void auth.sessionExpired();
          throw new Error('Signed out.');
        }
        return token;
      });
  },
});

export function signalRLink(accessToken: () => Promise<string>): HubLink {
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/board', { accessTokenFactory: accessToken })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) => RECONNECT_DELAYS_MS[previousRetryCount] ?? MAX_RECONNECT_DELAY_MS,
    })
    .configureLogging(LogLevel.Warning)
    .build();

  return {
    start: () => connection.start(),
    stop: () => connection.stop(),
    invoke: (method, ...args) => connection.invoke(method, ...args),
    on: (method, handler) => connection.on(method, handler as (...args: unknown[]) => void),
    onReconnecting: (callback) => connection.onreconnecting(() => callback()),
    onReconnected: (callback) => connection.onreconnected(() => callback()),
    onClose: (callback) => connection.onclose(() => callback()),
  };
}
