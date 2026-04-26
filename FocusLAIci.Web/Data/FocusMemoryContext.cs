using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Data;

public sealed class FocusMemoryContext : DbContext
{
    public FocusMemoryContext(DbContextOptions<FocusMemoryContext> options)
        : base(options)
    {
    }

    public DbSet<Wing> Wings => Set<Wing>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TodoEntry> Todos => Set<TodoEntry>();
    public DbSet<TicketEntry> Tickets => Set<TicketEntry>();
    public DbSet<TicketNoteEntry> TicketNotes => Set<TicketNoteEntry>();
    public DbSet<TicketActivityEntry> TicketActivities => Set<TicketActivityEntry>();
    public DbSet<TicketTimeLogEntry> TicketTimeLogs => Set<TicketTimeLogEntry>();
    public DbSet<MemoryEntry> Memories => Set<MemoryEntry>();
    public DbSet<MemoryEntryTag> MemoryTags => Set<MemoryEntryTag>();
    public DbSet<MemoryLink> MemoryLinks => Set<MemoryLink>();
    public DbSet<CodeGraphProject> CodeGraphProjects => Set<CodeGraphProject>();
    public DbSet<CodeGraphFile> CodeGraphFiles => Set<CodeGraphFile>();
    public DbSet<CodeGraphNode> CodeGraphNodes => Set<CodeGraphNode>();
    public DbSet<CodeGraphEdge> CodeGraphEdges => Set<CodeGraphEdge>();
    public DbSet<ContextLinkEntry> ContextLinks => Set<ContextLinkEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Wing>(entity =>
        {
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(400);
        });

