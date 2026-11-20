import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TeamBoardApi } from '../api/team-board-api';
import { AuthService } from './auth.service';

const session = (token: string) => ({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z', user: { id: 1, username: 'ann', email: 'ann@example.com' } });

describe('AuthService', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    api = { login: vi.fn(), register: vi.fn(), refresh: vi.fn(), logout: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: TeamBoardApi, useValue: api }] });
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  it('starts signed out', () => {
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.accessToken).toBeNull();
  });

  it('keeps the access token and user in memory after login', async () => {
    api['login'].mockResolvedValue(session('t1'));

    await auth.login({ email: 'ann@example.com', password: 'secret secret' });

    expect(auth.accessToken).toBe('t1');
    expect(auth.user()?.username).toBe('ann');
    expect(auth.isSignedIn()).toBe(true);
    expect(localStorage.length + sessionStorage.length).toBe(0); // never written to browser storage
  });

  it('stays signed out when login is refused', async () => {
    api['login'].mockRejectedValue(new Error('401'));
    await expect(auth.login({ email: 'a@b.c', password: 'x' })).rejects.toThrow();
    expect(auth.isSignedIn()).toBe(false);
  });

  describe('restoreSession', () => {
    it('signs back in from the refresh cookie', async () => {
      api['refresh'].mockResolvedValue(session('restored'));
      await auth.restoreSession();
      expect(auth.accessToken).toBe('restored');
    });

    it('is quiet when there is no cookie', async () => {
      api['refresh'].mockRejectedValue(new Error('401'));
      await expect(auth.restoreSession()).resolves.toBeUndefined();
      expect(auth.isSignedIn()).toBe(false);
    });
  });

  describe('refresh', () => {
    it('shares one call between concurrent callers, because the refresh token rotates', async () => {
      api['refresh'].mockResolvedValue(session('t2'));

      const results = await Promise.all([auth.refresh(), auth.refresh(), auth.refresh()]);

      expect(results).toEqual([true, true, true]);
      expect(api['refresh']).toHaveBeenCalledTimes(1);
    });

    it('can run again once the first has finished', async () => {
      api['refresh'].mockResolvedValue(session('t2'));
      await auth.refresh();
      await auth.refresh();
      expect(api['refresh']).toHaveBeenCalledTimes(2);
    });

    it('reports failure and signs out', async () => {
      api['login'].mockResolvedValue(session('t1'));
      await auth.login({ email: 'a@b.c', password: 'x' });
      api['refresh'].mockRejectedValue(new Error('401'));

      expect(await auth.refresh()).toBe(false);
      expect(auth.isSignedIn()).toBe(false);
      expect(auth.accessToken).toBeNull();
    });
  });

  describe('freshAccessToken (used by the live connection)', () => {
    it('returns the current token while it has time left', async () => {
      api['login'].mockResolvedValue(session('t1'));
      await auth.login({ email: 'a@b.c', password: 'x' });

      expect(await auth.freshAccessToken()).toBe('t1');
      expect(api['refresh']).not.toHaveBeenCalled();
    });

    it('refreshes first when the token is about to expire', async () => {
      api['login'].mockResolvedValue({ ...session('old'), expiresAt: new Date(Date.now() + 10_000).toISOString() });
      api['refresh'].mockResolvedValue(session('new'));
      await auth.login({ email: 'a@b.c', password: 'x' });

      expect(await auth.freshAccessToken()).toBe('new');
    });

    it('refreshes when there is no token at all, and returns null when that fails', async () => {
      api['refresh'].mockRejectedValue(new Error('401'));
      expect(await auth.freshAccessToken()).toBeNull();
      expect(api['refresh']).toHaveBeenCalledTimes(1);
    });
  });

  it('logout ends the local session even when the server cannot be reached', async () => {
    api['login'].mockResolvedValue(session('t1'));
    api['logout'].mockRejectedValue(new Error('offline'));
    await auth.login({ email: 'a@b.c', password: 'x' });

    await auth.logout();

    expect(auth.isSignedIn()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('sessionExpired sends the user to sign in, remembering where they were', async () => {
    api['login'].mockResolvedValue(session('t1'));
    await auth.login({ email: 'a@b.c', password: 'x' });

    await auth.sessionExpired();

    expect(auth.isSignedIn()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: expect.any(String) } });
  });
});
