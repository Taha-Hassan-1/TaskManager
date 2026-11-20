import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivityDto } from '../../api/models';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { toProblem } from '../../core/errors';
import { Avatar } from '../../shared/avatar';
import { describeActivity } from './activity-text';
import { BoardStore } from './board.store';

const PAGE_SIZE = 30;

/** Who did what on this board, newest first. New entries appear at the top as they happen. */
@Component({
  selector: 'app-activity-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatIconModule, Avatar],
  template: `
    <header>
      <h2>Activity</h2>
      <ng-content />
    </header>

    @if (error(); as message) {
      <p class="form-error" role="alert">{{ message }} <button mat-button (click)="loadFirstPage()">Try again</button></p>
    }

    <ul aria-live="polite" aria-label="Recent activity">
      @for (entry of entries(); track entry.id) {
        <li>
          <app-avatar [name]="entry.actorName" [size]="24" />
          <div>
            <p><strong>{{ entry.actorName }}</strong> {{ describe(entry) }}</p>
            <time [attr.datetime]="entry.createdAt">{{ entry.createdAt | date: 'MMM d, HH:mm' }}</time>
          </div>
        </li>
      } @empty {
        @if (!loading()) { <li class="empty">Nothing has happened here yet.</li> }
      }
    </ul>

    @if (nextCursor()) {
      <button mat-button class="more" (click)="loadMore()" [disabled]="loading()">Show older activity</button>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; width: 320px; flex: none; background: var(--mat-sys-surface); border-left: 1px solid var(--mat-sys-outline-variant); overflow-y: auto; }
    header { display: flex; align-items: center; justify-content: space-between; padding: 8px 8px 8px 16px; position: sticky; top: 0; background: inherit; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    h2 { margin: 0; font-size: 16px; font-weight: 500; }
    ul { list-style: none; margin: 0; padding: 8px 16px; display: flex; flex-direction: column; gap: 14px; }
    li { display: flex; gap: 10px; }
    p { margin: 0; font-size: 13px; line-height: 18px; word-break: break-word; }
    time { font-size: 12px; color: var(--mat-sys-on-surface-variant); }
    .empty { color: var(--mat-sys-on-surface-variant); font-size: 13px; }
    .more { margin: 0 8px 16px; }
  `,
})
export class ActivityPanel {
  private readonly store = inject(BoardStore);
  private readonly api = inject(TeamBoardApi);

  readonly entries = signal<ActivityDto[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  private readonly columnNames = computed(() => new Map(this.store.columns().map((c) => [c.id, c.name])));

  constructor() {
    void this.loadFirstPage();

    // A new entry pushed live goes to the top, unless the page already has it.
    effect(() => {
      const entry = this.store.latestActivity();
      if (!entry) return;
      untracked(() => this.entries.update((list) => (list.some((e) => e.id === entry.id) ? list : [entry, ...list])));
    });
  }

  describe(entry: ActivityDto): string {
    return describeActivity(entry, this.columnNames());
  }

  async loadFirstPage(): Promise<void> {
    this.error.set(null);
    this.loading.set(true);
    try {
      const page = await this.api.activity(this.store.boardId(), undefined, PAGE_SIZE);
      // Anything that arrived live while the first page was loading is kept, without duplicates.
      this.entries.update((live) => [...live, ...page.items.filter((p) => !live.some((l) => l.id === p.id))].sort((a, b) => b.id - a.id));
      this.nextCursor.set(page.nextCursor);
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.loading.set(false);
    }
  }

  async loadMore(): Promise<void> {
    const cursor = this.nextCursor();
    if (!cursor) return;
    this.loading.set(true);
    try {
      const page = await this.api.activity(this.store.boardId(), cursor, PAGE_SIZE);
      this.entries.update((list) => [...list, ...page.items.filter((p) => !list.some((l) => l.id === p.id))]);
      this.nextCursor.set(page.nextCursor);
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.loading.set(false);
    }
  }
}
