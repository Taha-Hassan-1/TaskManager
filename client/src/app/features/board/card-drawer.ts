import { CdkTrapFocus } from '@angular/cdk/a11y';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { CardDetailDto, CommentDto, TaskPriority, UpdateCardRequest } from '../../api/models';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { AuthService } from '../../core/auth/auth.service';
import { toProblem } from '../../core/errors';
import { Notifier } from '../../core/notifier';
import { Avatar } from '../../shared/avatar';
import { ConfirmDialog } from '../../shared/dialogs';
import { LabelChip } from '../../shared/label-chip';
import { findCard } from './board.model';
import { BoardStore, CommentChange } from './board.store';

const PRIORITIES: TaskPriority[] = ['Low', 'Normal', 'High', 'Urgent'];
const LABEL_COLORS = [
  { value: '#e53935', name: 'Red' }, { value: '#fb8c00', name: 'Orange' }, { value: '#fdd835', name: 'Yellow' },
  { value: '#43a047', name: 'Green' }, { value: '#1e88e5', name: 'Blue' }, { value: '#8e24aa', name: 'Purple' },
  { value: '#6d4c41', name: 'Brown' }, { value: '#546e7a', name: 'Grey' },
];

/** The card opened over the board: edit its fields, labels and assignee, and read and write comments. */
@Component({
  selector: 'app-card-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkTrapFocus, DatePipe, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, Avatar, LabelChip],
  templateUrl: './card-drawer.html',
  styleUrl: './card-drawer.scss',
  host: { '(document:keydown.escape)': 'onEscape()' },
})
export class CardDrawer {
  /** The :cardId route parameter. */
  readonly cardId = input.required<string>();

