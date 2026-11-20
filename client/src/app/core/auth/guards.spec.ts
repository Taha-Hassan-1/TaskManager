import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { BoardCache } from '../board-cache';
import { Notifier } from '../notifier';
import { AuthService } from './auth.service';
import { authGuard, boardGuard, guestGuard } from './guards';

describe('route guards', () => {
  let signedIn = false;
  let cache: { prefetch: ReturnType<typeof vi.fn> };
  let notifier: { error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    signedIn = false;
    cache = { prefetch: vi.fn() };
    notifier = { error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isSignedIn: () => signedIn } },
        { provide: BoardCache, useValue: cache },
        { provide: Notifier, useValue: notifier },
      ],
    });
  });

  const run = <T>(guard: (...a: never[]) => T, route: Partial<ActivatedRouteSnapshot> = {}, url = '/boards/1') =>
    TestBed.runInInjectionContext(() => (guard as unknown as (r: unknown, s: unknown) => T)(route, { url } as RouterStateSnapshot));

  const asUrl = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);
  const boardRoute = (id: string) => ({ paramMap: convertToParamMap({ id }) });

  describe('authGuard', () => {
    it('lets signed-in users through', () => {
      signedIn = true;
      expect(run(authGuard)).toBe(true);
    });

    it('sends everyone else to sign in, remembering the page they wanted', () => {
      expect(asUrl(run(authGuard, {}, '/boards/7'))).toBe('/login?returnUrl=%2Fboards%2F7');
    });
  });

  describe('guestGuard', () => {
    it('lets signed-out visitors see the sign-in page', () => expect(run(guestGuard)).toBe(true));

    it('sends signed-in users to their boards', () => {
      signedIn = true;
      expect(asUrl(run(guestGuard))).toBe('/boards');
    });
  });

  describe('boardGuard', () => {
    it('lets the user in once the board has loaded', async () => {
      cache.prefetch.mockResolvedValue(undefined);
      expect(await run(boardGuard, boardRoute('5'))).toBe(true);
      expect(cache.prefetch).toHaveBeenCalledWith(5);
    });

    it('explains a 404 on the boards list instead of showing an empty board', async () => {
      cache.prefetch.mockRejectedValue(new HttpErrorResponse({ status: 404 }));

      expect(asUrl(await run(boardGuard, boardRoute('5')))).toBe('/boards');
      expect(notifier.error).toHaveBeenCalledWith('That board does not exist, or you are not a member of it.');
    });

    it('explains a 403', async () => {
      cache.prefetch.mockRejectedValue(new HttpErrorResponse({ status: 403 }));

      expect(asUrl(await run(boardGuard, boardRoute('5')))).toBe('/boards');
      expect(notifier.error).toHaveBeenCalledWith('You do not have access to that board.');
    });

    it('treats any other failure as "could not load"', async () => {
      cache.prefetch.mockRejectedValue(new HttpErrorResponse({ status: 500 }));

      await run(boardGuard, boardRoute('5'));

      expect(notifier.error).toHaveBeenCalledWith('The board could not be loaded. Try again.');
    });

    it('ignores an id that is not a number, without calling the server', async () => {
      expect(asUrl(await run(boardGuard, boardRoute('abc')))).toBe('/boards');
      expect(cache.prefetch).not.toHaveBeenCalled();
    });
  });
});
