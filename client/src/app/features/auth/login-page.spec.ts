import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let auth: { login: ReturnType<typeof vi.fn> };
  let router: Router;

  const setup = () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges(); // first render: wires up the form
    const el = fixture.nativeElement as HTMLElement;
    const type = (selector: string, value: string) => {
      const input = el.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, el, type, submit };
  };

  beforeEach(() => {
    auth = { login: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: auth }] });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  it('does not call the server until the form is valid, and says what is missing', async () => {
    const { el, submit } = setup();

    await submit();

    expect(auth.login).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Enter your email address.');
    expect(el.textContent).toContain('Enter your password.');
  });

  it('rejects something that is not an email address', async () => {
    const { el, type, submit } = setup();
    type('input[type=email]', 'not-an-email');
    type('input[type=password]', 'whatever');

    await submit();

    expect(auth.login).not.toHaveBeenCalled();
    expect(el.textContent).toContain('does not look like an email');
  });

  it('signs in and goes to the boards', async () => {
    auth.login.mockResolvedValue(undefined);
    const { type, submit } = setup();
    type('input[type=email]', 'ann@example.com');
    type('input[type=password]', 'correct horse battery');

    await submit();

    expect(auth.login).toHaveBeenCalledWith({ email: 'ann@example.com', password: 'correct horse battery' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/boards');
  });

  it('shows one plain message for a wrong password', async () => {
    auth.login.mockRejectedValue(new HttpErrorResponse({ status: 401, error: { title: 'Invalid email or password.' } }));
    const { fixture, el, type, submit } = setup();
    type('input[type=email]', 'ann@example.com');
    type('input[type=password]', 'wrong');

    await submit();
    fixture.detectChanges();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('Wrong email or password.');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
