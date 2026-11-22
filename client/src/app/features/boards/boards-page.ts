import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { BoardSummaryDto } from '../../api/models';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { toProblem } from '../../core/errors';
import { Notifier } from '../../core/notifier';

@Component({
  selector: 'app-boards-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule],
  template: `
    <main class="page">
      <h1>Your boards</h1>

      <form class="create" (ngSubmit)="create()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>New board name</mat-label>
          <input matInput name="name" [(ngModel)]="name" maxlength="100" autocomplete="off" />
        </mat-form-field>
        <button mat-flat-button type="submit" [disabled]="!name.trim() || creating()">
          <mat-icon>add</mat-icon> Create board
        </button>
      </form>

      @if (loading()) {
        <mat-progress-bar mode="indeterminate" aria-label="Loading boards" />
      } @else if (loadError(); as message) {
        <p class="form-error" role="alert">{{ message }} <button mat-button (click)="load()">Try again</button></p>
      } @else if (boards().length === 0) {
        <p class="empty">You are not on any board yet. Create one above, or ask a teammate to invite you.</p>
      } @else {
        <ul class="boards">
          @for (board of boards(); track board.id) {
            <li>
              <a [routerLink]="['/boards', board.id]" class="board-link">
                <span class="name">{{ board.name }}</span>
                <span class="meta">{{ board.cardCount }} {{ board.cardCount === 1 ? 'card' : 'cards' }} · {{ board.myRole }}</span>
              </a>
            </li>
          }
        </ul>
      }
    </main>
  `,
  styles: `
    h1 { font-weight: 600; font-size: 28px; letter-spacing: -0.01em; margin: 0 0 16px; }
    .create { display: flex; gap: 12px; align-items: center; margin-bottom: 24px; flex-wrap: wrap; }
    .boards { list-style: none; padding: 0; margin: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(min(240px, 100%), 1fr)); gap: 16px; }
    .board-link {
      min-width: 0;
      display: flex; flex-direction: column; gap: 6px; padding: 20px; min-height: 64px;
      background: var(--tb-card-bg); border-radius: 14px; text-decoration: none; color: inherit;
      border: 1px solid var(--tb-hairline); border-top: 4px solid var(--mat-sys-primary);
      box-shadow: var(--tb-shadow-card);
      transition: box-shadow 150ms ease, transform 150ms ease;
      &:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
      @media (hover: hover) { &:hover { box-shadow: 0 8px 20px rgba(16, 24, 40, 0.12); } }
      @media (hover: hover) and (prefers-reduced-motion: no-preference) { &:hover { transform: translateY(-2px); } }
      @media (prefers-reduced-motion: reduce) { transition: none; }
    }
    .name { font-size: 18px; font-weight: 500; overflow-wrap: anywhere; text-wrap: balance; }
    .meta, .empty { color: var(--mat-sys-on-surface-variant); }
    .meta { font-variant-numeric: tabular-nums; }
  `,
})
export class BoardsPage {
  private readonly api = inject(TeamBoardApi);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  readonly boards = signal<BoardSummaryDto[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly creating = signal(false);
  name = '';

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.boards.set(await this.api.listBoards());
    } catch (e) {
      this.loadError.set(toProblem(e).message);
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> {
    const name = this.name.trim();
    if (!name) return;
    this.creating.set(true);
    try {
      const board = await this.api.createBoard(name);
      await this.router.navigate(['/boards', board.id]);
    } catch (e) {
      this.notifier.error(toProblem(e).message);
    } finally {
      this.creating.set(false);
    }
  }
}
