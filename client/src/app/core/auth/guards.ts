import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { BoardCache } from '../board-cache';
import { Notifier } from '../notifier';
import { AuthService } from './auth.service';

/** Signed-in pages. The session was restored at startup, so this is a plain check. */
export const authGuard: CanActivateFn = (_route, state) => {
  if (inject(AuthService).isSignedIn()) return true;
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Sign-in and register pages are for people who are not signed in. */
export const guestGuard: CanActivateFn = () => {
  if (!inject(AuthService).isSignedIn()) return true;
  return inject(Router).createUrlTree(['/boards']);
};

/**
 * Loads the board before the screen opens, so a board that does not exist, or that the user is not in,
 * never flashes an empty page: it explains itself on the boards list instead. The loaded board is handed
 * to the board screen through the cache so it is fetched only once.
 */
export const boardGuard: CanActivateFn = async (route) => {
  // inject() only works before the first await, so everything is fetched up front.
  const router = inject(Router);
  const cache = inject(BoardCache);
  const notifier = inject(Notifier);

  const id = Number(route.paramMap.get('id'));
  if (!Number.isInteger(id)) return router.createUrlTree(['/boards']);

  try {
    await cache.prefetch(id);
    return true;
  } catch (error) {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    notifier.error(
      status === 403 ? 'You do not have access to that board.'
      : status === 404 ? 'That board does not exist, or you are not a member of it.'
      : 'The board could not be loaded. Try again.',
    );
    return router.createUrlTree(['/boards']);
  }
};
