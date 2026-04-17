using System.Text;
using System.Text.RegularExpressions;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed class TicketingService
{
    private static readonly Regex TicketNumberPattern = new(@"^TKT-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BulletPattern = new(@"^(?:[-*•]|\d+[.)])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly FocusMemoryContext _dbContext;

    public TicketingService(FocusMemoryContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TicketBoardViewModel> GetBoardAsync(CancellationToken cancellationToken)
    {
        var tickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(x => x.SubTickets)
            .Include(x => x.TimeLogs)
            .Where(x => x.ParentTicketId == null)
            .OrderBy(x => x.Status == TicketStatus.InProgress ? 0 : x.Status == TicketStatus.New ? 1 : x.Status == TicketStatus.Blocked ? 2 : 3)
            .ThenByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        return new TicketBoardViewModel
        {
            Stats = await BuildStatsAsync(cancellationToken),
            CreateInput = new TicketEditorInput(),
            NewTickets = tickets.Where(x => x.Status == TicketStatus.New).Select(MapTicketSummary).ToArray(),
            InProgressTickets = tickets.Where(x => x.Status == TicketStatus.InProgress).Select(MapTicketSummary).ToArray(),
            BlockedTickets = tickets.Where(x => x.Status == TicketStatus.Blocked).Select(MapTicketSummary).ToArray(),
            CompletedTickets = tickets.Where(x => x.Status == TicketStatus.Completed).Select(MapTicketSummary).ToArray()
        };
    }

    public async Task<TicketDetailsViewModel> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .AsNoTracking()
            .Include(x => x.ParentTicket)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("That ticket no longer exists.");

        var subTickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(x => x.SubTickets)
            .Include(x => x.TimeLogs)
            .Where(x => x.ParentTicketId == id)
            .OrderBy(x => x.Status == TicketStatus.InProgress ? 0 : x.Status == TicketStatus.New ? 1 : x.Status == TicketStatus.Blocked ? 2 : 3)
            .ThenByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken);

        var notes = await _dbContext.TicketNotes
            .AsNoTracking()
            .Where(x => x.TicketId == id)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new TicketNoteViewModel
            {
                Id = x.Id,
                Author = x.Author,
                Content = x.Content,
                CreatedUtc = x.CreatedUtc,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var timeLogs = await _dbContext.TicketTimeLogs
            .AsNoTracking()
            .Where(x => x.TicketId == id)
            .OrderByDescending(x => x.LoggedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Select(x => new TicketTimeLogViewModel
            {
                Id = x.Id,
                ModelName = x.ModelName,
                Summary = x.Summary,
                MinutesSpent = x.MinutesSpent,
                LoggedUtc = x.LoggedUtc,
                CreatedUtc = x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var activities = await _dbContext.TicketActivities
            .AsNoTracking()
            .Where(x => x.TicketId == id)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new TicketActivityViewModel
            {
                Id = x.Id,
                ActivityType = x.ActivityType,
                Message = x.Message,
                Metadata = x.Metadata,
                CreatedUtc = x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var totalMinutesSpent = timeLogs.Sum(x => x.MinutesSpent);
        return new TicketDetailsViewModel
        {
            Ticket = new TicketDetailViewModel
            {
                Id = ticket.Id,
                ParentTicketId = ticket.ParentTicketId,
                ParentTicketNumber = ticket.ParentTicket?.TicketNumber ?? string.Empty,
                ParentTicketTitle = ticket.ParentTicket?.Title ?? string.Empty,
                SummaryMemoryId = ticket.SummaryMemoryId,
                TicketNumber = ticket.TicketNumber,
                Title = ticket.Title,
                Description = ticket.Description,
                Status = ticket.Status,
                StatusLabel = MapStatusLabel(ticket.Status),
                Priority = ticket.Priority,
                PriorityLabel = MapPriorityLabel(ticket.Priority),
                Assignee = ticket.Assignee,
                Tags = ParseTags(ticket.TagsText).ToArray(),
                GitBranch = ticket.GitBranch,
                HasGitCommit = HasGitCommit(ticket.GitCommit),
                CreatedUtc = ticket.CreatedUtc,
                UpdatedUtc = ticket.UpdatedUtc,
                CompletedUtc = ticket.CompletedUtc,
                SubTicketCount = subTickets.Count,
                CompletedSubTicketCount = subTickets.Count(x => x.Status == TicketStatus.Completed),
                TotalMinutesSpent = totalMinutesSpent
            },
            EditInput = new TicketEditorInput
            {
                Id = ticket.Id,
                Title = ticket.Title,
                Description = ticket.Description,
                Status = ticket.Status,
                Priority = ticket.Priority,
                Assignee = ticket.Assignee,
                TagsText = ticket.TagsText,
                GitBranch = ticket.GitBranch,
                HasGitCommit = HasGitCommit(ticket.GitCommit)
            },
            SubTicketInput = new TicketSubTicketInput(),
            NoteInput = new TicketNoteInput(),
            TimeLogInput = new TicketTimeLogInput(),
            SubTickets = subTickets.Select(MapTicketSummary).ToArray(),
            Notes = notes,
            TimeLogs = timeLogs,
            Activities = activities
        };
    }

    public async Task<Guid> CreateTicketAsync(TicketEditorInput input, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var ticket = new TicketEntry
        {
            TicketNumber = FormatTicketNumber(await BuildNextTicketSequenceAsync(cancellationToken)),
            Title = input.Title.Trim(),
            Description = input.Description.Trim(),
            Status = input.Status,
            Priority = input.Priority,
            Assignee = input.Assignee.Trim(),
            TagsText = NormalizeTagText(input.TagsText),
            GitBranch = input.GitBranch.Trim(),
            GitCommit = input.HasGitCommit ? "Yes" : "No",
            CreatedUtc = now,
            UpdatedUtc = now,
            CompletedUtc = input.Status == TicketStatus.Completed ? now : null
        };

        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(ticket.Id, "created", $"Created {ticket.TicketNumber}.", string.Empty, cancellationToken);

        if (ticket.Status == TicketStatus.Completed)
        {
            await EnsureTicketSummaryMemoryAsync(ticket.Id, cancellationToken);
        }

        return ticket.Id;
    }

    public async Task<Guid> CreateSubTicketAsync(Guid parentTicketId, TicketSubTicketInput input, CancellationToken cancellationToken)
    {
        var parent = await _dbContext.Tickets.FirstOrDefaultAsync(x => x.Id == parentTicketId, cancellationToken)
            ?? throw new InvalidOperationException("The parent ticket no longer exists.");

        var now = DateTime.UtcNow;
        var ticket = new TicketEntry
        {
            ParentTicketId = parent.Id,
            TicketNumber = FormatTicketNumber(await BuildNextTicketSequenceAsync(cancellationToken)),
            Title = input.Title.Trim(),
            Description = input.Description.Trim(),
            Status = input.Status,
            Priority = parent.Priority,
            Assignee = parent.Assignee,
            TagsText = parent.TagsText,
            GitBranch = parent.GitBranch,
            GitCommit = parent.GitCommit,
            CreatedUtc = now,
            UpdatedUtc = now,
            CompletedUtc = input.Status == TicketStatus.Completed ? now : null
        };

        _dbContext.Tickets.Add(ticket);
        parent.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await AddActivityAsync(parent.Id, "subticket-created", $"Added sub-ticket {ticket.TicketNumber}.", ticket.Title, cancellationToken);
        await AddActivityAsync(ticket.Id, "created", $"Created {ticket.TicketNumber} from parent {parent.TicketNumber}.", parent.Title, cancellationToken);

        if (ticket.Status == TicketStatus.Completed)
        {
            await EnsureTicketSummaryMemoryAsync(ticket.Id, cancellationToken);
        }

        return ticket.Id;
    }

    public async Task UpdateTicketAsync(Guid id, TicketEditorInput input, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("That ticket no longer exists.");

        var changes = new List<string>();
        TrackChange(changes, "title", ticket.Title, input.Title.Trim());
        TrackChange(changes, "description", ticket.Description, input.Description.Trim());
        TrackChange(changes, "status", MapStatusLabel(ticket.Status), MapStatusLabel(input.Status));
        TrackChange(changes, "priority", MapPriorityLabel(ticket.Priority), MapPriorityLabel(input.Priority));
        TrackChange(changes, "assignee", ticket.Assignee, input.Assignee.Trim());
        TrackChange(changes, "tags", ticket.TagsText, NormalizeTagText(input.TagsText));
        TrackChange(changes, "branch", ticket.GitBranch, input.GitBranch.Trim());
        TrackChange(changes, "git commit", ticket.GitCommit, input.HasGitCommit ? "Yes" : "No");

        var wasCompleted = ticket.Status == TicketStatus.Completed;
        ticket.Title = input.Title.Trim();
        ticket.Description = input.Description.Trim();
        ticket.Status = input.Status;
        ticket.Priority = input.Priority;
        ticket.Assignee = input.Assignee.Trim();
        ticket.TagsText = NormalizeTagText(input.TagsText);
        ticket.GitBranch = input.GitBranch.Trim();
        ticket.GitCommit = input.HasGitCommit ? "Yes" : "No";
        ticket.UpdatedUtc = DateTime.UtcNow;
        ticket.CompletedUtc = ticket.Status == TicketStatus.Completed ? ticket.CompletedUtc ?? ticket.UpdatedUtc : null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (changes.Count > 0)
        {
            await AddActivityAsync(ticket.Id, "updated", "Updated ticket details.", string.Join(Environment.NewLine, changes), cancellationToken);
        }

        if (ticket.Status == TicketStatus.Completed)
        {
            await EnsureTicketSummaryMemoryAsync(ticket.Id, cancellationToken);
            if (!wasCompleted)
            {
                await AddActivityAsync(ticket.Id, "completed", $"{ticket.TicketNumber} was completed.", string.Empty, cancellationToken);
            }
        }
        else if (wasCompleted)
        {
            await AddActivityAsync(ticket.Id, "reopened", $"{ticket.TicketNumber} moved out of completed.", MapStatusLabel(ticket.Status), cancellationToken);
        }
    }

    public async Task<Guid> AddNoteAsync(Guid ticketId, TicketNoteInput input, CancellationToken cancellationToken)
    {
        await EnsureTicketExistsAsync(ticketId, cancellationToken);

        var note = new TicketNoteEntry
        {
            TicketId = ticketId,
            Author = input.Author.Trim(),
            Content = input.Content.Trim(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        _dbContext.TicketNotes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(ticketId, "note-added", $"Added note by {note.Author}.", note.Content, cancellationToken);
        return note.Id;
    }

    public async Task UpdateNoteAsync(Guid ticketId, Guid noteId, TicketNoteInput input, CancellationToken cancellationToken)
    {
        var note = await _dbContext.TicketNotes.FirstOrDefaultAsync(x => x.TicketId == ticketId && x.Id == noteId, cancellationToken)
            ?? throw new InvalidOperationException("That note no longer exists.");

        note.Author = input.Author.Trim();
        note.Content = input.Content.Trim();
        note.UpdatedUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(ticketId, "note-updated", $"Updated note by {note.Author}.", note.Content, cancellationToken);
    }

    public async Task DeleteNoteAsync(Guid ticketId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _dbContext.TicketNotes.FirstOrDefaultAsync(x => x.TicketId == ticketId && x.Id == noteId, cancellationToken)
            ?? throw new InvalidOperationException("That note no longer exists.");

        _dbContext.TicketNotes.Remove(note);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(ticketId, "note-removed", $"Removed note by {note.Author}.", string.Empty, cancellationToken);
    }

    public async Task<Guid> LogTimeAsync(Guid ticketId, TicketTimeLogInput input, CancellationToken cancellationToken)
    {
        await EnsureTicketExistsAsync(ticketId, cancellationToken);

        var timeLog = new TicketTimeLogEntry
        {
            TicketId = ticketId,
            ModelName = input.ModelName.Trim(),
            Summary = input.Summary.Trim(),
            MinutesSpent = input.MinutesSpent,
            LoggedUtc = DateTime.SpecifyKind(input.LoggedUtc, DateTimeKind.Utc),
            CreatedUtc = DateTime.UtcNow
        };

        _dbContext.TicketTimeLogs.Add(timeLog);

        var ticket = await _dbContext.Tickets.FirstAsync(x => x.Id == ticketId, cancellationToken);
        ticket.UpdatedUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(ticketId, "time-logged", $"Logged {timeLog.MinutesSpent} minutes for {timeLog.ModelName}.", timeLog.Summary, cancellationToken);

        if (ticket.Status == TicketStatus.Completed)
        {
            await EnsureTicketSummaryMemoryAsync(ticket.Id, cancellationToken);
        }

        return timeLog.Id;
    }

    public async Task<int> GenerateSubTicketsAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .Include(x => x.SubTickets)
            .FirstOrDefaultAsync(x => x.Id == ticketId, cancellationToken)
            ?? throw new InvalidOperationException("That ticket no longer exists.");

        var candidateTitles = ExtractSubTicketCandidates(ticket.Description);
        if (candidateTitles.Count == 0)
        {
            throw new InvalidOperationException("Add bullet points or clear paragraph steps to the ticket description before generating subtickets.");
        }

        var existingTitles = ticket.SubTickets
            .Select(x => x.Title.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var nextSequence = await BuildNextTicketSequenceAsync(cancellationToken);
        var createdCount = 0;
        foreach (var title in candidateTitles)
        {
            if (!existingTitles.Add(title))
            {
                continue;
            }

            _dbContext.Tickets.Add(new TicketEntry
            {
                ParentTicketId = ticket.Id,
                TicketNumber = FormatTicketNumber(nextSequence++),
                Title = title,
                Description = $"Auto-generated from {ticket.TicketNumber}: {title}",
                Status = TicketStatus.New,
                Priority = ticket.Priority,
                Assignee = ticket.Assignee,
                TagsText = ticket.TagsText,
                GitBranch = ticket.GitBranch,
                GitCommit = ticket.GitCommit,
                CreatedUtc = now,
                UpdatedUtc = now
            });
            createdCount++;
        }

        if (createdCount == 0)
        {
            throw new InvalidOperationException("All generated subtickets already exist for this ticket.");
        }

        ticket.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddActivityAsync(ticket.Id, "subtickets-generated", $"Generated {createdCount} subtickets.", string.Join(Environment.NewLine, candidateTitles), cancellationToken);
        return createdCount;
    }

    private async Task EnsureTicketSummaryMemoryAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, cancellationToken)
            ?? throw new InvalidOperationException("That ticket no longer exists.");

        var subTickets = await _dbContext.Tickets
            .AsNoTracking()
            .Where(x => x.ParentTicketId == ticket.Id)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new { x.Id, x.TicketNumber, x.Title, x.Status, x.SummaryMemoryId })
            .ToListAsync(cancellationToken);

        var timeLogs = await _dbContext.TicketTimeLogs
            .AsNoTracking()
            .Where(x => x.TicketId == ticket.Id)
            .OrderBy(x => x.LoggedUtc)
            .ToListAsync(cancellationToken);

        var notes = await _dbContext.TicketNotes
            .AsNoTracking()
            .Where(x => x.TicketId == ticket.Id)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        var (wingId, roomId) = await EnsureTicketMemoryLocationAsync(cancellationToken);
        MemoryEntry memory;
        if (ticket.SummaryMemoryId.HasValue)
        {
            memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == ticket.SummaryMemoryId.Value, cancellationToken)
                ?? new MemoryEntry { CreatedUtc = DateTime.UtcNow };
            if (memory.Id == Guid.Empty)
            {
                memory.Id = Guid.NewGuid();
            }
            if (_dbContext.Entry(memory).State == EntityState.Detached)
            {
                _dbContext.Memories.Add(memory);
            }
        }
        else
        {
            memory = new MemoryEntry
            {
                CreatedUtc = DateTime.UtcNow
            };
            _dbContext.Memories.Add(memory);
        }

        var totalMinutes = timeLogs.Sum(x => x.MinutesSpent);
        var summary = $"{MapPriorityLabel(ticket.Priority)} {MapStatusLabel(ticket.Status)} ticket for {ticket.Assignee} with {subTickets.Count} subtickets and {totalMinutes} tracked minutes.";
        var content = new StringBuilder()
            .AppendLine($"Ticket: {ticket.TicketNumber}")
            .AppendLine($"Title: {ticket.Title}")
            .AppendLine($"Status: {MapStatusLabel(ticket.Status)}")
            .AppendLine($"Priority: {MapPriorityLabel(ticket.Priority)}")
            .AppendLine($"Assignee: {ticket.Assignee}")
            .AppendLine($"Git branch: {ticket.GitBranch}")
            .AppendLine($"Git commit: {(HasGitCommit(ticket.GitCommit) ? "Yes" : "No")}")
            .AppendLine($"Completed UTC: {ticket.CompletedUtc:O}")
            .AppendLine()
            .AppendLine("Description")
            .AppendLine(ticket.Description)
            .AppendLine();

        if (subTickets.Count > 0)
        {
            content.AppendLine("Subtickets");
            foreach (var subTicket in subTickets)
            {
                content.AppendLine($"- {subTicket.TicketNumber}: {subTicket.Title} ({MapStatusLabel(subTicket.Status)})");
            }
            content.AppendLine();
        }

        if (notes.Count > 0)
        {
            content.AppendLine("Latest notes");
            foreach (var note in notes)
            {
                content.AppendLine($"- {note.Author}: {note.Content}");
            }
            content.AppendLine();
        }

        if (timeLogs.Count > 0)
        {
            content.AppendLine("Time logs");
            foreach (var timeLog in timeLogs)
            {
                content.AppendLine($"- {timeLog.LoggedUtc:O} | {timeLog.ModelName} | {timeLog.MinutesSpent}m | {timeLog.Summary}");
            }
        }

        memory.Title = $"{ticket.TicketNumber} - {ticket.Title}";
        memory.Summary = summary.Length <= 500 ? summary : summary[..500];
        memory.Content = content.ToString().Trim();
        memory.Kind = MemoryKind.Task;
        memory.SourceKind = SourceKind.ManualNote;
        memory.SourceReference = ticket.TicketNumber;
        memory.Importance = ticket.Priority switch
        {
            TicketPriority.Critical => 5,
            TicketPriority.High => 4,
            TicketPriority.Medium => 3,
            _ => 2
        };
        memory.IsPinned = ticket.Priority >= TicketPriority.High;
        memory.WingId = wingId;
        memory.RoomId = roomId;
        memory.UpdatedUtc = DateTime.UtcNow;
        memory.OccurredUtc = ticket.CompletedUtc ?? DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (ticket.SummaryMemoryId != memory.Id)
        {
            ticket.SummaryMemoryId = memory.Id;
            ticket.UpdatedUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var tags = ParseTags(ticket.TagsText)
            .Append("ticketing")
            .Append(ticket.TicketNumber)
            .Append(ticket.Assignee)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await SyncMemoryTagsAsync(memory.Id, tags, cancellationToken);
        await SyncTicketMemoryLinksAsync(ticket, subTickets.Select(x => x.SummaryMemoryId).Where(x => x.HasValue).Select(x => x!.Value).ToArray(), cancellationToken);
    }

    private async Task<(Guid wingId, Guid roomId)> EnsureTicketMemoryLocationAsync(CancellationToken cancellationToken)
    {
        const string wingName = "Ticketing System";
        const string roomName = "Completed Tickets";
        var wingSlug = SlugUtility.CreateSlug(wingName);
        var roomSlug = SlugUtility.CreateSlug(roomName);

        var wing = await _dbContext.Wings.FirstOrDefaultAsync(x => x.Slug == wingSlug, cancellationToken);
        if (wing is null)
        {
            wing = new Wing
            {
                Name = wingName,
                Slug = wingSlug,
                Description = "Operational ticket records and implementation handoffs."
            };
            _dbContext.Wings.Add(wing);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var room = await _dbContext.Rooms.FirstOrDefaultAsync(x => x.WingId == wing.Id && x.Slug == roomSlug, cancellationToken);
        if (room is null)
        {
            room = new Room
            {
                WingId = wing.Id,
                Name = roomName,
                Slug = roomSlug,
                Description = "Auto-captured summaries for completed engineering tickets."
            };
            _dbContext.Rooms.Add(room);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return (wing.Id, room.Id);
    }

    private async Task SyncTicketMemoryLinksAsync(TicketEntry ticket, IReadOnlyCollection<Guid> childMemoryIds, CancellationToken cancellationToken)
    {
        if (!ticket.SummaryMemoryId.HasValue)
        {
            return;
        }

        if (ticket.ParentTicketId.HasValue)
        {
            var parentMemoryId = await _dbContext.Tickets
                .Where(x => x.Id == ticket.ParentTicketId.Value)
                .Select(x => x.SummaryMemoryId)
                .FirstOrDefaultAsync(cancellationToken);

            if (parentMemoryId.HasValue)
            {
                await EnsureMemoryLinkAsync(ticket.SummaryMemoryId.Value, parentMemoryId.Value, "subticket of", cancellationToken);
            }
        }

        foreach (var childMemoryId in childMemoryIds)
        {
            await EnsureMemoryLinkAsync(childMemoryId, ticket.SummaryMemoryId.Value, "subticket of", cancellationToken);
        }
    }

    private async Task EnsureMemoryLinkAsync(Guid fromMemoryId, Guid toMemoryId, string label, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.MemoryLinks.AnyAsync(
            x => x.FromMemoryEntryId == fromMemoryId && x.ToMemoryEntryId == toMemoryId && x.Label == label,
            cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.MemoryLinks.Add(new MemoryLink
        {
            FromMemoryEntryId = fromMemoryId,
            ToMemoryEntryId = toMemoryId,
            Label = label
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncMemoryTagsAsync(Guid memoryId, IReadOnlyCollection<string> tagNames, CancellationToken cancellationToken)
    {
        var normalizedTags = tagNames
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desiredSlugs = normalizedTags.Select(SlugUtility.CreateSlug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTags = await _dbContext.Tags
            .Where(x => desiredSlugs.Contains(x.Slug))
            .ToListAsync(cancellationToken);

        foreach (var tagName in normalizedTags)
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

        await _dbContext.SaveChangesAsync(cancellationToken);

        var existingMemoryTags = await _dbContext.MemoryTags
            .Where(x => x.MemoryEntryId == memoryId)
            .ToListAsync(cancellationToken);
        if (existingMemoryTags.Count > 0)
        {
            _dbContext.MemoryTags.RemoveRange(existingMemoryTags);
        }

        foreach (var tag in existingTags.Where(x => desiredSlugs.Contains(x.Slug)))
        {
            _dbContext.MemoryTags.Add(new MemoryEntryTag
            {
                MemoryEntryId = memoryId,
                TagId = tag.Id
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PalaceStatsViewModel> BuildStatsAsync(CancellationToken cancellationToken)
    {
        return new PalaceStatsViewModel
        {
            WingCount = await _dbContext.Wings.CountAsync(cancellationToken),
            RoomCount = await _dbContext.Rooms.CountAsync(cancellationToken),
            MemoryCount = await _dbContext.Memories.CountAsync(cancellationToken),
            PinnedCount = await _dbContext.Memories.CountAsync(x => x.IsPinned, cancellationToken),
            TagCount = await _dbContext.Tags.CountAsync(cancellationToken),
            OpenTodoCount = await _dbContext.Todos.CountAsync(x => x.Status != TodoStatus.Done, cancellationToken),
            CompletedTodoCount = await _dbContext.Todos.CountAsync(x => x.Status == TodoStatus.Done, cancellationToken),
            OpenTicketCount = await _dbContext.Tickets.CountAsync(x => x.Status != TicketStatus.Completed, cancellationToken),
            CompletedTicketCount = await _dbContext.Tickets.CountAsync(x => x.Status == TicketStatus.Completed, cancellationToken)
        };
    }

    private async Task EnsureTicketExistsAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Tickets.AnyAsync(x => x.Id == ticketId, cancellationToken))
        {
            throw new InvalidOperationException("That ticket no longer exists.");
        }
    }

    private async Task AddActivityAsync(Guid ticketId, string activityType, string message, string metadata, CancellationToken cancellationToken)
    {
        _dbContext.TicketActivities.Add(new TicketActivityEntry
        {
            TicketId = ticketId,
            ActivityType = activityType,
            Message = message,
            Metadata = metadata,
            CreatedUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> BuildNextTicketSequenceAsync(CancellationToken cancellationToken)
    {
        var existingNumbers = await _dbContext.Tickets
            .AsNoTracking()
            .Select(x => x.TicketNumber)
            .ToListAsync(cancellationToken);

        return existingNumbers
            .Select(ParseTicketSequence)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static string FormatTicketNumber(int sequence)
    {
        return $"TKT-{sequence:0000}";
    }

    private static int ParseTicketSequence(string ticketNumber)
    {
        var match = TicketNumberPattern.Match(ticketNumber);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? value
            : 0;
    }

    private static IReadOnlyCollection<string> ExtractSubTicketCandidates(string description)
    {
        var lines = description
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var bulletMatches = lines
            .Where(x => BulletPattern.IsMatch(x))
            .Select(x => BulletPattern.Replace(x, string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (bulletMatches.Length > 0)
        {
            return bulletMatches;
        }

        return description
            .Split([Environment.NewLine + Environment.NewLine, "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Replace(Environment.NewLine, " ").Replace("\n", " ").Trim())
            .Where(x => x.Length > 25)
            .Select(x => x.Length > 180 ? x[..180].TrimEnd() : x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    private static string NormalizeTagText(string tagsText)
    {
        return string.Join(", ", ParseTags(tagsText));
    }

    private static IReadOnlyCollection<string> ParseTags(string? tagsText)
    {
        return (tagsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void TrackChange(ICollection<string> changes, string fieldName, string previousValue, string nextValue)
    {
        if (string.Equals(previousValue.Trim(), nextValue.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{fieldName}: {NormalizeDiffValue(previousValue)} -> {NormalizeDiffValue(nextValue)}");
    }

    private static string NormalizeDiffValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    }

    private static bool HasGitCommit(string gitCommitValue)
    {
        return !string.IsNullOrWhiteSpace(gitCommitValue)
            && !string.Equals(gitCommitValue.Trim(), "No", StringComparison.OrdinalIgnoreCase);
    }

    private static TicketSummaryViewModel MapTicketSummary(TicketEntry ticket)
    {
        return new TicketSummaryViewModel
        {
            Id = ticket.Id,
            ParentTicketId = ticket.ParentTicketId,
            TicketNumber = ticket.TicketNumber,
            Title = ticket.Title,
            Description = ticket.Description,
            Status = ticket.Status,
            StatusLabel = MapStatusLabel(ticket.Status),
            Priority = ticket.Priority,
            PriorityLabel = MapPriorityLabel(ticket.Priority),
            Assignee = ticket.Assignee,
            Tags = ParseTags(ticket.TagsText).ToArray(),
            GitBranch = ticket.GitBranch,
            HasGitCommit = HasGitCommit(ticket.GitCommit),
            CreatedUtc = ticket.CreatedUtc,
            UpdatedUtc = ticket.UpdatedUtc,
            CompletedUtc = ticket.CompletedUtc,
            SubTicketCount = ticket.SubTickets.Count,
            CompletedSubTicketCount = ticket.SubTickets.Count(x => x.Status == TicketStatus.Completed),
            TotalMinutesSpent = ticket.TimeLogs.Sum(x => x.MinutesSpent)
        };
    }

    private static string MapStatusLabel(TicketStatus status)
    {
        return status switch
        {
            TicketStatus.New => "New",
            TicketStatus.InProgress => "In progress",
            TicketStatus.Blocked => "Blocked",
            TicketStatus.Completed => "Completed",
            _ => status.ToString()
        };
    }

    private static string MapPriorityLabel(TicketPriority priority)
    {
        return priority switch
        {
            TicketPriority.Low => "Low",
            TicketPriority.Medium => "Medium",
            TicketPriority.High => "High",
            TicketPriority.Critical => "Critical",
            _ => priority.ToString()
        };
    }
}
