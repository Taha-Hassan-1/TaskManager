namespace TaskManager.API.Models;

public enum BoardRole { Viewer, Editor, Owner }

public enum TaskPriority { Low = 0, Normal = 1, High = 2, Urgent = 3 }

public enum ActivityType
{
    BoardCreated, MemberAdded, MemberRemoved, MemberRoleChanged,
    ColumnCreated, ColumnUpdated,
    TaskCreated, TaskUpdated, TaskMoved, TaskDeleted,
    CommentAdded
}
