using Microsoft.EntityFrameworkCore;

namespace Ground43.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<BatchCreationEntity> BatchCreations => Set<BatchCreationEntity>();
    public DbSet<AuthSessionEntity> AuthSessions => Set<AuthSessionEntity>();
    public DbSet<StatusEntity> Statuses => Set<StatusEntity>();
    public DbSet<ModuleEntity> Modules => Set<ModuleEntity>();
    public DbSet<RequirementTypeEntity> RequirementTypes => Set<RequirementTypeEntity>();
    public DbSet<RequirementDefaultsEntity> RequirementDefaults => Set<RequirementDefaultsEntity>();
    public DbSet<SystemBrandingEntity> SystemBranding => Set<SystemBrandingEntity>();
    public DbSet<JobLockEntity> JobLocks => Set<JobLockEntity>();
    public DbSet<IterationEntity> Iterations => Set<IterationEntity>();
    public DbSet<RequirementEntity> Requirements => Set<RequirementEntity>();
    public DbSet<CommentEntity> Comments => Set<CommentEntity>();
    public DbSet<HistoryEntity> History => Set<HistoryEntity>();
    public DbSet<AttachmentEntity> Attachments => Set<AttachmentEntity>();
    public DbSet<UploadSessionEntity> UploadSessions => Set<UploadSessionEntity>();
    public DbSet<CustomFieldEntity> CustomFields => Set<CustomFieldEntity>();
    public DbSet<WorkCalendarEntity> WorkCalendar => Set<WorkCalendarEntity>();
    public DbSet<NotificationLogEntity> NotificationLogs => Set<NotificationLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BatchCreationEntity>(entity => { entity.HasKey(x => new { x.UserId, x.RequestId }); });
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Account).IsUnique();
            entity.HasIndex(x => x.WeComUserId).IsUnique();
            entity.Property(x => x.Account).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Role).HasMaxLength(20);
            entity.Property(x => x.WeComEmail).HasMaxLength(254);
            entity.Property(x => x.WeComUserId).HasMaxLength(80);
        });
        modelBuilder.Entity<AuthSessionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<StatusEntity>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => x.SortOrder); });
        modelBuilder.Entity<ModuleEntity>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => x.Name).IsUnique(); });
        modelBuilder.Entity<RequirementTypeEntity>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => x.Name).IsUnique(); entity.HasIndex(x => x.SortOrder); });
        modelBuilder.Entity<RequirementDefaultsEntity>(entity => entity.HasKey(x => x.Id));
        modelBuilder.Entity<SystemBrandingEntity>(entity => entity.HasKey(x => x.Id));
        modelBuilder.Entity<JobLockEntity>(entity => entity.HasKey(x => x.Key));
        modelBuilder.Entity<IterationEntity>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.StartDate, x.EndDate }).IsUnique(); });
        modelBuilder.Entity<RequirementEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UpdatedAt);
            entity.HasIndex(x => x.ParentId);
            entity.HasIndex(x => x.StatusId);
            entity.HasIndex(x => x.AssigneeId);
            entity.HasIndex(x => x.IterationId);
            entity.HasIndex(x => x.DueDate);
            entity.HasIndex(x => x.Module);
            entity.HasIndex(x => x.ReviewerId);
            entity.HasIndex(x => x.RequirementTypeId);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.Module).HasMaxLength(100);
            entity.Property(x => x.Priority).HasMaxLength(20);
            entity.Property(x => x.CustomValuesJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : "TEXT");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RequirementType).WithMany().HasForeignKey(x => x.RequirementTypeId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Iteration).WithMany().HasForeignKey(x => x.IterationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CommentEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RequirementId, x.CreatedAt });
            entity.HasOne(x => x.Requirement).WithMany(x => x.Comments).HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<HistoryEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RequirementId, x.CreatedAt });
            entity.HasOne(x => x.Requirement).WithMany(x => x.History).HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AttachmentEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RequirementId);
            entity.HasOne(x => x.Requirement).WithMany(x => x.Attachments).HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<UploadSessionEntity>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => x.ExpiresAt); });
        modelBuilder.Entity<CustomFieldEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.OptionsJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : "TEXT");
        });
        modelBuilder.Entity<WorkCalendarEntity>(entity => entity.HasKey(x => x.Date));
        modelBuilder.Entity<NotificationLogEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.Property(x => x.PayloadJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : "TEXT");
        });
    }
}
