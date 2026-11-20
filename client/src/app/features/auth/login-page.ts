import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { toProblem } from '../../core/errors';
import { applyServerErrors, safeReturnUrl } from '../../core/forms';

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <main class="auth-card">
      <h1>Sign in to TeamBoard</h1>
      @if (error(); as message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="email" />
          @if (form.controls.email.hasError('required')) { <mat-error>Enter your email address.</mat-error> }
          @else if (form.controls.email.hasError('email')) { <mat-error>That does not look like an email address.</mat-error> }
          @else if (form.controls.email.hasError('server')) { <mat-error>{{ form.controls.email.getError('server') }}</mat-error> }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Password</mat-label>
          <input matInput type="password" formControlName="password" autocomplete="current-password" />
          @if (form.controls.password.hasError('required')) { <mat-error>Enter your password.</mat-error> }
          @else if (form.controls.password.hasError('server')) { <mat-error>{{ form.controls.password.getError('server') }}</mat-error> }
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy()">Sign in</button>
      </form>
      <p>No account yet? <a routerLink="/register">Create one</a></p>
    </main>
  `,
  styleUrl: './auth-pages.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    this.error.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    try {
      await this.auth.login(this.form.getRawValue());
      await this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
    } catch (e) {
      const problem = toProblem(e);
      const unmatched = applyServerErrors(this.form, problem.fieldErrors);
      this.error.set(problem.status === 401 ? 'Wrong email or password.' : unmatched[0] ?? problem.message);
    } finally {
      this.busy.set(false);
    }
  }
}
