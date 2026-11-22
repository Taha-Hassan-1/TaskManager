import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { BoardRole, MemberDto } from '../../api/models';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { AuthService } from '../../core/auth/auth.service';
import { toProblem } from '../../core/errors';
import { Notifier } from '../../core/notifier';
import { Avatar } from '../../shared/avatar';
import { ConfirmDialog } from '../../shared/dialogs';
import { BoardStore } from './board.store';

@Component({
  selector: 'app-members-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, Avatar],
  template: `
    <h2 mat-dialog-title>Members</h2>
    <mat-dialog-content>
      <ul class="members">
        @for (member of store.members(); track member.userId) {
          <li>
            <app-avatar [name]="member.username" />
            <div class="who">
              <strong>{{ member.username }}</strong>
              <span>{{ member.email }}</span>
            </div>
            @if (store.isOwner() && member.role !== 'Owner') {
              <mat-form-field subscriptSizing="dynamic" class="role">
                <mat-select [value]="member.role" (selectionChange)="changeRole(member, $event.value)" [attr.aria-label]="'Role of ' + member.username">
                  <mat-option value="Editor">Editor</mat-option>
                  <mat-option value="Viewer">Viewer</mat-option>
                </mat-select>
              </mat-form-field>
              <button mat-icon-button (click)="remove(member)" [attr.aria-label]="'Remove ' + member.username"><mat-icon>person_remove</mat-icon></button>
            } @else {
              <span class="role-text">{{ member.role }}</span>
              @if (member.userId === auth.user()?.id && member.role !== 'Owner') {
                <button mat-button (click)="leave(member)">Leave</button>
              }
            }
          </li>
        }
      </ul>

      @if (store.isOwner()) {
        <h3>Invite someone</h3>
        <p class="hint">They need an account already. Invite them by the email they registered with.</p>
        <form class="invite" (ngSubmit)="invite()">
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="email">
            <mat-label>Email</mat-label>
            <input matInput type="email" name="email" [(ngModel)]="email" autocomplete="off" />
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="role">
            <mat-label>Role</mat-label>
            <mat-select name="role" [(ngModel)]="role">
              <mat-option value="Editor">Editor</mat-option>
              <mat-option value="Viewer">Viewer</mat-option>
            </mat-select>
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="!email.trim() || busy()">Invite</button>
        </form>
        @if (error(); as message) { <p class="form-error" role="alert">{{ message }}</p> }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Close</button></mat-dialog-actions>
  `,
  styles: `
    .members { list-style: none; margin: 0; padding: 0; min-width: min(420px, 100%); }
    li { display: flex; align-items: center; gap: 12px; padding: 6px 0; flex-wrap: wrap; }
    .who { display: flex; flex-direction: column; flex: 1; min-width: 140px; overflow-wrap: anywhere; span { font-size: 12px; color: var(--mat-sys-on-surface-variant); } }
    .role { width: 120px; }
    .role-text { color: var(--mat-sys-on-surface-variant); }
    h3 { margin: 24px 0 4px; font-weight: 500; }
    .hint { margin: 0 0 12px; font-size: 13px; color: var(--mat-sys-on-surface-variant); }
    .invite { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .email { flex: 1; min-width: 200px; }
  `,
})
export class MembersDialog {
  readonly store = inject(BoardStore);
  readonly auth = inject(AuthService);
  private readonly api = inject(TeamBoardApi);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  email = '';
  role: BoardRole = 'Editor';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async invite(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      await this.api.invite(this.store.boardId(), this.email.trim(), this.role);
      this.email = '';
      await this.store.reload();
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  async changeRole(member: MemberDto, role: BoardRole): Promise<void> {
    try {
      await this.api.changeRole(this.store.boardId(), member.userId, role);
      await this.store.reload();
    } catch (e) {
      this.notifier.error(toProblem(e).message);
      await this.store.reload(); // put the select back to the truth
    }
  }

  remove(member: MemberDto): void {
    this.confirm(`Remove ${member.username}?`, `${member.username} loses access to this board. You can invite them again later.`, 'Remove member', async () => {
      try {
        await this.api.removeMember(this.store.boardId(), member.userId);
        await this.store.reload();
      } catch (e) {
        this.notifier.error(toProblem(e).message);
      }
    });
  }

  leave(member: MemberDto): void {
    this.confirm('Leave this board?', 'You lose access to it, and an owner has to invite you again to get back in.', 'Leave board', async () => {
      try {
        await this.api.removeMember(this.store.boardId(), member.userId);
        await this.router.navigate(['/boards']);
      } catch (e) {
        this.notifier.error(toProblem(e).message);
      }
    });
  }

  private confirm(title: string, message: string, confirmText: string, onConfirm: () => Promise<void>): void {
    this.dialog.open(ConfirmDialog, { data: { title, message, confirmText } }).afterClosed().subscribe((yes) => yes && void onConfirm());
  }
}
