import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Notifier } from '../../core/notifier';
import { ClientId } from '../../core/realtime/client-id';
import { HUB_LINK_FACTORY, HubLink } from '../../core/realtime/hub-link';
import {
  ActivityAddedMessage, BoardChangedMessage, BoardDeletedMessage, ColumnChangedMessage, CommentAddedMessage,
  CommentChangedMessage, LabelChangedMessage, MemberChangedMessage, TaskCreatedMessage, TaskDeletedMessage,
  TaskMovedMessage, TaskUpdatedMessage,
} from '../../core/realtime/messages';
import { BoardStore } from './board.store';

export type SyncStatus = 'connecting' | 'live' | 'reconnecting' | 'offline';

const RETRY_DELAYS_MS = [1_000, 2_000, 5_000, 10_000, 30_000];

/**
 * Keeps one board screen in step with everyone else. It joins the board's group on the hub and writes what
 * the server pushes into the same store the REST responses write into, so the screen has one source of truth.
 *
 * - Changes made by this tab come back as pushes too; they are recognised by their originClientId and skipped.
 * - After a reconnect the group membership is gone and pushes may have been missed, so it rejoins and reloads
 *   the board once. That is what guarantees the screen cannot stay stale.
 */
@Injectable()
export class BoardSync {
  private readonly createLink = inject(HUB_LINK_FACTORY);
  private readonly store = inject(BoardStore);
  private readonly auth = inject(AuthService);
  private readonly clientId = inject(ClientId);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);

  readonly status = signal<SyncStatus>('connecting');

  private link: HubLink | null = null;
  private boardId = 0;
  private started = false; // the transport is up (joining may still be pending)
  private retryTimer: ReturnType<typeof setTimeout> | undefined;

  async start(boardId: number): Promise<void> {
    await this.stop();
    this.boardId = boardId;
    this.status.set('connecting');
    const link = this.createLink();
    this.link = link;
    this.listen(link);
    await this.connect(link, 0);
  }

  async stop(): Promise<void> {
    clearTimeout(this.retryTimer);
    const link = this.link;
    this.link = null;
    this.started = false;
    if (link) {
      try {
        await link.stop();
      } catch {
        // Already down.
      }
    }
  }

  /** Brings the connection up and joins the board, retrying with a growing delay for as long as it takes. */
  private async connect(link: HubLink, attempt: number): Promise<void> {
    if (this.link !== link) return; // a newer start() or stop() superseded this one
    try {
      if (!this.started) {
        await link.start();
        this.started = true;
      }
      await link.invoke('JoinBoard', this.boardId);
      if (this.link === link) this.status.set('live');
    } catch (error) {
      if (this.link !== link) return;
      if (isBoardNotFound(error)) {
        void this.lostAccess('You no longer have access to that board.');
        return;
      }
      this.status.set('offline');
      const delay = RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)];
      this.retryTimer = setTimeout(() => void this.connect(link, attempt + 1), delay);
    }
  }

  private listen(link: HubLink): void {
    link.onReconnecting(() => this.link === link && this.status.set('reconnecting'));

    link.onReconnected(async () => {
      if (this.link !== link) return;
      try {
        await link.invoke('JoinBoard', this.boardId); // a new connection has no groups yet
        await this.store.reload(); // and may have missed pushes while it was away
        if (this.link === link) this.status.set('live');
      } catch (error) {
        if (isBoardNotFound(error)) void this.lostAccess('You no longer have access to that board.');
        else if (this.link === link) {
          this.status.set('offline');
          void this.connect(link, 0);
        }
      }
    });

    link.onClose(() => {
      if (this.link !== link) return;
      this.started = false;
      this.status.set('offline');
      this.retryTimer = setTimeout(() => void this.connect(link, 0), RETRY_DELAYS_MS[0]);
    });

    this.on<TaskCreatedMessage>(link, 'TaskCreated', (m) => this.store.applyRemoteCard(m.card));
    this.on<TaskUpdatedMessage>(link, 'TaskUpdated', (m) => this.store.applyRemoteCard(m.card));
    this.on<TaskMovedMessage>(link, 'TaskMoved', (m) => this.store.applyRemoteMove(m));
    this.on<TaskDeletedMessage>(link, 'TaskDeleted', (m) => this.store.applyRemoteDelete(m.taskId));
    this.on<ColumnChangedMessage>(link, 'ColumnChanged', (m) => this.store.applyRemoteColumn(m.column));
    this.on<LabelChangedMessage>(link, 'LabelChanged', (m) => this.store.applyRemoteLabel(m.labelId, m.label));
    this.on<BoardChangedMessage>(link, 'BoardChanged', (m) => this.store.applyRemoteBoardName(m.name));
    this.on<CommentAddedMessage>(link, 'CommentAdded', (m) =>
      this.store.applyRemoteComment({ taskId: m.taskId, commentId: m.comment.id, comment: m.comment }, 'added'),
    );
    this.on<CommentChangedMessage>(link, 'CommentChanged', (m) =>
      this.store.applyRemoteComment({ taskId: m.taskId, commentId: m.commentId, comment: m.comment }, 'changed'),
    );
    this.on<MemberChangedMessage>(link, 'MemberChanged', (m) => this.memberChanged(m));
    this.on<BoardDeletedMessage>(link, 'BoardDeleted', () => void this.lostAccess('This board was deleted by its owner.'));

    // Entries carry no origin: the feed lists my own actions too, and the panel drops duplicates by id.
    link.on('ActivityAdded', ((m: ActivityAddedMessage) => this.store.applyRemoteActivity(m.entry)) as (body: never) => void);
  }

  /** Registers a handler that skips the echo of this tab's own changes. */
  private on<T extends { originClientId: string | null }>(link: HubLink, method: string, handler: (message: T) => void): void {
    link.on(method, ((message: T) => {
      if (this.link !== link || message.originClientId === this.clientId.value) return;
      handler(message);
    }) as (body: never) => void);
  }

  private memberChanged(m: MemberChangedMessage): void {
    const me = this.auth.user()?.id;
    if (m.userId === me) {
      if (m.member === null) {
        void this.lostAccess('You were removed from this board.');
        return;
      }
      if (m.member.role !== this.store.role()) {
        this.notifier.info(`Your role on this board is now ${m.member.role}.`);
        this.store.board.update((b) => (b ? { ...b, myRole: m.member!.role } : b));
      }
    }
    this.store.applyRemoteMember(m.userId, m.member);
  }

  private async lostAccess(message: string): Promise<void> {
    await this.stop();
    this.notifier.error(message);
    await this.router.navigate(['/boards']);
  }
}

/** The hub answers "Board not found." for both a missing board and one the user is not in. */
function isBoardNotFound(error: unknown): boolean {
  return error instanceof Error && error.message.includes('Board not found');
}
