import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthResponse, LoginRequest, RegisterRequest, UserDto } from '../../api/models';
import { TeamBoardApi } from '../api/team-board-api';

/**
 * Holds the session. The access token lives only in memory (never localStorage), so a page reload
 * starts without one and gets a new one from the HttpOnly refresh cookie.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(TeamBoardApi);
  private readonly router = inject(Router);

  private readonly token = signal<string | null>(null);
  readonly user = signal<UserDto | null>(null);
  readonly isSignedIn = computed(() => this.user() !== null);

  private refreshing: Promise<boolean> | null = null;
  private expiresAt = 0;

  get accessToken(): string | null {
    return this.token();
  }

  /**
   * A token that is good for at least another half minute, refreshing first if needed. The live connection
   * asks for one every time it (re)connects, which can be long after the last one expired.
   * Returns null when there is no session any more.
   */
  async freshAccessToken(): Promise<string | null> {
    if (!this.token() || Date.now() > this.expiresAt - 30_000) await this.refresh();
    return this.token();
  }

  async login(request: LoginRequest): Promise<void> {
    this.apply(await this.api.login(request));
  }

  async register(request: RegisterRequest): Promise<void> {
    this.apply(await this.api.register(request));
  }

  /** On app start: pick the session back up from the refresh cookie, if there is one. Never throws. */
  async restoreSession(): Promise<void> {
    await this.refresh();
  }

  /**
   * Gets a new access token. Concurrent callers (several requests failing with 401 at once) share one
   * call, because the refresh token rotates and a second call with the old one would look like theft.
   */
  refresh(): Promise<boolean> {
    this.refreshing ??= this.doRefresh().finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  async logout(): Promise<void> {
    try {
      await this.api.logout();
    } catch {
      // The cookie may already be gone; the local session ends either way.
    }
    this.clear();
    await this.router.navigate(['/login']);
  }

  /** The refresh token is no longer good: back to the sign-in page, returning here afterwards. */
  async sessionExpired(): Promise<void> {
    this.clear();
    await this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
  }

  private async doRefresh(): Promise<boolean> {
    try {
      this.apply(await this.api.refresh());
      return true;
    } catch {
      this.clear();
      return false;
    }
  }

  private apply(response: AuthResponse): void {
    this.token.set(response.accessToken);
    this.expiresAt = Date.parse(response.expiresAt);
    this.user.set(response.user);
  }

  private clear(): void {
    this.expiresAt = 0;
    this.token.set(null);
    this.user.set(null);
  }
}
