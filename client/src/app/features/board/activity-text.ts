import { ActivityDto } from '../../api/models';

/** One line of the activity feed, after the actor's name: "moved “Fix login” from To do to Doing". */
export function describeActivity(entry: ActivityDto, columnNames: ReadonlyMap<number, string>): string {
  const p = (entry.payload ?? {}) as Record<string, unknown>;
  const text = (key: string) => (typeof p[key] === 'string' ? (p[key] as string) : undefined);
  const column = (key: string) => {
    const id = p[key];
    return typeof id === 'number' ? (columnNames.get(id) ?? 'another column') : 'another column';
  };
  const card = text('title') ? `“${text('title')}”` : 'a card';

  switch (entry.type) {
    case 'BoardCreated': return `created the board “${text('name') ?? ''}”`;
    case 'BoardRenamed': return `renamed the board to “${text('name') ?? ''}”`;
    case 'MemberAdded': return `added ${text('username') ?? 'someone'} as ${text('role') ?? 'a member'}`;
    case 'MemberRemoved': return `removed ${text('username') ?? 'a member'} from the board`;
    case 'MemberRoleChanged': return `made ${text('username') ?? 'a member'} ${text('role') ?? 'a member'}`;
    case 'ColumnCreated': return `added the column “${text('name') ?? ''}”`;
    case 'ColumnUpdated': return `updated the column “${text('name') ?? ''}”`;
    case 'TaskCreated': return `created ${card} in ${column('columnId')}`;
    case 'TaskUpdated': return `edited ${card}`;
    case 'TaskMoved': return `moved ${card} from ${column('fromColumnId')} to ${column('toColumnId')}`;
    case 'TaskDeleted': return `deleted ${card}`;
    case 'CommentAdded': return `commented on ${card}`;
    default: return 'made a change';
  }
}
