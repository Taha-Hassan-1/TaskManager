import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TeamBoardApi } from '../api/team-board-api';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const session = (token: string) => ({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z', user: { id: 1, username: 'ann', email: 'ann@example.com' } });

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let auth: AuthService;
  let router: Router;

  beforeEach(async () => {
    api = { login: vi.fn().mockResolvedValue(session('t1')), refresh: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: TeamBoardApi, useValue: api },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await auth.login({ email: 'ann@example.com', password: 'x' });
  });

  afterEach(() => backend.verify());

  /** The request shows up after a promise or two (the refresh runs between), so wait for it. */
  const request = (url: string) => vi.waitFor(() => backend.expectOne(url));

  it('adds the access token to API requests', () => {
    http.get('/api/boards').subscribe();

    const req = backend.expectOne('/api/boards');
    expect(req.request.headers.get('Authorization')).toBe('Bearer t1');
    req.flush([]);
  });

  it('leaves requests to other addresses alone', () => {
    http.get('/assets/logo.svg').subscribe();

    const req = backend.expectOne('/assets/logo.svg');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush('');
  });

  it('marks the cookie-authenticated /api/auth calls with the CSRF header, and sends no token', () => {
    http.post('/api/auth/refresh', {}).subscribe();

    const req = backend.expectOne('/api/auth/refresh');
    expect(req.request.headers.get('X-Requested-With')).toBe('TeamBoard');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('on a 401, refreshes the token once and retries the request with the new one', async () => {
    api['refresh'].mockResolvedValue(session('t2'));
    const results: unknown[] = [];
    http.get('/api/boards').subscribe((r) => results.push(r));

    backend.expectOne('/api/boards').flush(null, { status: 401, statusText: 'Unauthorized' });
    const retry = await request('/api/boards');

    expect(retry.request.headers.get('Authorization')).toBe('Bearer t2');
    retry.flush(['board']);
    expect(results).toEqual([['board']]);
    expect(api['refresh']).toHaveBeenCalledTimes(1);
  });

  it('does not loop: a second 401 after the retry goes to the caller', async () => {
    api['refresh'].mockResolvedValue(session('t2'));
    const errors: number[] = [];
    http.get('/api/boards').subscribe({ error: (e) => errors.push(e.status) });

    backend.expectOne('/api/boards').flush(null, { status: 401, statusText: 'Unauthorized' });
    (await request('/api/boards')).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(errors).toEqual([401]);
    expect(api['refresh']).toHaveBeenCalledTimes(1);
  });

  it('when the refresh token is no good, ends the session and sends the user to sign in', async () => {
    api['refresh'].mockRejectedValue(new Error('401'));
    const errors: number[] = [];
    http.get('/api/boards').subscribe({ error: (e) => errors.push(e.status) });

    backend.expectOne('/api/boards').flush(null, { status: 401, statusText: 'Unauthorized' });
    await vi.waitFor(() => expect(errors).toEqual([401]));

    expect(auth.isSignedIn()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], expect.anything());
  });

  it('several requests failing together trigger a single refresh', async () => {
    api['refresh'].mockResolvedValue(session('t2'));
    http.get('/api/boards').subscribe();
    http.get('/api/boards/1').subscribe();

    backend.expectOne('/api/boards').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/boards/1').flush(null, { status: 401, statusText: 'Unauthorized' });
    (await request('/api/boards')).flush([]);
    (await request('/api/boards/1')).flush({});

    expect(api['refresh']).toHaveBeenCalledTimes(1);
  });

  it('passes other errors through untouched, without refreshing', () => {
    const errors: number[] = [];
    http.get('/api/boards/9').subscribe({ error: (e) => errors.push(e.status) });

    backend.expectOne('/api/boards/9').flush(null, { status: 404, statusText: 'Not Found' });

    expect(errors).toEqual([404]);
    expect(api['refresh']).not.toHaveBeenCalled();
  });

  it('never tries to refresh when the /api/auth call itself is refused', () => {
    const errors: number[] = [];
    http.post('/api/auth/login', {}).subscribe({ error: (e) => errors.push(e.status) });

    backend.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(errors).toEqual([401]);
    expect(api['refresh']).not.toHaveBeenCalled();
  });
});
