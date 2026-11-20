import { Injectable, computed, inject, signal } from '@angular/core';
import { BoardDetailDto, CardDto, ColumnRequest, LabelDto } from '../../api/models';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { BoardCache } from '../../core/board-cache';
import { toProblem } from '../../core/errors';
import { Notifier } from '../../core/notifier';
import {
  CardFilters, NO_FILTERS, byPosition, findCard, hasFilters, matchesFilters, placeAmong,
  withCard, withColumn, withLabel, withoutCard, withoutLabel,
} from './board.model';

/** Where a dropped card goes, relative to a neighbour. Neither field: the end of the column. */
interface DropHint {
  beforeCardId?: number;
  afterCardId?: number;
}

export interface ColumnView {
  id: number;
  name: string;
  wipLimit: number | null;
  /** Cards in the column, all of them, whatever the filters say. */
  total: number;
  /** Over its soft WIP limit: a warning in the UI, never a refusal. */
  overLimit: boolean;
  /** The cards to show: filtered, in order. */
  cards: CardDto[];
}

/**
 * The single source of truth for the board screen. REST responses write into it (and, from M5, so do live
 * events); components read the computed signals and never mutate them.
 */
@Injectable()
export class BoardStore {
  private readonly api = inject(TeamBoardApi);
  private readonly notifier = inject(Notifier);
  private readonly cache = inject(BoardCache);

  readonly board = signal<BoardDetailDto | null>(null);
  readonly filters = signal<CardFilters>(NO_FILTERS);

  readonly boardId = computed(() => this.board()?.id ?? 0);
  readonly role = computed(() => this.board()?.myRole ?? 'Viewer');
  readonly canEdit = computed(() => this.role() !== 'Viewer');
  readonly isOwner = computed(() => this.role() === 'Owner');
  readonly members = computed(() => this.board()?.members ?? []);
  readonly labels = computed(() => this.board()?.labels ?? []);
  readonly hasActiveFilters = computed(() => hasFilters(this.filters()));
  readonly membersById = computed(() => new Map(this.members().map((m) => [m.userId, m])));
  readonly labelsById = computed(() => new Map(this.labels().map((l) => [l.id, l])));

  readonly columns = computed<ColumnView[]>(() => {
    const filters = this.filters();
    return (this.board()?.columns ?? []).map((column) => ({
      id: column.id,
      name: column.name,
      wipLimit: column.wipLimit,
      total: column.cards.length,
      overLimit: column.wipLimit !== null && column.cards.length > column.wipLimit,
      cards: column.cards.filter((card) => matchesFilters(card, filters)).sort(byPosition),
    }));
  });

  // ---- loading ----

  async load(id: number): Promise<void> {
    this.board.set(this.cache.take(id) ?? (await this.api.getBoard(id)));
  }

  async reload(): Promise<void> {
    const id = this.boardId();
    if (id) this.board.set(await this.api.getBoard(id));
  }

  setFilters(patch: Partial<CardFilters>): void {
    this.filters.update((f) => ({ ...f, ...patch }));
  }

  clearFilters(): void {
    this.filters.set(NO_FILTERS);
  }

  // ---- cards ----

  /** Cards are replaced wholesale by whatever the server last said about them. */
  upsertCard(card: CardDto): void {
    this.board.update((b) => (b ? withCard(b, card) : b));
  }

  async addCard(columnId: number, title: string): Promise<boolean> {
    try {
      this.upsertCard(await this.api.createCard(this.boardId(), { title, columnId }));
      return true;
    } catch (error) {
      this.notifier.error(toProblem(error).message);
      return false;
    }
  }

  async deleteCard(cardId: number): Promise<boolean> {
    try {
      await this.api.deleteCard(cardId);
      this.board.update((b) => (b ? withoutCard(b, cardId) : b));
      return true;
    } catch (error) {
      this.notifier.error(toProblem(error).message);
      return false;
    }
  }