        builder.Entity<SiteSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.DisplayName).HasMaxLength(80);
            entity.Property(x => x.HomeHeroCopy).HasMaxLength(400);
            entity.Property(x => x.TimeZoneId).HasMaxLength(120);
        });

        builder.Entity<Room>(entity =>
        {
            entity.HasIndex(x => new { x.WingId, x.Slug }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(400);
            entity.HasOne(x => x.Wing)
                .WithMany(x => x.Rooms)
                .HasForeignKey(x => x.WingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Tag>(entity =>
        {
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.Property(x => x.Slug).HasMaxLength(120);
        });

        builder.Entity<TodoEntry>(entity =>
        {
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.Title).HasMaxLength(180);
            entity.Property(x => x.Details).HasColumnType("TEXT");
        });

        builder.Entity<TicketEntry>(entity =>
        {
            entity.HasIndex(x => x.TicketNumber).IsUnique();
            entity.HasIndex(x => new { x.Status, x.UpdatedUtc });
            entity.HasIndex(x => x.ParentTicketId);
            entity.Property(x => x.TicketNumber).HasMaxLength(24);
            entity.Property(x => x.Title).HasMaxLength(180);
            entity.Property(x => x.Description).HasColumnType("TEXT");
            entity.Property(x => x.Assignee).HasMaxLength(120);
            entity.Property(x => x.TagsText).HasMaxLength(400);
            entity.Property(x => x.GitBranch).HasMaxLength(120);
            entity.Property(x => x.GitCommit).HasMaxLength(80);
            entity.HasOne(x => x.ParentTicket)
                .WithMany(x => x.SubTickets)
                .HasForeignKey(x => x.ParentTicketId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SummaryMemory)
                .WithMany()
                .HasForeignKey(x => x.SummaryMemoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TicketNoteEntry>(entity =>
        {
            entity.HasIndex(x => new { x.TicketId, x.CreatedUtc });
            entity.Property(x => x.Author).HasMaxLength(120);
            entity.Property(x => x.Content).HasColumnType("TEXT");
            entity.HasOne(x => x.Ticket)
                .WithMany(x => x.Notes)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TicketActivityEntry>(entity =>
        {
            entity.HasIndex(x => new { x.TicketId, x.CreatedUtc });
            entity.Property(x => x.ActivityType).HasMaxLength(60);
            entity.Property(x => x.Message).HasMaxLength(500);
            entity.Property(x => x.Metadata).HasColumnType("TEXT");
            entity.HasOne(x => x.Ticket)
                .WithMany(x => x.Activities)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TicketTimeLogEntry>(entity =>
        {
            entity.HasIndex(x => new { x.TicketId, x.LoggedUtc });
            entity.Property(x => x.ModelName).HasMaxLength(120);
            entity.Property(x => x.Summary).HasMaxLength(260);
            entity.HasOne(x => x.Ticket)
                .WithMany(x => x.TimeLogs)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MemoryEntry>(entity =>
        {
            entity.HasIndex(x => x.VerificationStatus);
            entity.HasIndex(x => x.ReviewAfterUtc);
            entity.HasIndex(x => x.LifecycleState);
            entity.HasIndex(x => x.SupersededByMemoryId);
            entity.HasIndex(x => x.LastReferencedUtc);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.SourceReference).HasMaxLength(260);
            entity.Property(x => x.LifecycleReason).HasMaxLength(260);
            entity.HasOne(x => x.Wing)
                .WithMany(x => x.Memories)
                .HasForeignKey(x => x.WingId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Room)
                .WithMany(x => x.Memories)
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.SupersededByMemory)
                .WithMany()
                .HasForeignKey(x => x.SupersededByMemoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MemoryEntryTag>(entity =>
        {
            entity.HasKey(x => new { x.MemoryEntryId, x.TagId });
            entity.HasOne(x => x.MemoryEntry)
                .WithMany(x => x.MemoryTags)
                .HasForeignKey(x => x.MemoryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Tag)
                .WithMany(x => x.MemoryTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MemoryLink>(entity =>
        {
            entity.Property(x => x.Label).HasMaxLength(80);
            entity.HasOne(x => x.FromMemoryEntry)
                .WithMany(x => x.OutgoingLinks)
                .HasForeignKey(x => x.FromMemoryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ToMemoryEntry)
                .WithMany(x => x.IncomingLinks)
                .HasForeignKey(x => x.ToMemoryEntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ContextLinkEntry>(entity =>
        {
            entity.Property(x => x.Label).HasMaxLength(120);
            entity.HasIndex(x => new { x.SourceKind, x.SourceId });
            entity.HasIndex(x => new { x.TargetKind, x.TargetId });
            entity.HasIndex(x => new { x.SourceKind, x.SourceId, x.TargetKind, x.TargetId }).IsUnique();
        });

        builder.Entity<CodeGraphProject>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.RootPath).HasMaxLength(400);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Summary).HasColumnType("TEXT");
        });

        builder.Entity<CodeGraphFile>(entity =>
        {
            entity.HasIndex(x => new { x.ProjectId, x.RelativePath }).IsUnique();
            entity.Property(x => x.RelativePath).HasMaxLength(320);
            entity.Property(x => x.Language).HasMaxLength(40);
            entity.Property(x => x.ContentHash).HasMaxLength(128);
            entity.HasOne(x => x.Project)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CodeGraphNode>(entity =>
        {
            entity.HasIndex(x => new { x.ProjectId, x.NodeKey }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.NodeType, x.Label });
            entity.Property(x => x.NodeKey).HasMaxLength(400);
            entity.Property(x => x.Label).HasMaxLength(180);
            entity.Property(x => x.SecondaryLabel).HasMaxLength(240);
            entity.Property(x => x.Language).HasMaxLength(40);
            entity.Property(x => x.Metadata).HasColumnType("TEXT");
            entity.HasOne(x => x.Project)
                .WithMany(x => x.Nodes)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.File)
                .WithMany(x => x.Nodes)
                .HasForeignKey(x => x.FileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CodeGraphEdge>(entity =>
        {
            entity.HasIndex(x => new { x.ProjectId, x.FromNodeId, x.ToNodeId, x.RelationshipType });
            entity.Property(x => x.RelationshipType).HasMaxLength(40);
            entity.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
            entity.Property(x => x.Details).HasMaxLength(400);
            entity.HasOne(x => x.Project)
                .WithMany(x => x.Edges)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FromNode)
                .WithMany(x => x.OutgoingEdges)
                .HasForeignKey(x => x.FromNodeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ToNode)
                .WithMany(x => x.IncomingEdges)
                .HasForeignKey(x => x.ToNodeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
