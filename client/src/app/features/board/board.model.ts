import { BoardDetailDto, CardDto, ColumnDto, LabelDto, MemberDto } from '../../api/models';

/** Mirrors the server's GapPositionCalculator, so the optimistic position is the one the server will pick. */
export const POSITION_STEP = 1024;
const MIN_GAP = 1e-6;

export function positionBetween(lower: number | null, upper: number | null): number | null {
  if (lower === null && upper === null) return POSITION_STEP;
  if (lower === null) return upper! - POSITION_STEP;
  if (upper === null) return lower + POSITION_STEP;
  if (upper - lower < MIN_GAP) return null; // gap used up: the server renumbers the column
  return (lower + upper) / 2;
}

export interface Sibling {
  id: number;
  position: number;
}

export interface Placement {
  /** False when the card the drop refers to is not in the list. */
  valid: boolean;
  /** Null when the gap is used up. */
  position: number | null;
}

/** Where a card lands among `siblings` (ordered, without the card itself): before one, after one, or at the end. */
export function placeAmong(siblings: Sibling[], beforeId?: number, afterId?: number): Placement {
  let lower: number | null;
  let upper: number | null;

  if (beforeId !== undefined) {
    const i = siblings.findIndex((s) => s.id === beforeId);
    if (i < 0) return { valid: false, position: null };
    upper = siblings[i].position;
    lower = i > 0 ? siblings[i - 1].position : null;
  } else if (afterId !== undefined) {
    const i = siblings.findIndex((s) => s.id === afterId);
    if (i < 0) return { valid: false, position: null };
    lower = siblings[i].position;
    upper = i < siblings.length - 1 ? siblings[i + 1].position : null;
  } else {
    lower = siblings.length > 0 ? siblings[siblings.length - 1].position : null;
    upper = null;
  }
  return { valid: true, position: positionBetween(lower, upper) };
}

// Equal positions (two people adding to a column at once) are ordered by id, the same way the server does.
export const byPosition = (a: { position: number; id: number }, b: { position: number; id: number }) =>
  a.position - b.position || a.id - b.id;

// ---- immutable updates of the board ----

export function findCard(board: BoardDetailDto, cardId: number): CardDto | undefined {
  for (const column of board.columns) {
    const card = column.cards.find((c) => c.id === cardId);
    if (card) return card;
  }
  return undefined;
}

/** Puts the card in the column it says it belongs to, replacing any older copy wherever that was. */
export function withCard(board: BoardDetailDto, card: CardDto): BoardDetailDto {
  return {
    ...board,
    columns: board.columns.map((column) => {
      const others = column.cards.filter((c) => c.id !== card.id);
      return column.id === card.columnId ? { ...column, cards: [...others, card].sort(byPosition) } : { ...column, cards: others };
    }),
  };
}

export function withoutCard(board: BoardDetailDto, cardId: number): BoardDetailDto {
  return { ...board, columns: board.columns.map((c) => ({ ...c, cards: c.cards.filter((x) => x.id !== cardId) })) };
}

export function withColumn(board: BoardDetailDto, column: ColumnDto): BoardDetailDto {
  const exists = board.columns.some((c) => c.id === column.id);
  const columns = exists
    ? board.columns.map((c) => (c.id === column.id ? { ...column, cards: c.cards } : c))
    : [...board.columns, column];
  return { ...board, columns: columns.sort(byPosition) };
}

export function withLabel(board: BoardDetailDto, label: LabelDto): BoardDetailDto {
  const labels = board.labels.some((l) => l.id === label.id)
    ? board.labels.map((l) => (l.id === label.id ? label : l))
    : [...board.labels, label];
  return { ...board, labels };
}

export function withoutLabel(board: BoardDetailDto, labelId: number): BoardDetailDto {
  return {
    ...board,
    labels: board.labels.filter((l) => l.id !== labelId),
    columns: board.columns.map((c) => ({
      ...c,
      cards: c.cards.map((card) => (card.labelIds.includes(labelId) ? { ...card, labelIds: card.labelIds.filter((id) => id !== labelId) } : card)),
    })),
  };
}

export function withMember(board: BoardDetailDto, member: MemberDto): BoardDetailDto {
  const exists = board.members.some((m) => m.userId === member.userId);
  const members = exists ? board.members.map((m) => (m.userId === member.userId ? member : m)) : [...board.members, member];
  return { ...board, members: members.sort((a, b) => a.username.localeCompare(b.username)) };
}

/** Removes the member, and unassigns their cards, as the server does. */
export function withoutMember(board: BoardDetailDto, userId: number): BoardDetailDto {
  return {
    ...board,
    members: board.members.filter((m) => m.userId !== userId),
    columns: board.columns.map((c) => ({ ...c, cards: c.cards.map((card) => (card.assigneeId === userId ? { ...card, assigneeId: null } : card)) })),
  };
}

// ---- filters ----

export interface CardFilters {
  text: string;
  /** A member id, 'none' for unassigned cards, or null for everyone. */
  assigneeId: number | 'none' | null;
  labelId: number | null;
}

export const NO_FILTERS: CardFilters = { text: '', assigneeId: null, labelId: null };

export function hasFilters(f: CardFilters): boolean {
  return f.text.trim() !== '' || f.assigneeId !== null || f.labelId !== null;
}

export function matchesFilters(card: CardDto, f: CardFilters): boolean {
  const text = f.text.trim().toLowerCase();
  if (text && !`${card.title} ${card.description ?? ''}`.toLowerCase().includes(text)) return false;
  if (f.assigneeId === 'none' && card.assigneeId !== null) return false;
  if (typeof f.assigneeId === 'number' && card.assigneeId !== f.assigneeId) return false;
  if (f.labelId !== null && !card.labelIds.includes(f.labelId)) return false;
  return true;
}
