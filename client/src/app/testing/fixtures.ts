import { BoardDetailDto, CardDto } from '../api/models';

export function card(id: number, columnId: number, position: number, extra: Partial<CardDto> = {}): CardDto {
  return {
    id, boardId: 1, columnId, title: `Card ${id}`, description: null, position, priority: 'Normal', dueDate: null,
    assigneeId: null, createdById: 1, createdAt: '2026-01-01T00:00:00Z', labelIds: [], commentCount: 0, rowVersion: `v${id}`,
    ...extra,
  };
}

export function board(columns: { id: number; cards: CardDto[]; wipLimit?: number | null }[], extra: Partial<BoardDetailDto> = {}): BoardDetailDto {
  return {
    id: 1, name: 'Board', ownerId: 1, myRole: 'Owner', members: [], labels: [],
    columns: columns.map((c, i) => ({ id: c.id, name: `Column ${c.id}`, position: (i + 1) * 1024, wipLimit: c.wipLimit ?? null, cards: c.cards })),
    ...extra,
  };
}
