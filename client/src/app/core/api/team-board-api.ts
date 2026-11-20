import { Injectable, inject } from '@angular/core';
import { Api } from '../../api/api';
import {
  apiAuthLoginPost, apiAuthLogoutPost, apiAuthRefreshPost, apiAuthRegisterPost,
  apiBoardsBoardIdActivityGet, apiBoardsBoardIdColumnsPost, apiBoardsBoardIdLabelsPost,
  apiBoardsBoardIdMembersPost, apiBoardsBoardIdMembersUserIdDelete, apiBoardsBoardIdMembersUserIdPut,
  apiBoardsBoardIdTasksPost, apiBoardsGet, apiBoardsIdDelete, apiBoardsIdGet, apiBoardsIdPut, apiBoardsPost,
  apiColumnsIdPut, apiCommentsIdDelete, apiCommentsIdPut, apiLabelsIdDelete, apiMeGet,
  apiTasksIdDelete, apiTasksIdGet, apiTasksIdMovePatch, apiTasksIdPut, apiTasksTaskIdCommentsPost,
} from '../../api/functions';
import {
  ActivityPageDto, AuthResponse, BoardDetailDto, BoardRole, BoardSummaryDto, CardDetailDto, CardDto,
  ColumnDto, ColumnRequest, CommentDto, CreateCardRequest, CreateLabelRequest, LabelDto, LoginRequest,
  MemberDto, MoveCardRequest, MoveResultDto, RegisterRequest, UpdateCardRequest, UserDto,
} from '../../api/models';

/**
 * The one place the app talks to the server. Components and stores depend on this, never on the generated
 * functions, so tests can replace it with a plain object and the generated code stays an implementation detail.
 */
@Injectable({ providedIn: 'root' })
export class TeamBoardApi {
  private readonly api = inject(Api);

  // auth
  login(body: LoginRequest): Promise<AuthResponse> { return this.api.invoke(apiAuthLoginPost, { body }); }
  register(body: RegisterRequest): Promise<AuthResponse> { return this.api.invoke(apiAuthRegisterPost, { body }); }
  refresh(): Promise<AuthResponse> { return this.api.invoke(apiAuthRefreshPost, {}); }
  logout(): Promise<void> { return this.api.invoke(apiAuthLogoutPost, {}); }
  me(): Promise<UserDto> { return this.api.invoke(apiMeGet, {}); }

  // boards and members
  listBoards(): Promise<BoardSummaryDto[]> { return this.api.invoke(apiBoardsGet, {}); }
  createBoard(name: string): Promise<BoardSummaryDto> { return this.api.invoke(apiBoardsPost, { body: { name } }); }
  getBoard(id: number): Promise<BoardDetailDto> { return this.api.invoke(apiBoardsIdGet, { id }); }
  renameBoard(id: number, name: string): Promise<BoardSummaryDto> { return this.api.invoke(apiBoardsIdPut, { id, body: { name } }); }
  deleteBoard(id: number): Promise<void> { return this.api.invoke(apiBoardsIdDelete, { id }); }
  invite(boardId: number, email: string, role: BoardRole): Promise<MemberDto> {
    return this.api.invoke(apiBoardsBoardIdMembersPost, { boardId, body: { email, role } });
  }
  changeRole(boardId: number, userId: number, role: BoardRole): Promise<MemberDto> {
    return this.api.invoke(apiBoardsBoardIdMembersUserIdPut, { boardId, userId, body: { role } });
  }
  removeMember(boardId: number, userId: number): Promise<void> {
    return this.api.invoke(apiBoardsBoardIdMembersUserIdDelete, { boardId, userId });
  }

  // columns
  createColumn(boardId: number, body: ColumnRequest): Promise<ColumnDto> { return this.api.invoke(apiBoardsBoardIdColumnsPost, { boardId, body }); }
  updateColumn(id: number, body: ColumnRequest): Promise<ColumnDto> { return this.api.invoke(apiColumnsIdPut, { id, body }); }

  // cards
  createCard(boardId: number, body: CreateCardRequest): Promise<CardDto> { return this.api.invoke(apiBoardsBoardIdTasksPost, { boardId, body }); }
  getCard(id: number): Promise<CardDetailDto> { return this.api.invoke(apiTasksIdGet, { id }); }
  updateCard(id: number, body: UpdateCardRequest): Promise<CardDto> { return this.api.invoke(apiTasksIdPut, { id, body }); }
  moveCard(id: number, body: MoveCardRequest): Promise<MoveResultDto> { return this.api.invoke(apiTasksIdMovePatch, { id, body }); }
  deleteCard(id: number): Promise<void> { return this.api.invoke(apiTasksIdDelete, { id }); }

  // labels and comments
  createLabel(boardId: number, body: CreateLabelRequest): Promise<LabelDto> { return this.api.invoke(apiBoardsBoardIdLabelsPost, { boardId, body }); }
  deleteLabel(id: number): Promise<void> { return this.api.invoke(apiLabelsIdDelete, { id }); }
  addComment(taskId: number, body: string): Promise<CommentDto> { return this.api.invoke(apiTasksTaskIdCommentsPost, { taskId, body: { body } }); }
  updateComment(id: number, body: string): Promise<CommentDto> { return this.api.invoke(apiCommentsIdPut, { id, body: { body } }); }
  deleteComment(id: number): Promise<void> { return this.api.invoke(apiCommentsIdDelete, { id }); }

  // activity
  activity(boardId: number, cursor?: string, limit?: number): Promise<ActivityPageDto> {
    return this.api.invoke(apiBoardsBoardIdActivityGet, { boardId, cursor, limit });
  }
}
