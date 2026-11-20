import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { BoardCache } from '../../core/board-cache';
import { Notifier } from '../../core/notifier';
import { BoardStore } from './board.store';
import { board, card } from '../../testing/fixtures';

/** A promise the test settles by hand, so it can look at the screen in between. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => ((resolve = res), (reject = rej)));
  return { promise, resolve, reject };
}

describe('BoardStore', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let notifier: { info: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let store: BoardStore;

  // To do: cards 1, 2, 3 · Doing: card 4 · Done: empty
  const initial = () =>
    board(
      [
        { id: 10, cards: [card(1, 10, 1024), card(2, 10, 2048), card(3, 10, 3072)] },
        { id: 20, cards: [card(4, 20, 1024)] },
        { id: 30, cards: [] },
      ],
      { members: [{ userId: 1, username: 'ann', email: 'a@x.com', role: 'Owner' }] },
    );

  beforeEach(() => {
    api = {
      getBoard: vi.fn().mockResolvedValue(initial()),
      moveCard: vi.fn(),
      getCard: vi.fn(),
      createCard: vi.fn(),
      deleteCard: vi.fn().mockResolvedValue(undefined),
    };
    notifier = { info: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [BoardStore, { provide: TeamBoardApi, useValue: api }, { provide: Notifier, useValue: notifier }],
    });
    store = TestBed.inject(BoardStore);
    store.board.set(initial());
  });

  const cardIn = (id: number) => store.board()!.columns.flatMap((c) => c.cards).find((c) => c.id === id)!;
  const titlesIn = (columnId: number) => store.columns().find((c) => c.id === columnId)!.cards.map((c) => c.id);

  describe('loading', () => {
    it('uses the board the route guard already fetched, without a second request', async () => {
      const prefetched = board([{ id: 10, cards: [] }], { name: 'From the guard' });
      TestBed.inject(BoardCache)['prefetched'].set(1, prefetched);

      await store.load(1);

      expect(store.board()!.name).toBe('From the guard');
      expect(api['getBoard']).not.toHaveBeenCalled();
    });

    it('fetches the board when nothing was prefetched', async () => {
      await store.load(1);
      expect(api['getBoard']).toHaveBeenCalledWith(1);
      expect(store.columns()).toHaveLength(3);
    });
  });

  describe('columns', () => {
    it('shows cards in order, with counts that ignore the filters', () => {
      store.setFilters({ text: 'Card 2' });
      const todo = store.columns()[0];
      expect(todo.cards.map((c) => c.id)).toEqual([2]);
      expect(todo.total).toBe(3);
    });

    it('flags a column over its soft WIP limit without refusing anything', () => {
      store.board.set(board([{ id: 10, cards: [card(1, 10, 1), card(2, 10, 2), card(3, 10, 3)], wipLimit: 2 }, { id: 20, cards: [], wipLimit: 2 }]));
      expect(store.columns().map((c) => c.overLimit)).toEqual([true, false]);
    });

    it('reports what the role allows', () => {
      store.board.set({ ...initial(), myRole: 'Viewer' });
      expect([store.canEdit(), store.isOwner()]).toEqual([false, false]);
      store.board.set({ ...initial(), myRole: 'Editor' });
      expect([store.canEdit(), store.isOwner()]).toEqual([true, false]);
    });
  });

  describe('moveCard', () => {
    it('updates the screen at once, before the server has answered', async () => {
      const reply = deferred<{ id: number; columnId: number; position: number; rowVersion: string }>();
      api['moveCard'].mockReturnValue(reply.promise);

      const moving = store.moveCard(1, 20, 0); // to the top of Doing

      expect(titlesIn(20)).toEqual([1, 4]); // already there, optimistic
      expect(titlesIn(10)).toEqual([2, 3]);

      reply.resolve({ id: 1, columnId: 20, position: 0, rowVersion: 'v1-new' });
      await moving;
      expect(cardIn(1).rowVersion).toBe('v1-new'); // the server's answer replaces the guess
    });

    it('asks to be placed before the card now at that index, sending the card\'s version', async () => {
      api['moveCard'].mockResolvedValue({ id: 3, columnId: 10, position: 1536, rowVersion: 'n' });

      await store.moveCard(3, 10, 1); // between card 1 and card 2

      expect(api['moveCard']).toHaveBeenCalledWith(3, { columnId: 10, rowVersion: 'v3', beforeCardId: 2 });
      expect(titlesIn(10)).toEqual([1, 3, 2]);
    });

    it('asks to be placed after the last card when dropped at the bottom', async () => {
      api['moveCard'].mockResolvedValue({ id: 1, columnId: 20, position: 2048, rowVersion: 'n' });

      await store.moveCard(1, 20, 5); // past the end

      expect(api['moveCard']).toHaveBeenCalledWith(1, { columnId: 20, rowVersion: 'v1', afterCardId: 4 });
    });

    it('sends no hint into an empty column', async () => {
      api['moveCard'].mockResolvedValue({ id: 1, columnId: 30, position: 1024, rowVersion: 'n' });

      await store.moveCard(1, 30, 0);

      expect(api['moveCard']).toHaveBeenCalledWith(1, { columnId: 30, rowVersion: 'v1' });
    });

    it('does nothing when the card is dropped where it already was', async () => {
      await store.moveCard(2, 10, 1);
      expect(api['moveCard']).not.toHaveBeenCalled();
    });

    it('counts only visible cards when the board is filtered', async () => {
      api['moveCard'].mockResolvedValue({ id: 4, columnId: 10, position: 2560, rowVersion: 'n' });
      store.board.set(board([{ id: 10, cards: [card(1, 10, 1024, { title: 'keep' }), card(2, 10, 2048, { title: 'hidden' }), card(3, 10, 3072, { title: 'keep too' })] }, { id: 20, cards: [card(4, 20, 1024)] }]));
      store.setFilters({ text: 'keep' }); // visible in To do: cards 1 and 3

      await store.moveCard(4, 10, 1); // between the two visible cards, i.e. before card 3

      expect(api['moveCard']).toHaveBeenCalledWith(4, { columnId: 10, rowVersion: 'v4', beforeCardId: 3 });
    });

    it('snaps the card back and says why when the server refuses', async () => {
      api['moveCard'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { title: 'You do not have permission to do that on this board.' } }));

      await store.moveCard(1, 20, 0);

      expect(titlesIn(10)).toEqual([1, 2, 3]);
      expect(titlesIn(20)).toEqual([4]);
      expect(cardIn(1).rowVersion).toBe('v1');
      expect(notifier.error).toHaveBeenCalledWith('You do not have permission to do that on this board.');
    });

    it('on a 409 puts the card back, explains, and fetches the current version so a retry can work', async () => {
      api['moveCard'].mockRejectedValue(new HttpErrorResponse({ status: 409 }));
      api['getCard'].mockResolvedValue({ card: card(1, 30, 1024, { rowVersion: 'someone-elses' }), comments: [] });

      await store.moveCard(1, 20, 0);

      expect(notifier.error).toHaveBeenCalledWith(expect.stringContaining('Someone else changed that card'));
      expect(api['getCard']).toHaveBeenCalledWith(1);
      expect(cardIn(1).rowVersion).toBe('someone-elses'); // their version and their column are now what is shown
      expect(titlesIn(30)).toEqual([1]);
    });

    it('reloads the board when the server had to renumber the column', async () => {
      // The neighbours are 1e-7 apart: no midpoint exists, so the server renumbers the column.
      store.board.set(board([{ id: 10, cards: [card(1, 10, 1024), card(2, 10, 1024 + 1e-7)] }, { id: 20, cards: [card(4, 20, 1024)] }]));
      api['moveCard'].mockResolvedValue({ id: 4, columnId: 10, position: 1536, rowVersion: 'n' });
      api['getBoard'].mockResolvedValue(
        board([{ id: 10, cards: [card(1, 10, 1024), card(4, 10, 1536), card(2, 10, 2048)] }, { id: 20, cards: [] }]),
      );

      await store.moveCard(4, 10, 1);

      expect(api['getBoard']).toHaveBeenCalled(); // the fresh positions were fetched
      expect(titlesIn(10)).toEqual([1, 4, 2]);
    });
  });

  describe('cards', () => {
    it('adds a created card to its column', async () => {
      api['createCard'].mockResolvedValue(card(9, 30, 1024, { title: 'New' }));

      expect(await store.addCard(30, 'New')).toBe(true);

      expect(api['createCard']).toHaveBeenCalledWith(1, { title: 'New', columnId: 30 });
      expect(titlesIn(30)).toEqual([9]);
    });

    it('reports a failed create and leaves the board alone', async () => {
      api['createCard'].mockRejectedValue(new HttpErrorResponse({ status: 400, error: { title: 'Bad', errors: { Title: ['A card needs a title.'] } } }));

      expect(await store.addCard(30, '')).toBe(false);

      expect(notifier.error).toHaveBeenCalledWith('A card needs a title.');
      expect(titlesIn(30)).toEqual([]);
    });

    it('removes a deleted card', async () => {
      expect(await store.deleteCard(2)).toBe(true);
      expect(titlesIn(10)).toEqual([1, 3]);
    });
  });
});
