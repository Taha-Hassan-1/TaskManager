import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * - adds the access token to API calls;
 * - on a 401, refreshes the token once and retries the request once;
 * - marks the cookie-authenticated /api/auth calls with the header the server requires against CSRF.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);

  if (req.url.startsWith('/api/auth/')) {
    return next(req.clone({ setHeaders: { 'X-Requested-With': 'TeamBoard' } }));
  }

  const auth = inject(AuthService);
  const withToken = (r: HttpRequest<unknown>) => {
    const token = auth.accessToken;
    return token ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : r;
  };

  return next(withToken(req)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) return throwError(() => error);

      return from(auth.refresh()).pipe(
        switchMap((refreshed) => {
          if (refreshed) return next(withToken(req)); // a second 401 here goes to the caller: no loop
          void auth.sessionExpired();
          return throwError(() => error);
        }),
      );
    }),
  );
};
