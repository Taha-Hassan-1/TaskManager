using Microsoft.EntityFrameworkCore;
using TaskManager.API.Models;

namespace TaskManager.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<BoardColumn> Columns => Set<BoardColumn>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ActivityEntry> Activity => Set<ActivityEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // SQL Server rejects schemas with several cascade paths to one table. The rule used here:
    // deleting a Board cascades down the tree (members, columns, cards, labels, activity); every
    // foreign key to User is Restrict (no account deletion in v1) except tokens, which go with the user.
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(50);
            e.Property(u => u.Email).HasMaxLength(256);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Username).IsUnique();
        });

        b.Entity<Board>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<BoardMember>(e =>
        {
            e.HasKey(m => new { m.BoardId, m.UserId });
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(10);
            e.HasOne(m => m.Board).WithMany(x => x.Members).HasForeignKey(m => m.BoardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.Memberships).HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => m.UserId);
            // Exactly one Owner per board.
            e.HasIndex(m => m.BoardId).IsUnique().HasFilter("[Role] = 'Owner'");
        });

        b.Entity<BoardColumn>(e =>
        {
            e.ToTable("Columns");
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasOne(c => c.Board).WithMany(x => x.Columns).HasForeignKey(c => c.BoardId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.BoardId, c.Position });
        });

        b.Entity<TaskItem>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(4000);
            e.Property(t => t.RowVersion).IsRowVersion();
            e.HasOne(t => t.Board).WithMany(x => x.Tasks).HasForeignKey(t => t.BoardId).OnDelete(DeleteBehavior.Cascade);
            // Board -> Column and Board -> Task are both cascades; the Column -> Task one must not be.
            e.HasOne(t => t.Column).WithMany(c => c.Tasks).HasForeignKey(t => t.ColumnId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CreatedBy).WithMany(u => u.CreatedTasks).HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => new { t.BoardId, t.ColumnId, t.Position });
        });

        b.Entity<Label>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(50);
            e.Property(l => l.Color).HasMaxLength(9);
            e.HasOne(l => l.Board).WithMany(x => x.Labels).HasForeignKey(l => l.BoardId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskLabel>(e =>
        {
            e.HasKey(x => new { x.TaskItemId, x.LabelId });
            e.HasOne(x => x.TaskItem).WithMany(t => t.Labels).HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Label).WithMany().HasForeignKey(x => x.LabelId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Comment>(e =>
        {
            e.Property(c => c.Body).HasMaxLength(2000);
            e.HasOne(c => c.TaskItem).WithMany(t => t.Comments).HasForeignKey(c => c.TaskItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActivityEntry>(e =>
        {
            e.ToTable("Activity");
            e.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
            e.HasOne(a => a.Board).WithMany().HasForeignKey(a => a.BoardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Actor).WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.BoardId, a.CreatedAt });
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(r => r.TokenHash).HasMaxLength(64);
            e.HasIndex(r => r.TokenHash).IsUnique();
            e.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