  readonly store = inject(BoardStore);
  readonly auth = inject(AuthService);
  private readonly api = inject(TeamBoardApi);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);
  private readonly dialog = inject(MatDialog);

  readonly priorities = PRIORITIES;
  readonly labelColors = LABEL_COLORS;

  readonly detail = signal<CardDetailDto | null>(null);
  readonly saving = signal(false);
  /** Someone else saved this card since it was opened. */
  readonly conflict = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly loadFailed = signal(false);

  readonly form = inject(NonNullableFormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(4000)],
    dueDate: [''],
    priority: ['Normal' as TaskPriority],
    assigneeId: [null as number | null],
    labelIds: [[] as number[]],
  });

  // comments
  newComment = signal('');
  readonly editingCommentId = signal<number | null>(null);
  editText = '';

  // new label
  readonly addingLabel = signal(false);
  newLabelName = '';
  newLabelColor = LABEL_COLORS[4].value;

  readonly comments = computed(() => this.detail()?.comments ?? []);

  /** This card as the board screen currently holds it: it changes when someone else changes the card. */
  private readonly liveCard = computed(() => {
    const board = this.store.board();
    return board ? findCard(board, Number(this.cardId())) : undefined;
  });
  private deletedByMe = false;

  constructor() {
    effect(() => {
      const id = Number(this.cardId());
      untracked(() => void this.load(id));
    });

    // Someone else changed, moved or deleted this card while it is open.
    effect(() => {
      const live = this.liveCard();
      const detail = untracked(this.detail);
      if (detail) untracked(() => this.cardChangedUnderneath(live, detail));
    });

    // Someone else commented on it.
    effect(() => {
      const change = this.store.commentChange();
      if (change) untracked(() => this.applyCommentChange(change));
    });

    // My role changed (say, from Editor to Viewer) while the card is open.
    effect(() => {
      const canEdit = this.store.canEdit();
      untracked(() => (canEdit ? this.form.enable({ emitEvent: false }) : this.form.disable({ emitEvent: false })));
    });
  }

  private cardChangedUnderneath(live: CardDetailDto['card'] | undefined, detail: CardDetailDto): void {
    if (!live) {
      if (!this.deletedByMe) {
        this.notifier.info('This card was deleted by someone else.');
        this.close();
      }
      return;
    }
    if (live.rowVersion === detail.card.rowVersion) return; // nothing new (my own save lands here)

    // Not typing anything: just show the new version. Typing something: let the user choose (the existing banner).
    if (this.form.pristine) void this.refreshFromServer();
    else this.conflict.set(true);
  }

  private async refreshFromServer(): Promise<void> {
    try {
      const latest = await this.api.getCard(Number(this.cardId()));
      this.detail.set(latest);
      this.resetForm(latest);
    } catch {
      // Gone, or no access any more: the board screen deals with that.
    }
  }

  private applyCommentChange(change: CommentChange): void {
    const detail = this.detail();
    if (!detail || change.taskId !== detail.card.id) return;
    this.detail.update((d) => {
      if (!d) return d;
      if (change.comment === null) return { ...d, comments: d.comments.filter((c) => c.id !== change.commentId) };
      const known = d.comments.some((c) => c.id === change.commentId);
      const comment = change.comment;
      return { ...d, comments: known ? d.comments.map((c) => (c.id === change.commentId ? comment : c)) : [...d.comments, comment] };
    });
  }

  async load(id: number): Promise<void> {
    this.detail.set(null);
    this.loadFailed.set(false);
    this.conflict.set(false);
    this.saveError.set(null);
    try {
      const detail = await this.api.getCard(id);
      this.detail.set(detail);
      this.resetForm(detail);
    } catch (e) {
      this.loadFailed.set(true);
      this.notifier.error(toProblem(e).message);
      this.close();
    }
  }

  private resetForm(detail: CardDetailDto): void {
    const card = detail.card;
    this.form.reset({
      title: card.title,
      description: card.description ?? '',
      dueDate: card.dueDate?.slice(0, 10) ?? '',
      priority: card.priority,
      assigneeId: card.assigneeId,
      labelIds: [...card.labelIds],
    });
    if (this.store.canEdit()) this.form.enable();
    else this.form.disable();
  }

  async save(): Promise<void> {
    const detail = this.detail();
    if (!detail || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const body: UpdateCardRequest = {
      title: v.title.trim(),
      description: v.description.trim() || null,
      dueDate: v.dueDate ? `${v.dueDate}T00:00:00Z` : null,
      priority: v.priority,
      assigneeId: v.assigneeId,
      labelIds: v.labelIds,
      rowVersion: detail.card.rowVersion,
    };

    this.saving.set(true);
    this.saveError.set(null);
    try {
      const card = await this.api.updateCard(detail.card.id, body);
      this.store.upsertCard(card);
      this.detail.set({ ...detail, card });
      this.form.markAsPristine();
      this.conflict.set(false);
      this.notifier.info('Saved');
    } catch (e) {
      const problem = toProblem(e);
      if (problem.status === 409) this.conflict.set(true);
      else this.saveError.set(problem.message);
    } finally {
      this.saving.set(false);
    }
  }

  /** Someone else changed the card: drop my edits and show theirs. */
  async reloadAfterConflict(): Promise<void> {
    await this.load(Number(this.cardId()));
    this.store.upsertCard(this.detail()!.card);
  }

  /** Someone else changed the card: keep my edits, but build them on the latest version (press Save to apply). */
  async keepMyEdits(): Promise<void> {
    try {
      const latest = await this.api.getCard(Number(this.cardId()));
      this.detail.set({ ...latest, comments: latest.comments });
      this.store.upsertCard(latest.card);
      this.conflict.set(false);
    } catch (e) {
      this.saveError.set(toProblem(e).message);
    }
  }

  deleteCard(): void {
    this.dialog
      .open(ConfirmDialog, { data: { title: 'Delete this card?', message: 'The card and its comments are deleted for everyone.', confirmText: 'Delete card' } })
      .afterClosed()
      .subscribe(async (confirmed) => {
        if (!confirmed) return;
        this.deletedByMe = true; // so the "deleted by someone else" notice does not fire for my own delete
        if (await this.store.deleteCard(Number(this.cardId()))) this.close();
        else this.deletedByMe = false;
      });
  }

  /** Escape closes the card, unless a dialog or menu is open on top of it (Escape closes that first). */
  onEscape(): void {
    if (!document.querySelector('.mat-mdc-dialog-container, .mat-mdc-menu-panel, .mat-mdc-select-panel')) this.close();
  }

  close(): void {
    void this.router.navigate(['/boards', this.store.boardId()]);
  }

  // ---- labels ----

  toggleAddLabel(): void {
    this.addingLabel.update((v) => !v);
  }

  async createLabel(): Promise<void> {
    const name = this.newLabelName.trim();
    if (!name) return;
    const label = await this.store.addLabel(name, this.newLabelColor);
    if (!label) return;
    const selected = this.form.controls.labelIds;
    selected.setValue([...selected.value, label.id]);
    selected.markAsDirty();
    this.newLabelName = '';
    this.addingLabel.set(false);
  }

  // ---- comments ----

  canEditComment(c: CommentDto): boolean {
    return c.authorId === this.auth.user()?.id;
  }

  canDeleteComment(c: CommentDto): boolean {
    return this.canEditComment(c) || this.store.isOwner();
  }

  async addComment(): Promise<void> {
    const body = this.newComment().trim();
    const detail = this.detail();
    if (!body || !detail) return;
    try {
      const comment = await this.api.addComment(detail.card.id, body);
      this.detail.update((d) => d && { ...d, comments: [...d.comments, comment] });
      this.newComment.set('');
      this.store.upsertCard({ ...this.currentCard(), commentCount: this.comments().length });
    } catch (e) {
      this.notifier.error(toProblem(e).message);
    }
  }

  startEdit(c: CommentDto): void {
    this.editingCommentId.set(c.id);
    this.editText = c.body;
  }

  async saveEdit(c: CommentDto): Promise<void> {
    const body = this.editText.trim();
    if (!body) return;
    try {
      const updated = await this.api.updateComment(c.id, body);
      this.detail.update((d) => d && { ...d, comments: d.comments.map((x) => (x.id === c.id ? updated : x)) });
      this.editingCommentId.set(null);
    } catch (e) {
      this.notifier.error(toProblem(e).message);
    }
  }

  deleteComment(c: CommentDto): void {
    this.dialog
      .open(ConfirmDialog, { data: { title: 'Delete this comment?', message: 'The comment is deleted for everyone. This cannot be undone.', confirmText: 'Delete comment' } })
      .afterClosed()
      .subscribe(async (confirmed) => {
        if (!confirmed) return;
        try {
          await this.api.deleteComment(c.id);
          this.detail.update((d) => d && { ...d, comments: d.comments.filter((x) => x.id !== c.id) });
          this.store.upsertCard({ ...this.currentCard(), commentCount: this.comments().length });
        } catch (e) {
          this.notifier.error(toProblem(e).message);
        }
      });
  }

  private currentCard() {
    return this.detail()!.card;
  }
}
