import { HubLink } from '../core/realtime/hub-link';

/** A stand-in for the live connection that tests drive by hand: push a message, drop the link, bring it back. */
export class FakeHubLink implements HubLink {
  invocations: { method: string; args: unknown[] }[] = [];
  starts = 0;
  stops = 0;

  /** Make the next start() fail with this error. */
  failNextStart: Error | null = null;
  /** Make the next invoke() of this method fail with this error. */
  failNextInvoke = new Map<string, Error>();

  private readonly handlers = new Map<string, (body: never) => void>();
  private reconnecting: (() => void) | null = null;
  private reconnected: (() => unknown) | null = null;
  private closed: (() => void) | null = null;

  async start(): Promise<void> {
    this.starts++;
    if (this.failNextStart) {
      const error = this.failNextStart;
      this.failNextStart = null;
      throw error;
    }
  }

  async stop(): Promise<void> {
    this.stops++;
  }

  async invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invocations.push({ method, args });
    const failure = this.failNextInvoke.get(method);
    if (failure) {
      this.failNextInvoke.delete(method);
      throw failure;
    }
    return undefined;
  }

  on(method: string, handler: (body: never) => void): void {
    this.handlers.set(method, handler);
  }

  onReconnecting(callback: () => void): void { this.reconnecting = callback; }
  onReconnected(callback: () => void): void { this.reconnected = callback; }
  onClose(callback: () => void): void { this.closed = callback; }

  // ---- what the tests do to it ----

  /** The server pushes a message. */
  emit(method: string, body: object): void {
    const handler = this.handlers.get(method);
    if (!handler) throw new Error(`Nothing is listening for ${method}`);
    handler(body as never);
  }

  /** The connection drops and the library starts trying to get it back. */
  drop(): void { this.reconnecting?.(); }

  /** The library got the connection back (a new connection, with no groups joined). */
  async restore(): Promise<void> { await this.reconnected?.(); }

  /** The library gave up. */
  giveUp(): void { this.closed?.(); }

  joins(): number[] {
    return this.invocations.filter((i) => i.method === 'JoinBoard').map((i) => i.args[0] as number);
  }
}