  /**
   * Moves a card. The screen changes at once (optimistic); the server confirms or refuses afterwards.
   * On a 409 or any other failure the card snaps back and the user is told why.
   *
   * @param targetIndex where the card ends up among the *visible* cards of the target column, counted
   *   without the card itself (the index CDK reports on drop). Past the end means "at the bottom".
   */
  async moveCard(cardId: number, toColumnId: number, targetIndex: number): Promise<void> {
    const board = this.board();
    const card = board && findCard(board, cardId);
    const target = board?.columns.find((c) => c.id === toColumnId);
    if (!board || !card || !target) return;

    const filters = this.filters();
    const everyone = target.cards.filter((c) => c.id !== cardId).sort(byPosition); // the real neighbours
    const visible = everyone.filter((c) => matchesFilters(c, filters)); //             what the user dropped among

    if (card.columnId === toColumnId && this.indexAmongVisible(card, toColumnId) === targetIndex) return; // dropped where it was

    const reference = visible[targetIndex];
    const hint: DropHint = reference
      ? { beforeCardId: reference.id }
      : visible.length > 0
        ? { afterCardId: visible[visible.length - 1].id }
        : {};

    const placement = placeAmong(everyone, hint.beforeCardId, hint.afterCardId);
    if (!placement.valid) return;

    // A used-up gap has no exact position yet; the midpoint keeps the card in the right spot until the server renumbers.
    const optimisticPosition = placement.position ?? this.approximatePosition(everyone, hint);
    const previous = card;
    this.upsertCard({ ...card, columnId: toColumnId, position: optimisticPosition });

    try {
      const result = await this.api.moveCard(cardId, { columnId: toColumnId, rowVersion: card.rowVersion, ...hint });
      const current = this.board() && findCard(this.board()!, cardId);
      if (current) this.upsertCard({ ...current, columnId: result.columnId, position: result.position, rowVersion: result.rowVersion });
      if (placement.position === null) await this.reload(); // the server renumbered the column
    } catch (error) {
      this.upsertCard(previous); // snap back
      const problem = toProblem(error);
      if (problem.status === 409) {
        this.notifier.error('Someone else changed that card, so it was put back. Try the move again.');
        await this.refreshCard(cardId); // pick up the current version so a retry can succeed
      } else {
        this.notifier.error(problem.message);
      }
    }
  }

  /** Re-reads one card from the server (after a conflict). */
  async refreshCard(cardId: number): Promise<void> {
    try {
      this.upsertCard((await this.api.getCard(cardId)).card);
    } catch {
      this.board.update((b) => (b ? withoutCard(b, cardId) : b)); // gone (deleted by someone else)
    }
  }

  // ---- columns ----

  async addColumn(request: ColumnRequest): Promise<boolean> {
    try {
      const column = await this.api.createColumn(this.boardId(), request);
      this.board.update((b) => (b ? withColumn(b, column) : b));
      return true;
    } catch (error) {
      this.notifier.error(toProblem(error).message);
      return false;
    }
  }

  async updateColumn(columnId: number, request: ColumnRequest): Promise<boolean> {
    try {
      const column = await this.api.updateColumn(columnId, request);
      this.board.update((b) => (b ? withColumn(b, column) : b));
      return true;
    } catch (error) {
      this.notifier.error(toProblem(error).message);
      return false;
    }
  }

  // ---- labels ----

  async addLabel(name: string, color: string): Promise<LabelDto | null> {
    try {
      const label = await this.api.createLabel(this.boardId(), { name, color });
      this.board.update((b) => (b ? withLabel(b, label) : b));
      return label;
    } catch (error) {
      this.notifier.error(toProblem(error).message);
      return null;
    }
  }

  async removeLabel(labelId: number): Promise<void> {
    try {
      await this.api.deleteLabel(labelId);
      this.board.update((b) => (b ? withoutLabel(b, labelId) : b));
    } catch (error) {
      this.notifier.error(toProblem(error).message);
    }
  }

  // ---- helpers ----

  private indexAmongVisible(card: CardDto, columnId: number): number {
    return this.columns().find((c) => c.id === columnId)?.cards.findIndex((c) => c.id === card.id) ?? -1;
  }

  private approximatePosition(siblings: { id: number; position: number }[], hint: DropHint): number {
    const neighbour = siblings.find((s) => s.id === (hint.beforeCardId ?? hint.afterCardId));
    if (!neighbour) return (siblings.at(-1)?.position ?? 0) + 1;
    return hint.beforeCardId !== undefined ? neighbour.position - 1e-9 : neighbour.position + 1e-9;
  }
}
