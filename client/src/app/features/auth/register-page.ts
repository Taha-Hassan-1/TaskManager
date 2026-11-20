import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { toProblem } from '../../core/errors';
import { applyServerErrors } from '../../core/forms';

@Component({
  selector: 'app-register-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <main class="auth-card">
      <h1>Create your account</h1>
      @if (error(); as message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-form-field appearance="outline">
          <mat-label>Username</mat-label>
          <input matInput formControlName="username" autocomplete="username" />
          <mat-hint>3 to 50 letters, digits, '_', '.' or '-'</mat-hint>
          @if (form.controls.username.hasError('required')) { <mat-error>Choose a username.</mat-error> }
          @else if (form.controls.username.hasError('minlength') || form.controls.username.hasError('pattern')) { <mat-error>3 to 50 letters, digits, '_', '.' or '-'.</mat-error> }
          @else if (form.controls.username.hasError('server')) { <mat-error>{{ form.controls.username.getError('server') }}</mat-error> }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="email" />
          @if (form.controls.email.hasError('required')) { <mat-error>Enter your email address.</mat-error> }
          @else if (form.controls.email.hasError('email')) { <mat-error>That does not look like an email address.</mat-error> }
          @else if (form.controls.email.hasError('server')) { <mat-error>{{ form.controls.email.getError('server') }}</mat-error> }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Password</mat-label>
          <input matInput type="password" formControlName="password" autocomplete="new-password" />
          <mat-hint>At least 10 characters</mat-hint>
          @if (form.controls.password.hasError('required')) { <mat-error>Choose a password.</mat-error> }
          @else if (form.controls.password.hasError('minlength')) { <mat-error>Use at least 10 characters.</mat-error> }
          @else if (form.controls.password.hasError('maxlength')) { <mat-error>Use at most 72 characters.</mat-error> }
          @else if (form.controls.password.hasError('server')) { <mat-error>{{ form.controls.password.getError('server') }}</mat-error> }
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="busy()">Create account</button>
      </form>
      <p>Already registered? <a routerLink="/login">Sign in</a></p>
    </main>
  `,
  styleUrl: './auth-pages.scss',
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // The same rules as the API, so most mistakes are caught before a request is made.
  readonly form = inject(NonNullableFormBuilder).group({
    username: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9_.-]+$/)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(72)]],
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
      await this.auth.register(this.form.getRawValue());
      await this.router.navigateByUrl('/boards');
    } catch (e) {
      const problem = toProblem(e);
      const unmatched = applyServerErrors(this.form, problem.fieldErrors);
      // A 409 names the clash ("Email is already registered"); show it on the matching field too.
      if (problem.status === 409) {
        const field = problem.message.toLowerCase().includes('username') ? 'username' : 'email';
        this.form.controls[field].setErrors({ server: problem.message });
      } else {
        this.error.set(unmatched[0] ?? problem.message);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
