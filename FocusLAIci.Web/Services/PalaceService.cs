using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed class PalaceService
{
    private readonly FocusMemoryContext _dbContext;

    public PalaceService(FocusMemoryContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var memories = await BuildMemoryQuery()
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var wings = await _dbContext.Wings
            .AsNoTracking()
            .Include(x => x.Rooms)
            .Include(x => x.Memories)
            .OrderBy(x => x.Name)
            .Select(x => new WingSummaryViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Slug = x.Slug,
                Description = x.Description,
                RoomCount = x.Rooms.Count,
                MemoryCount = x.Memories.Count,
                LatestActivityUtc = x.Memories.OrderByDescending(m => m.UpdatedUtc).Select(m => m.UpdatedUtc).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new DashboardViewModel
        {
            Stats = new PalaceStatsViewModel
            {
                WingCount = await _dbContext.Wings.CountAsync(cancellationToken),
                RoomCount = await _dbContext.Rooms.CountAsync(cancellationToken),
                MemoryCount = await _dbContext.Memories.CountAsync(cancellationToken),
                PinnedCount = await _dbContext.Memories.CountAsync(x => x.IsPinned, cancellationToken),
                TagCount = await _dbContext.Tags.CountAsync(cancellationToken)
            },
            Wings = wings,
            RecentMemories = memories.Select(MapCard).ToArray(),
            PinnedMemories = memories.Where(x => x.IsPinned).Select(MapCard).ToArray(),
            SearchExamples =
            [
                "installer reliability",
                "why did we choose local-first memory",
                "frontend browse patterns"
            ]
        };
    }

    public async Task<ExploreViewModel> GetExploreAsync(string? query, Guid? wingId, Guid? roomId, MemoryKind? kind, string? tag, CancellationToken cancellationToken)
    {
        var memories = await BuildMemoryQuery(query, wingId, roomId, kind, tag)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return new ExploreViewModel
        {
            Query = query?.Trim() ?? string.Empty,
            WingId = wingId,
            RoomId = roomId,
            Kind = kind,
            Tag = tag?.Trim() ?? string.Empty,
            Memories = memories.Select(MapCard).ToArray(),
            WingOptions = await BuildWingOptionsAsync(cancellationToken),
            RoomOptions = await BuildRoomOptionsAsync(wingId, cancellationToken)
        };
    }

    public async Task<WingDetailViewModel?> GetWingAsync(string slug, CancellationToken cancellationToken)
    {
        var wing = await _dbContext.Wings
            .AsNoTracking()
            .Include(x => x.Rooms)
            .Include(x => x.Memories)
                .ThenInclude(x => x.MemoryTags)
                    .ThenInclude(x => x.Tag)
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (wing is null)
        {
            return null;
        }

        return new WingDetailViewModel
        {
            Id = wing.Id,
            Name = wing.Name,
            Description = wing.Description,
            Rooms = wing.Rooms
                .OrderBy(x => x.Name)
                .Select(x => new RoomSummaryViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    MemoryCount = wing.Memories.Count(m => m.RoomId == x.Id)
                })
                .ToArray(),
            Memories = wing.Memories
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.UpdatedUtc)
                .Select(MapCard)
                .ToArray()
        };
    }

    public async Task<MemoryDetailViewModel?> GetMemoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .Include(x => x.OutgoingLinks)
                .ThenInclude(x => x.ToMemoryEntry)
            .Include(x => x.IncomingLinks)
                .ThenInclude(x => x.FromMemoryEntry)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (memory is null)
        {
            return null;
        }

        return new MemoryDetailViewModel
        {
            Memory = MapCard(memory),
            Content = memory.Content,
            SourceReference = memory.SourceReference,
            CreatedUtc = memory.CreatedUtc,
            OccurredUtc = memory.OccurredUtc,
            OutgoingLinks = memory.OutgoingLinks
                .OrderBy(x => x.Label)
                .Select(x => new MemoryRelationshipViewModel
                {
                    MemoryId = x.ToMemoryEntryId,
                    Title = x.ToMemoryEntry?.Title ?? "Related memory",
                    Label = x.Label
                })
                .ToArray(),
            IncomingLinks = memory.IncomingLinks
                .OrderBy(x => x.Label)
                .Select(x => new MemoryRelationshipViewModel
                {
                    MemoryId = x.FromMemoryEntryId,
                    Title = x.FromMemoryEntry?.Title ?? "Related memory",
                    Label = x.Label
                })
                .ToArray()
        };
    }

    public async Task<MemoryEditorViewModel> BuildMemoryEditorAsync(Guid? id, Guid? selectedWingId, CancellationToken cancellationToken)
    {
        if (id is null)
        {
            return new MemoryEditorViewModel
            {
                Heading = "Add memory",
                SubmitLabel = "Add Memory",
                Input = new MemoryEditorInput
                {
                    WingId = selectedWingId
                },
                WingOptions = await BuildWingOptionsAsync(cancellationToken),
                RoomOptions = await BuildRoomOptionsAsync(selectedWingId, cancellationToken)
            };
        }

        var memory = await _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        return new MemoryEditorViewModel
        {
            Heading = "Edit memory",
            SubmitLabel = "Save changes",
            Input = new MemoryEditorInput
            {
                Id = memory.Id,
                Title = memory.Title,
                Summary = memory.Summary,
                Content = memory.Content,
                Kind = memory.Kind,
                SourceKind = memory.SourceKind,
                SourceReference = memory.SourceReference,
                Importance = memory.Importance,
                IsPinned = memory.IsPinned,
                OccurredUtc = memory.OccurredUtc,
                WingId = selectedWingId ?? memory.WingId,
                RoomId = memory.RoomId,
                TagsText = string.Join(", ", memory.MemoryTags.Select(x => x.Tag!.Name).OrderBy(x => x))
            },
            WingOptions = await BuildWingOptionsAsync(cancellationToken),
            RoomOptions = await BuildRoomOptionsAsync(selectedWingId ?? memory.WingId, cancellationToken)
        };
    }

    public async Task<RoomEditorViewModel> BuildRoomEditorAsync(CancellationToken cancellationToken)
    {
        return new RoomEditorViewModel
        {
            Input = new RoomEditorInput(),
            WingOptions = await BuildWingOptionsAsync(cancellationToken)
        };
    }

    public async Task<Guid> SaveMemoryAsync(MemoryEditorInput input, CancellationToken cancellationToken)
    {
        var room = input.RoomId.HasValue
            ? await _dbContext.Rooms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.RoomId.Value, cancellationToken)
            : null;

        if (input.RoomId.HasValue && room is null)
        {
            throw new InvalidOperationException("The selected room no longer exists.");
        }

        if (input.WingId.HasValue && !await _dbContext.Wings.AnyAsync(x => x.Id == input.WingId.Value, cancellationToken))
        {
            throw new InvalidOperationException("The selected wing no longer exists.");
        }

        var wingId = room?.WingId ?? input.WingId;
        if (room is not null && input.WingId.HasValue && room.WingId != input.WingId.Value)
        {
            throw new InvalidOperationException("The selected room does not belong to the selected wing.");
        }

        MemoryEntry memory;
        if (input.Id is null)
        {
            memory = new MemoryEntry
            {
                CreatedUtc = DateTime.UtcNow
            };
            _dbContext.Memories.Add(memory);
        }
        else
        {
            memory = await _dbContext.Memories
                .Include(x => x.MemoryTags)
                .FirstOrDefaultAsync(x => x.Id == input.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Memory entry not found.");
        }

        memory.Title = input.Title.Trim();
        memory.Summary = input.Summary.Trim();
        memory.Content = input.Content.Trim();
        memory.Kind = input.Kind;
        memory.SourceKind = input.SourceKind;
        memory.SourceReference = input.SourceReference.Trim();
        memory.Importance = Math.Clamp(input.Importance, 1, 5);
        memory.IsPinned = input.IsPinned;
        memory.OccurredUtc = input.OccurredUtc;
        memory.WingId = wingId;
        memory.RoomId = room?.Id;
        memory.UpdatedUtc = DateTime.UtcNow;

        var tagNames = ParseTags(input.TagsText);
        await SyncTagsAsync(memory, tagNames, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return memory.Id;
    }

    public async Task<Guid> CreateWingAsync(WingEditorInput input, CancellationToken cancellationToken)
    {
        var wing = new Wing
        {
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Slug = await BuildUniqueWingSlugAsync(input.Name, cancellationToken)
        };

        _dbContext.Wings.Add(wing);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return wing.Id;
    }

    public async Task<Guid> CreateRoomAsync(RoomEditorInput input, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Wings.AnyAsync(x => x.Id == input.WingId, cancellationToken))
        {
            throw new InvalidOperationException("The selected wing no longer exists.");
        }

        var room = new Room
        {
            WingId = input.WingId,
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Slug = await BuildUniqueRoomSlugAsync(input.WingId, input.Name, cancellationToken)
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return room.Id;
    }

    public async Task<string?> GetWingSlugAsync(Guid wingId, CancellationToken cancellationToken)
    {
        return await _dbContext.Wings
            .AsNoTracking()
            .Where(x => x.Id == wingId)
            .Select(x => x.Slug)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PalaceApiSummaryViewModel> GetApiSummaryAsync(CancellationToken cancellationToken)
    {
        var dashboard = await GetDashboardAsync(cancellationToken);
        return new PalaceApiSummaryViewModel
        {
            Stats = dashboard.Stats,
            Wings = dashboard.Wings
        };
    }

    public async Task<IReadOnlyCollection<MemoryCardViewModel>> SearchMemoriesAsync(string? query, Guid? wingId, Guid? roomId, MemoryKind? kind, string? tag, CancellationToken cancellationToken)
    {
        return await BuildMemoryQuery(query, wingId, roomId, kind, tag)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(100)
            .Select(x => new MemoryCardViewModel
            {
                Id = x.Id,
                Title = x.Title,
                Summary = x.Summary,
                WingSlug = x.Wing != null ? x.Wing.Slug : string.Empty,
                WingName = x.Wing != null ? x.Wing.Name : "Unsorted",
                RoomName = x.Room != null ? x.Room.Name : "General",
                Kind = x.Kind,
                SourceKind = x.SourceKind,
                Importance = x.Importance,
                IsPinned = x.IsPinned,
                UpdatedUtc = x.UpdatedUtc,
                Tags = x.MemoryTags.OrderBy(mt => mt.Tag!.Name).Select(mt => mt.Tag!.Name).ToArray()
            })
            .ToListAsync(cancellationToken);
    }

    private IQueryable<MemoryEntry> BuildMemoryQuery(string? query = null, Guid? wingId = null, Guid? roomId = null, MemoryKind? kind = null, string? tag = null)
    {
        var memories = _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .AsQueryable();

        if (wingId.HasValue)
        {
            memories = memories.Where(x => x.WingId == wingId.Value);
        }

        if (roomId.HasValue)
        {
            memories = memories.Where(x => x.RoomId == roomId.Value);
        }

        if (kind.HasValue)
        {
            memories = memories.Where(x => x.Kind == kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim().ToLowerInvariant();
            memories = memories.Where(x =>
                x.Title.ToLower().Contains(normalized) ||
                x.Summary.ToLower().Contains(normalized) ||
                x.Content.ToLower().Contains(normalized) ||
                x.MemoryTags.Any(mt => mt.Tag!.Name.ToLower().Contains(normalized)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagSlug = SlugUtility.CreateSlug(tag);
            memories = memories.Where(x => x.MemoryTags.Any(mt => mt.Tag!.Slug == tagSlug));
        }

        return memories;
    }

    private async Task<IReadOnlyCollection<SelectListItem>> BuildWingOptionsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Wings
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<SelectListItem>> BuildRoomOptionsAsync(Guid? wingId, CancellationToken cancellationToken)
    {
        var query = _dbContext.Rooms
            .AsNoTracking()
            .Include(x => x.Wing)
            .AsQueryable();

        if (wingId.HasValue)
        {
            query = query.Where(x => x.WingId == wingId.Value);
        }

        return await query
            .OrderBy(x => x.Wing!.Name)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem($"{x.Wing!.Name} / {x.Name}", x.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private async Task SyncTagsAsync(MemoryEntry memory, IReadOnlyCollection<string> tagNames, CancellationToken cancellationToken)
    {
        var desiredSlugs = tagNames.Select(SlugUtility.CreateSlug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTags = await _dbContext.Tags
            .Where(x => desiredSlugs.Contains(x.Slug))
            .ToListAsync(cancellationToken);

        foreach (var tagName in tagNames)
        {
            var slug = SlugUtility.CreateSlug(tagName);
            if (existingTags.Any(x => x.Slug == slug))
            {
                continue;
            }

            var tag = new Tag
            {
                Name = tagName,
                Slug = slug
            };
            _dbContext.Tags.Add(tag);
            existingTags.Add(tag);
        }

        memory.MemoryTags.Clear();
        foreach (var tag in existingTags.OrderBy(x => x.Name))
        {
            memory.MemoryTags.Add(new MemoryEntryTag
            {
                MemoryEntry = memory,
                Tag = tag
            });
        }
    }

    private async Task<string> BuildUniqueWingSlugAsync(string name, CancellationToken cancellationToken)
    {
        var slug = SlugUtility.CreateSlug(name);
        var suffix = 2;
        while (await _dbContext.Wings.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            slug = $"{SlugUtility.CreateSlug(name)}-{suffix++}";
        }

        return slug;
    }

    private async Task<string> BuildUniqueRoomSlugAsync(Guid wingId, string name, CancellationToken cancellationToken)
    {
        var baseSlug = SlugUtility.CreateSlug(name);
        var slug = baseSlug;
        var suffix = 2;
        while (await _dbContext.Rooms.AnyAsync(x => x.WingId == wingId && x.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static IReadOnlyCollection<string> ParseTags(string tagsText)
    {
        return tagsText
            .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MemoryCardViewModel MapCard(MemoryEntry memory)
    {
        return new MemoryCardViewModel
        {
            Id = memory.Id,
            Title = memory.Title,
            Summary = memory.Summary,
            WingSlug = memory.Wing?.Slug ?? string.Empty,
            WingName = memory.Wing?.Name ?? "Unsorted",
            RoomName = memory.Room?.Name ?? "General",
            Kind = memory.Kind,
            SourceKind = memory.SourceKind,
            Importance = memory.Importance,
            IsPinned = memory.IsPinned,
            UpdatedUtc = memory.UpdatedUtc,
            Tags = memory.MemoryTags.OrderBy(x => x.Tag!.Name).Select(x => x.Tag!.Name).ToArray()
        };
    }
}
