import { ActivityDto, BoardRole, CardDto, ColumnDto, CommentDto, LabelDto, MemberDto } from '../../api/models';

/**
 * What the server pushes to a board's group (see Infrastructure/LiveMessages.cs on the API).
 * They are not part of the OpenAPI document, so they are written out here; the API's hub tests pin their shape.
 * `originClientId` is the tab that caused the change, or null.
 */
interface Live {
  boardId: number;
  originClientId: string | null;
}

export interface TaskCreatedMessage extends Live { card: CardDto }
export interface TaskUpdatedMessage extends Live { card: CardDto }
export interface TaskMovedMessage extends Live {
  taskId: number;
  fromColumnId: number;
  columnId: number;
  position: number;
  rowVersion: string;
}
export interface TaskDeletedMessage extends Live { taskId: number }
export interface ColumnChangedMessage extends Live { column: ColumnDto }
export interface CommentAddedMessage extends Live { taskId: number; comment: CommentDto }
/** `comment` is null when the comment was deleted. */
export interface CommentChangedMessage extends Live { taskId: number; commentId: number; comment: CommentDto | null }
/** `member` is null when the person left or was removed. */
export interface MemberChangedMessage extends Live { userId: number; member: MemberDto | null }
/** `label` is null when the label was deleted. */
export interface LabelChangedMessage extends Live { labelId: number; label: LabelDto | null }
export interface BoardChangedMessage extends Live { name: string }
export type BoardDeletedMessage = Live;
export interface ActivityAddedMessage { boardId: number; entry: ActivityDto }

export type { BoardRole };
