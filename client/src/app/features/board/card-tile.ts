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
    <div class="tile" [class]="'priority-' + card().priority.toLowerCase()">
      @if (labels().length) {
        <div class="labels">
          @for (label of labels(); track label.id) { <app-label-chip [label]="label" /> }
        </div>
      }
      <!-- The title is the one real button; its ::after stretches over the whole tile, so the tile is clickable
           without a button nested inside a button (which screen readers cannot reach). -->
      <button type="button" class="open" [attr.aria-label]="'Open card: ' + card().title" (click)="open.emit()">
        <span class="title">{{ card().title }}</span>
      </button>
      <div class="meta">
        @if (card().priority === 'High' || card().priority === 'Urgent') {
          <span class="badge priority" [title]="card().priority + ' priority'"><mat-icon>priority_high</mat-icon>{{ card().priority }}</span>
        }
        @if (card().dueDate) {
          <span class="badge" [class.overdue]="overdue()" [attr.title]="overdue() ? 'Overdue' : 'Due date'">
            <mat-icon aria-hidden="true">{{ overdue() ? 'event_busy' : 'event' }}</mat-icon>
            @if (overdue()) { <span class="visually-hidden">Overdue: </span> }{{ card().dueDate!.slice(0, 10) }}
          </span>
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
      position: relative; background: var(--tb-card-bg); border-radius: 10px; padding: 12px 14px;
      border: 1px solid var(--tb-hairline); border-left: 4px solid transparent;
      box-shadow: var(--tb-shadow-card); cursor: pointer;
      transition: box-shadow 150ms ease, transform 150ms ease;
      -webkit-tap-highlight-color: transparent;
      &:has(.open:focus-visible) { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
      // Hover feedback only where a pointer can really hover: on touch it would stay "stuck" after a tap.
      @media (hover: hover) { &:hover { box-shadow: var(--tb-shadow-card-hover); } }
      @media (hover: hover) and (prefers-reduced-motion: no-preference) { &:hover { transform: translateY(-1px); } }
      @media (prefers-reduced-motion: reduce) { transition: none; }
    }
    .priority-urgent { border-left-color: var(--tb-urgent); }
    .priority-high { border-left-color: var(--tb-high); }
    .priority-low { border-left-color: var(--tb-low); }
    .labels { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 6px; }
    .open {
      display: block; width: 100%; padding: 0 28px 0 0; margin: 0; border: 0; background: none; color: inherit;
      font: inherit; text-align: start; cursor: pointer; outline: none;
      &::after { content: ''; position: absolute; inset: 0; border-radius: inherit; }
    }
    .title { display: block; font-size: 14px; line-height: 20px; font-weight: 500; overflow-wrap: anywhere; text-wrap: pretty; }
    .meta { display: flex; align-items: center; gap: 8px; margin-top: 8px; min-height: 24px; }
    .spacer { flex: 1; }
    .badge {
      display: inline-flex; align-items: center; gap: 2px; font-size: 12px; font-variant-numeric: tabular-nums; color: var(--mat-sys-on-surface-variant);
      mat-icon { font-size: 16px; width: 16px; height: 16px; }
    }
    .badge.overdue { color: var(--mat-sys-error); font-weight: 500; }
    .badge.priority { color: var(--tb-warn-fg); font-weight: 500; }
    .visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
    // Above the tile-wide button, so the menu stays clickable.
    .menu { position: absolute; top: 2px; right: 0; z-index: 1; opacity: 0.7; &:hover, &:focus-visible { opacity: 1; } }
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
