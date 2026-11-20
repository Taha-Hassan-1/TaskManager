import { ActivityDto } from '../../api/models';
import { describeActivity } from './activity-text';

const columns = new Map([[10, 'To do'], [20, 'Doing']]);
const entry = (type: ActivityDto['type'], payload: object): ActivityDto => ({
  id: 1, type, actorId: 1, actorName: 'ann', taskItemId: null, payload, createdAt: '2026-01-01T00:00:00Z',
});

describe('describeActivity', () => {
  it('names the card and both columns of a move', () => {
    expect(describeActivity(entry('TaskMoved', { title: 'Fix login', fromColumnId: 10, toColumnId: 20 }), columns))
      .toBe('moved “Fix login” from To do to Doing');
  });

  it('copes with entries written before cards carried their title', () => {
    expect(describeActivity(entry('TaskMoved', { fromColumnId: 10, toColumnId: 20 }), columns)).toBe('moved a card from To do to Doing');
  });

  it('does not break on a column it does not know', () => {
    expect(describeActivity(entry('TaskMoved', { title: 'x', fromColumnId: 99, toColumnId: 20 }), columns)).toContain('from another column');
  });

  it.each([
    ['TaskCreated', { title: 'New', columnId: 10 }, 'created “New” in To do'],
    ['TaskUpdated', { title: 'Edit' }, 'edited “Edit”'],
    ['TaskDeleted', { title: 'Gone' }, 'deleted “Gone”'],
    ['CommentAdded', { title: 'Topic', commentId: 3 }, 'commented on “Topic”'],
    ['ColumnCreated', { name: 'Review' }, 'added the column “Review”'],
    ['BoardRenamed', { name: 'New name' }, 'renamed the board to “New name”'],
    ['MemberAdded', { username: 'bob', role: 'Viewer' }, 'added bob as Viewer'],
    ['MemberRemoved', { username: 'bob' }, 'removed bob from the board'],
    ['MemberRoleChanged', { username: 'bob', role: 'Editor' }, 'made bob Editor'],
  ] as const)('%s', (type, payload, expected) => {
    expect(describeActivity(entry(type, payload), columns)).toBe(expected);
  });
});
