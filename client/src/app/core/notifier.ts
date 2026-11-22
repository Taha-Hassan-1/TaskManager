import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);

  info(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 3000 });
  }

  error(message: string): void {
    // An error carries something to act on, so it stays until the person dismisses it.
    this.snackBar.open(message, 'Dismiss', { panelClass: 'snack-error' });
  }
}
