import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenu, MatMenuModule } from '@angular/material/menu';
import { CardDto, LabelDto, MemberDto } from '../../api/models';
import { Avatar } from '../../shared/avatar';
import { LabelChip } from '../../shared/label-chip';

@Component({
  selector: 'app-card-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, LabelChip, MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    <div
      class="tile"
      [class]="'priority-' + card().priority.toLowerCase()"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Open card: ' + card().title"
      (click)="open.emit()"
      (keydown.enter)="open.emit()"
    >
      @if (labels().length) {
        <div class="labels">
          @for (label of labels(); track label.id) { <app-label-chip [label]="label" /> }
        </div>
      }
      <div class="title">{{ card().title }}</div>
      <div class="meta">
        @if (card().priority === 'High' || card().priority === 'Urgent') {
          <span class="badge priority" [title]="card().priority + ' priority'"><mat-icon>priority_high</mat-icon>{{ card().priority }}</span>
        }
        @if (card().dueDate) {
          <span class="badge" [class.overdue]="overdue()" title="Due date"><mat-icon>event</mat-icon>{{ card().dueDate!.slice(0, 10) }}</span>
        }
        @if (card().commentCount > 0) {
          <span class="badge" title="Comments"><mat-icon>chat_bubble_outline</mat-icon>{{ card().commentCount }}</span>
        }
        <span class="spacer"></span>
        @if (assignee(); as person) { <app-avatar [name]="person.username" [size]="24" /> }
      </div>
      @if (canEdit()) {
        <button
          mat-icon-button
          class="menu"
          [matMenuTriggerFor]="menu()"
          [matMenuTriggerData]="{ card: card() }"
          (click)="$event.stopPropagation()"
          (keydown.enter)="$event.stopPropagation()"
          aria-label="Card options"
        >
          <mat-icon>more_vert</mat-icon>
        </button>
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .tile {
      position: relative; background: var(--mat-sys-surface); border-radius: 8px; padding: 10px 12px;
      border-left: 4px solid transparent; box-shadow: var(--mat-sys-level1); cursor: pointer;
      &:hover, &:focus-visible { box-shadow: var(--mat-sys-level2); outline: none; }
      &:focus-visible { outline: 2px solid var(--mat-sys-primary); }
    }
    .priority-urgent { border-left-color: #d32f2f; }
    .priority-high { border-left-color: #f57c00; }
    .priority-low { border-left-color: #90a4ae; }
    .labels { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 6px; }
    .title { font-size: 14px; line-height: 20px; word-break: break-word; padding-right: 28px; }
    .meta { display: flex; align-items: center; gap: 8px; margin-top: 8px; min-height: 24px; }
    .spacer { flex: 1; }
    .badge {
      display: inline-flex; align-items: center; gap: 2px; font-size: 12px; color: var(--mat-sys-on-surface-variant);
      mat-icon { font-size: 16px; width: 16px; height: 16px; }
    }
    .badge.overdue { color: var(--mat-sys-error); font-weight: 500; }
    .badge.priority { color: #b85c00; font-weight: 500; }
    .menu { position: absolute; top: 2px; right: 0; opacity: 0.6; &:hover, &:focus-visible { opacity: 1; } }
  `,
})
export class CardTile {
  readonly card = input.required<CardDto>();
  readonly membersById = input.required<Map<number, MemberDto>>();
  readonly labelsById = input.required<Map<number, LabelDto>>();
  readonly canEdit = input(false);
  /** Shared by every tile, so a board with hundreds of cards creates one menu, not hundreds. */
  readonly menu = input.required<MatMenu>();
  readonly open = output<void>();

  readonly labels = computed(() =>
    this.card().labelIds.map((id) => this.labelsById().get(id)).filter((l): l is LabelDto => !!l),
  );
  readonly assignee = computed(() => {
    const id = this.card().assigneeId;
    return id === null ? undefined : this.membersById().get(id);
  });
  readonly overdue = computed(() => {
    const due = this.card().dueDate;
    return !!due && due.slice(0, 10) < new Date().toISOString().slice(0, 10);
  });
}
