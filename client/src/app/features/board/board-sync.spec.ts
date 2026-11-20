import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TeamBoardApi } from '../../core/api/team-board-api';
import { AuthService } from '../../core/auth/auth.service';
import { Notifier } from '../../core/notifier';
import { HUB_LINK_FACTORY } from '../../core/realtime/hub-link';
import { ClientId } from '../../core/realtime/client-id';
import { FakeHubLink } from '../../testing/fake-hub-link';
import { board, card } from '../../testing/fixtures';
import { BoardStore } from './board.store';
import { BoardSync } from './board-sync';

describe('BoardSync', () => {
  let link: FakeHubLink;
  let links: FakeHubLink[];
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let notifier: { info: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let router: Router;
  let store: BoardStore;
  let sync: BoardSync;
  let me: string;

  const initial = () =>
    board([{ id: 10, cards: [card(1, 10, 1024), card(2, 10, 2048)] }, { id: 20, cards: [] }], {
      members: [
        { userId: 1, username: 'ann', email: 'ann@x.com', role: 'Editor' },
        { userId: 2, username: 'bob', email: 'bob@x.com', role: 'Editor' },
      ],
    });

  beforeEach(async () => {
    link = new FakeHubLink();
    links = [link];
    api = { getBoard: vi.fn().mockResolvedValue(initial()), getCard: vi.fn(), moveCard: vi.fn() };
    notifier = { info: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        BoardStore,
        BoardSync,
        { provide: HUB_LINK_FACTORY, useValue: () => links.shift() ?? link },
        { provide: TeamBoardApi, useValue: api },
        { provide: Notifier, useValue: notifier },
        { provide: AuthService, useValue: { user: () => ({ id: 1, username: 'ann' }) } },
      ],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    store = TestBed.inject(BoardStore);
    store.board.set(initial());
    sync = TestBed.inject(BoardSync);
    me = TestBed.inject(ClientId).value;
  });

  afterEach(() => vi.useRealTimers());

  const cardIn = (id: number) => store.board()!.columns.flatMap((c) => c.cards).find((c) => c.id === id);
  const ids = (columnId: number) => store.board()!.columns.find((c) => c.id === columnId)!.cards.map((c) => c.id);
  const live = (extra: object = {}) => ({ boardId: 1, originClientId: 'someone-else', ...extra });

  describe('connecting', () => {
    it('starts the connection, joins the board and reports live', async () => {
      await sync.start(1);

      expect(link.starts).toBe(1);
      expect(link.joins()).toEqual([1]);
      expect(sync.status()).toBe('live');
    });

    it('when the server is unreachable, reports offline and keeps trying with a growing delay', async () => {
      vi.useFakeTimers();
      link.failNextStart = new Error('refused');

      await sync.start(1);
      expect(sync.status()).toBe('offline');

      await vi.advanceTimersByTimeAsync(1_000);
      expect(sync.status()).toBe('live');
      expect(link.starts).toBe(2);
      expect(link.joins()).toEqual([1]);
    });

    it('a failed join does not start the transport a second time', async () => {
      vi.useFakeTimers();
      link.failNextInvoke.set('JoinBoard', new Error('hiccup'));

      await sync.start(1);
      await vi.advanceTimersByTimeAsync(1_000);

      expect(link.starts).toBe(1);
      expect(link.joins()).toEqual([1, 1]);
      expect(sync.status()).toBe('live');
    });

    it('leaves the screen when the join is refused, because the user has no access', async () => {
      link.failNextInvoke.set('JoinBoard', new Error("An unexpected error occurred invoking 'JoinBoard' on the server. HubException: Board not found."));

      await sync.start(1);

      expect(notifier.error).toHaveBeenCalledWith('You no longer have access to that board.');
      expect(router.navigate).toHaveBeenCalledWith(['/boards']);
      expect(link.stops).toBeGreaterThan(0);
    });
  });

  describe('changes made by other people', () => {
    beforeEach(() => sync.start(1));

    it('shows a card someone else created', () => {
      link.emit('TaskCreated', live({ card: card(9, 20, 1024, { title: 'From bob' }) }));
      expect(cardIn(9)?.title).toBe('From bob');
      expect(ids(20)).toEqual([9]);
    });

    it('applies an edit', () => {
      link.emit('TaskUpdated', live({ card: card(1, 10, 1024, { title: 'Renamed by bob', rowVersion: 'v-new' }) }));
      expect(cardIn(1)).toMatchObject({ title: 'Renamed by bob', rowVersion: 'v-new' });
    });

    it('moves a card without needing the rest of it', () => {
      link.emit('TaskMoved', live({ taskId: 1, fromColumnId: 10, columnId: 20, position: 1024, rowVersion: 'v-moved' }));

      expect(ids(10)).toEqual([2]);
      expect(ids(20)).toEqual([1]);
      expect(cardIn(1)).toMatchObject({ title: 'Card 1', rowVersion: 'v-moved' }); // everything else kept
    });

    it('asks the server for a moved card it has never heard of', async () => {
      api['getCard'].mockResolvedValue({ card: card(7, 20, 1024), comments: [] });

      link.emit('TaskMoved', live({ taskId: 7, fromColumnId: 10, columnId: 20, position: 1024, rowVersion: 'v' }));
      await vi.waitFor(() => expect(ids(20)).toEqual([7]));
    });

    it('removes a deleted card', () => {
      link.emit('TaskDeleted', live({ taskId: 2 }));
      expect(ids(10)).toEqual([1]);
    });

    it('applies column, label and board name changes', () => {
      link.emit('ColumnChanged', live({ column: { id: 30, name: 'Review', position: 4096, wipLimit: 2, cards: [] } }));
      link.emit('LabelChanged', live({ labelId: 5, label: { id: 5, name: 'bug', color: '#ff0000' } }));
      link.emit('BoardChanged', live({ name: 'Renamed board' }));

      expect(store.columns().map((c) => c.name)).toContain('Review');
      expect(store.labels().map((l) => l.name)).toEqual(['bug']);
      expect(store.board()!.name).toBe('Renamed board');

      link.emit('LabelChanged', live({ labelId: 5, label: null }));
      expect(store.labels()).toEqual([]);
    });

    it('keeps the comment count right and tells the open card', () => {
      const comment = { id: 3, taskItemId: 1, authorId: 2, authorName: 'bob', body: 'hi', createdAt: '2026-01-01T00:00:00Z' };

      link.emit('CommentAdded', live({ taskId: 1, comment }));
      expect(cardIn(1)!.commentCount).toBe(1);
      expect(store.commentChange()).toEqual({ taskId: 1, commentId: 3, comment });

      link.emit('CommentChanged', live({ taskId: 1, commentId: 3, comment: null }));
      expect(cardIn(1)!.commentCount).toBe(0);
      expect(store.commentChange()).toEqual({ taskId: 1, commentId: 3, comment: null });
    });

    it('lists new activity, even though entries carry no origin', () => {
      const entry = { id: 77, type: 'TaskCreated', actorId: 2, actorName: 'bob', taskItemId: 9, payload: { title: 'x' }, createdAt: '2026-01-01T00:00:00Z' };
      link.emit('ActivityAdded', { boardId: 1, entry });
      expect(store.latestActivity()).toEqual(entry);
    });
  });

  describe('this tab\'s own changes', () => {
    beforeEach(() => sync.start(1));

    it('are not applied a second time when they come back as pushes', () => {
      store.upsertCard(card(1, 20, 5000, { title: 'My local state' })); // what my own REST response put on screen

      link.emit('TaskUpdated', { boardId: 1, originClientId: me, card: card(1, 10, 1024, { title: 'Stale echo' }) });
      link.emit('TaskMoved', { boardId: 1, originClientId: me, taskId: 1, fromColumnId: 10, columnId: 10, position: 1, rowVersion: 'x' });
      link.emit('TaskDeleted', { boardId: 1, originClientId: me, taskId: 1 });

      expect(cardIn(1)).toMatchObject({ title: 'My local state', columnId: 20 });
    });

    it('another tab of the same user is not an echo, and is applied', () => {
      link.emit('TaskUpdated', live({ originClientId: 'my-other-tab', card: card(1, 10, 1024, { title: 'From my other tab' }) }));
      expect(cardIn(1)!.title).toBe('From my other tab');
    });

    it('a change with no origin at all is applied', () => {
      link.emit('TaskDeleted', { boardId: 1, originClientId: null, taskId: 1 });
      expect(cardIn(1)).toBeUndefined();
    });
  });

  describe('reconnecting', () => {
    beforeEach(() => sync.start(1));

    it('shows that the connection is down', () => {
      link.drop();
      expect(sync.status()).toBe('reconnecting');
    });

    it('rejoins the board and reloads it once, so nothing missed can leave the screen stale', async () => {
      link.drop();
      api['getBoard'].mockResolvedValue(board([{ id: 10, cards: [card(1, 10, 1024), card(2, 10, 2048), card(5, 10, 3072, { title: 'Added while offline' })] }]));

      await link.restore();

      expect(link.joins()).toEqual([1, 1]); // joined again: a new connection has no groups
      expect(api['getBoard']).toHaveBeenCalledTimes(1);
      expect(cardIn(5)?.title).toBe('Added while offline');
      expect(sync.status()).toBe('live');
    });

    it('leaves the board if access was lost while away', async () => {
      link.drop();
      link.failNextInvoke.set('JoinBoard', new Error('HubException: Board not found.'));

      await link.restore();

      expect(notifier.error).toHaveBeenCalledWith('You no longer have access to that board.');
      expect(router.navigate).toHaveBeenCalledWith(['/boards']);
    });

    it('when the library gives up, starts over', async () => {
      vi.useFakeTimers();
      link.giveUp();
      expect(sync.status()).toBe('offline');

      await vi.advanceTimersByTimeAsync(1_000);

      expect(link.starts).toBe(2);
      expect(sync.status()).toBe('live');
    });
  });

  describe('losing access, or the board', () => {
    beforeEach(() => sync.start(1));

    it('being removed sends the user back to their boards', async () => {
      link.emit('MemberChanged', live({ userId: 1, member: null }));

      await vi.waitFor(() => expect(router.navigate).toHaveBeenCalledWith(['/boards']));
      expect(notifier.error).toHaveBeenCalledWith('You were removed from this board.');
    });

    it('someone else being removed just updates the member list, and unassigns their cards', () => {
      store.upsertCard(card(1, 10, 1024, { assigneeId: 2 }));

      link.emit('MemberChanged', live({ userId: 2, member: null }));

      expect(store.members().map((m) => m.username)).toEqual(['ann']);
      expect(cardIn(1)!.assigneeId).toBeNull();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('a new member appears in the list', () => {
      link.emit('MemberChanged', live({ userId: 3, member: { userId: 3, username: 'cy', email: 'cy@x.com', role: 'Viewer' } }));
      expect(store.members().map((m) => m.username)).toEqual(['ann', 'bob', 'cy']);
    });

    it('a change to my own role updates what I am allowed to do, and says so', () => {
      link.emit('MemberChanged', live({ userId: 1, member: { userId: 1, username: 'ann', email: 'ann@x.com', role: 'Viewer' } }));

      expect(store.canEdit()).toBe(false);
      expect(notifier.info).toHaveBeenCalledWith('Your role on this board is now Viewer.');
    });

    it('the board being deleted sends everyone away', async () => {
      link.emit('BoardDeleted', live());

      await vi.waitFor(() => expect(router.navigate).toHaveBeenCalledWith(['/boards']));
      expect(notifier.error).toHaveBeenCalledWith('This board was deleted by its owner.');
    });
  });

  describe('stopping', () => {
    it('closes the connection and ignores anything that arrives after', async () => {
      await sync.start(1);
      await sync.stop();

      expect(link.stops).toBeGreaterThan(0);
      link.emit('TaskDeleted', live({ taskId: 1 }));
      expect(cardIn(1)).toBeDefined();
    });

    it('moving to another board leaves the first one and joins the second', async () => {
      await sync.start(1);
      const second = new FakeHubLink();
      links.push(second);

      await sync.start(2);

      expect(link.stops).toBeGreaterThan(0);
      expect(second.joins()).toEqual([2]);
      link.emit('TaskDeleted', live({ taskId: 1 })); // the old connection is no longer listened to
      expect(cardIn(1)).toBeDefined();
    });
  });
});
