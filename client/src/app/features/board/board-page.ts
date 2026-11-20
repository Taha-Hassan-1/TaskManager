import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, Injector, effect, inject, input, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink, RouterOutlet } from '@angular/router';
import { CardDto } from '../../api/models';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { toProblem } from '../../core/errors';
import { Notifier } from '../../core/notifier';
import { ConfirmDialog, NameDialog, NameDialogData, NameDialogResult } from '../../shared/dialogs';
import { AddCard } from './add-card';
import { BoardStore, ColumnView } from './board.store';
import { CardTile } from './card-tile';
import { MembersDialog } from './members-dialog';

@Component({
  selector: 'app-board-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CdkDrag, CdkDropList, CdkDropListGroup, RouterLink, RouterOutlet,
    MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatSelectModule, MatTooltipModule,
    AddCard, CardTile,
  ],
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPage {
  /** The :id route parameter. */
  readonly id = input.required<string>();

  readonly store = inject(BoardStore);
  private readonly injector = inject(Injector);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(TeamBoardApi);
  private readonly notifier = inject(Notifier);

  constructor() {
    effect(() => {
      const id = Number(this.id());
      untracked(() => {
        this.store.load(id).catch((e) => {
          this.notifier.error(toProblem(e).message);
          void this.router.navigate(['/boards']);
        });
      });
    });
  }

  // ---- drag and drop ----

  onDrop(event: CdkDragDrop<ColumnView, ColumnView, CardDto>): void {
    // currentIndex counts the visible cards of the target column, without the dragged one.
    void this.store.moveCard(event.item.data.id, event.container.data.id, event.currentIndex);
  }

  // ---- the "…" menu on a card: the keyboard-friendly alternative to dragging ----

  moveToColumn(card: CardDto, columnId: number): void {
    void this.store.moveCard(card.id, columnId, Number.MAX_SAFE_INTEGER); // bottom of that column
  }

  moveToTop(card: CardDto): void {
    void this.store.moveCard(card.id, card.columnId, 0);
  }

  openCard(cardId: number): void {
    void this.router.navigate(['cards', cardId], { relativeTo: this.route });
  }

  addCard(columnId: number, title: string): void {
    void this.store.addCard(columnId, title);
  }

  // ---- columns, board, members ----

  addColumn(): void {
    this.askName({ title: 'Add a column', label: 'Column name', name: '', wipLimit: null, confirmText: 'Add' }, async (r) => {
      await this.store.addColumn({ name: r.name, wipLimit: r.wipLimit });
    });
  }

  editColumn(column: ColumnView): void {
    this.askName({ title: 'Edit column', label: 'Column name', name: column.name, wipLimit: column.wipLimit }, async (r) => {
      await this.store.updateColumn(column.id, { name: r.name, wipLimit: r.wipLimit });
    });
  }

  renameBoard(): void {
    this.askName({ title: 'Rename board', label: 'Board name', name: this.store.board()?.name ?? '' }, async (r) => {
      try {
        const updated = await this.api.renameBoard(this.store.boardId(), r.name);
        this.store.board.update((b) => (b ? { ...b, name: updated.name } : b));
      } catch (e) {
        this.notifier.error(toProblem(e).message);
      }
    });
  }

  deleteBoard(): void {
    this.dialog
      .open(ConfirmDialog, {
        data: {
          title: 'Delete this board?',
          message: 'The board, all its cards and comments are deleted for everyone. This cannot be undone.',
          confirmText: 'Delete board',
        },
      })
      .afterClosed()
      .subscribe(async (confirmed) => {
        if (!confirmed) return;
        try {
          await this.api.deleteBoard(this.store.boardId());
          await this.router.navigate(['/boards']);
        } catch (e) {
          this.notifier.error(toProblem(e).message);
        }
      });
  }

  openMembers(): void {
    this.dialog.open(MembersDialog, { injector: this.injector, width: '560px', maxWidth: '95vw' });
  }

  private askName(data: NameDialogData, onSubmit: (result: NameDialogResult) => Promise<void>): void {
    this.dialog
      .open<NameDialog, NameDialogData, NameDialogResult>(NameDialog, { data })
      .afterClosed()
      .subscribe((result) => result && void onSubmit(result));
  }
}
