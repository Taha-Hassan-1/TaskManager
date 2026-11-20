import { Injectable, inject } from '@angular/core';
import { BoardDetailDto } from '../api/models';
import { TeamBoardApi } from './api/team-board-api';

/** Hands a board loaded by the route guard to the board screen, once, so it is not fetched twice. */
@Injectable({ providedIn: 'root' })
export class BoardCache {
  private readonly api = inject(TeamBoardApi);
  private readonly prefetched = new Map<number, BoardDetailDto>();

  async prefetch(id: number): Promise<void> {
    this.prefetched.set(id, await this.api.getBoard(id));
  }

  take(id: number): BoardDetailDto | undefined {
    const board = this.prefetched.get(id);
    this.prefetched.delete(id);
    return board;
  }
}
