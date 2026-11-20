import { card, board } from '../../testing/fixtures';
import {
  byPosition, findCard, matchesFilters, NO_FILTERS, placeAmong, positionBetween, withCard, withoutCard, withoutLabel,
} from './board.model';

describe('positionBetween (mirrors the server)', () => {
  it('starts an empty list at one step', () => expect(positionBetween(null, null)).toBe(1024));
  it('appends one step after the last', () => expect(positionBetween(2048, null)).toBe(3072));
  it('prepends one step before the first', () => expect(positionBetween(null, 1024)).toBe(0));
  it('takes the midpoint', () => expect(positionBetween(1024, 2048)).toBe(1536));
  it('asks for renumbering when the gap is used up', () => expect(positionBetween(1024, 1024 + 1e-7)).toBeNull());
});

describe('placeAmong', () => {
  const siblings = [{ id: 1, position: 1024 }, { id: 2, position: 2048 }, { id: 3, position: 3072 }];

  it('places before a card', () => expect(placeAmong(siblings, 2)).toEqual({ valid: true, position: 1536 }));
  it('places before the first card', () => expect(placeAmong(siblings, 1)).toEqual({ valid: true, position: 0 }));
  it('places after a card', () => expect(placeAmong(siblings, undefined, 2)).toEqual({ valid: true, position: 2560 }));
  it('places after the last card', () => expect(placeAmong(siblings, undefined, 3)).toEqual({ valid: true, position: 4096 }));
  it('goes to the end without a hint', () => expect(placeAmong(siblings).position).toBe(4096));
  it('goes into an empty column', () => expect(placeAmong([]).position).toBe(1024));
  it('rejects a card that is not in the list', () => expect(placeAmong(siblings, 99).valid).toBe(false));
});

describe('ordering', () => {
  it('breaks ties by id, like the server', () => {
    const sorted = [card(5, 1, 1024), card(2, 1, 1024), card(9, 1, 512)].sort(byPosition);
    expect(sorted.map((c) => c.id)).toEqual([9, 2, 5]);
  });
});

describe('board updates', () => {
  const start = board([{ id: 10, cards: [card(1, 10, 1024), card(2, 10, 2048)] }, { id: 20, cards: [] }]);

  it('withCard moves a card to the column it names, in order, without touching the original', () => {
    const next = withCard(start, card(1, 20, 500));
    expect(next.columns[0].cards.map((c) => c.id)).toEqual([2]);
    expect(next.columns[1].cards.map((c) => c.id)).toEqual([1]);
    expect(start.columns[0].cards.map((c) => c.id)).toEqual([1, 2]);
  });

  it('withCard replaces a card in place and re-sorts', () => {
    const next = withCard(start, card(1, 10, 3000, { title: 'Renamed' }));
    expect(next.columns[0].cards.map((c) => c.title)).toEqual(['Card 2', 'Renamed']);
  });

  it('withoutCard removes it, and findCard no longer finds it', () => {
    expect(findCard(withoutCard(start, 1), 1)).toBeUndefined();
    expect(findCard(start, 1)).toBeDefined();
  });

  it('withoutLabel strips the label from every card', () => {
    const labelled = board([{ id: 10, cards: [card(1, 10, 1, { labelIds: [7, 8] })] }], { labels: [{ id: 7, name: 'a', color: '#000000' }, { id: 8, name: 'b', color: '#000000' }] });
    const next = withoutLabel(labelled, 7);
    expect(next.labels.map((l) => l.id)).toEqual([8]);
    expect(next.columns[0].cards[0].labelIds).toEqual([8]);
  });
});

describe('matchesFilters', () => {
  const c = card(1, 10, 1, { title: 'Fix the Login bug', description: 'Happens on Safari', assigneeId: 5, labelIds: [3] });

  it('matches everything with no filters', () => expect(matchesFilters(c, NO_FILTERS)).toBe(true));
  it('searches title and description, ignoring case', () => {
    expect(matchesFilters(c, { ...NO_FILTERS, text: 'login' })).toBe(true);
    expect(matchesFilters(c, { ...NO_FILTERS, text: 'SAFARI' })).toBe(true);
    expect(matchesFilters(c, { ...NO_FILTERS, text: 'payment' })).toBe(false);
  });
  it('filters by assignee, including unassigned', () => {
    expect(matchesFilters(c, { ...NO_FILTERS, assigneeId: 5 })).toBe(true);
    expect(matchesFilters(c, { ...NO_FILTERS, assigneeId: 6 })).toBe(false);
    expect(matchesFilters(c, { ...NO_FILTERS, assigneeId: 'none' })).toBe(false);
    expect(matchesFilters({ ...c, assigneeId: null }, { ...NO_FILTERS, assigneeId: 'none' })).toBe(true);
  });
  it('filters by label', () => {
    expect(matchesFilters(c, { ...NO_FILTERS, labelId: 3 })).toBe(true);
    expect(matchesFilters(c, { ...NO_FILTERS, labelId: 4 })).toBe(false);
  });
  it('combines filters with AND', () => expect(matchesFilters(c, { text: 'login', assigneeId: 5, labelId: 4 })).toBe(false));
});
