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
    public DbSet<MemoryEntry> Memories => Set<MemoryEntry>();
    public DbSet<MemoryEntryTag> MemoryTags => Set<MemoryEntryTag>();
    public DbSet<MemoryLink> MemoryLinks => Set<MemoryLink>();

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

        builder.Entity<MemoryEntry>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.SourceReference).HasMaxLength(260);
            entity.HasOne(x => x.Wing)
                .WithMany(x => x.Memories)
                .HasForeignKey(x => x.WingId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Room)
                .WithMany(x => x.Memories)
                .HasForeignKey(x => x.RoomId)
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
    }
}
