import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { ClientId } from '../realtime/client-id';
import { AuthService } from './auth.service';

/**
 * - tells the server which tab is calling (X-Client-Id), so live updates can skip that tab's own echo;
 * - adds the access token to API calls;
 * - on a 401, refreshes the token once and retries the request once;
 * - marks the cookie-authenticated /api/auth calls with the header the server requires against CSRF.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);

  const clientId = inject(ClientId).value;

  if (req.url.startsWith('/api/auth/')) {
    return next(req.clone({ setHeaders: { 'X-Requested-With': 'TeamBoard', 'X-Client-Id': clientId } }));
  }

  const auth = inject(AuthService);
  const prepare = (r: HttpRequest<unknown>) => {
    const token = auth.accessToken;
    return r.clone({ setHeaders: token ? { 'X-Client-Id': clientId, Authorization: `Bearer ${token}` } : { 'X-Client-Id': clientId } });
  };

  return next(prepare(req)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) return throwError(() => error);

      return from(auth.refresh()).pipe(
        switchMap((refreshed) => {
          if (refreshed) return next(prepare(req)); // a second 401 here goes to the caller: no loop
          void auth.sessionExpired();
          return throwError(() => error);
        }),
      );
    }),
  );
};
