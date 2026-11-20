import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface ConfirmData {
  title: string;
  message: string;
  confirmText?: string;
}

@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [mat-dialog-close]="true" cdkFocusInitial>{{ data.confirmText ?? 'Confirm' }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

export interface NameDialogData {
  title: string;
  label: string;
  name: string;
  /** Pass a number or null to show the WIP limit field; leave undefined to hide it. */
  wipLimit?: number | null;
  confirmText?: string;
}

export interface NameDialogResult {
  name: string;
  wipLimit: number | null;
}

/** A name, and optionally a column's WIP limit. */
@Component({
  selector: 'app-name-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, FormsModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content class="content">
      <mat-form-field appearance="outline">
        <mat-label>{{ data.label }}</mat-label>
        <input matInput [(ngModel)]="name" maxlength="100" required cdkFocusInitial (keydown.enter)="submit()" />
      </mat-form-field>
      @if (data.wipLimit !== undefined) {
        <mat-form-field appearance="outline">
          <mat-label>WIP limit (optional)</mat-label>
          <input matInput type="number" min="1" max="1000" [(ngModel)]="wipLimit" (keydown.enter)="submit()" />
          <mat-hint>A soft limit: the column warns when it holds more cards.</mat-hint>
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="submit()" [disabled]="!name.trim()">{{ data.confirmText ?? 'Save' }}</button>
    </mat-dialog-actions>
  `,
  styles: `.content { display: flex; flex-direction: column; gap: 8px; min-width: 320px; padding-top: 8px; }`,
})
export class NameDialog {
  readonly data = inject<NameDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<NameDialog, NameDialogResult>>(MatDialogRef);

  name = this.data.name;
  wipLimit: number | null = this.data.wipLimit ?? null;

  submit(): void {
    if (!this.name.trim()) return;
    this.ref.close({ name: this.name.trim(), wipLimit: this.wipLimit ? Number(this.wipLimit) : null });
  }
}
