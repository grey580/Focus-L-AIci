using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed class SiteSettingsService
{
    private readonly FocusMemoryContext _dbContext;
    private SiteSettingsSnapshot? _cached;

    public SiteSettingsService(FocusMemoryContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SiteSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var settings = await _dbContext.SiteSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (settings is null)
        {
            settings = new SiteSettings();
            _dbContext.SiteSettings.Add(settings);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _cached = MapSnapshot(settings);
        return _cached;
    }

    public async Task<AdminSettingsViewModel> BuildAdminSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return new AdminSettingsViewModel
        {
            Input = new AdminSettingsInput
            {
                DisplayName = settings.DisplayName,
                HomeHeroCopy = settings.HomeHeroCopy,
                TimeZoneId = settings.TimeZoneId,
                ShowUtcTimestamps = settings.ShowUtcTimestamps,
                DefaultMemoryImportance = settings.DefaultMemoryImportance
            },
            TimeZoneOptions = BuildTimeZoneOptions(settings.TimeZoneId),
            ActiveTimeZoneLabel = ResolveTimeZone(settings.TimeZoneId).DisplayName
        };
    }

    public async Task SaveAsync(AdminSettingsInput input, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SiteSettings.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new SiteSettings();
        if (settings.Id != 1)
        {
            settings.Id = 1;
        }

        settings.DisplayName = input.DisplayName.Trim();
        settings.HomeHeroCopy = input.HomeHeroCopy.Trim();
        settings.TimeZoneId = ResolveTimeZone(input.TimeZoneId).Id;
        settings.ShowUtcTimestamps = input.ShowUtcTimestamps;
        settings.DefaultMemoryImportance = Math.Clamp(input.DefaultMemoryImportance, 1, 5);

        if (_dbContext.Entry(settings).State == EntityState.Detached)
        {
            _dbContext.SiteSettings.Add(settings);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _cached = MapSnapshot(settings);
    }

    public async Task<int> CleanupConcurrentTestWingsAsync(CancellationToken cancellationToken)
    {
        var wings = await _dbContext.Wings
            .Include(x => x.Rooms)
            .Include(x => x.Memories)
            .Where(x => x.Name == "Concurrent Wing" && x.Description == "race")
            .ToListAsync(cancellationToken);

        var removable = wings
            .Where(x => x.Rooms.Count == 0 && x.Memories.Count == 0)
            .ToList();

        if (removable.Count == 0)
        {
            return 0;
        }

        _dbContext.Wings.RemoveRange(removable);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return removable.Count;
    }

    public DateTime ConvertUtcToLocal(DateTime utcValue, SiteSettingsSnapshot settings)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcValue, DateTimeKind.Utc), ResolveTimeZone(settings.TimeZoneId));
    }

    public DateTime? ConvertUtcToLocal(DateTime? utcValue, SiteSettingsSnapshot settings)
    {
        return utcValue.HasValue ? ConvertUtcToLocal(utcValue.Value, settings) : null;
    }

    public DateTime? ConvertLocalToUtc(DateTime? localValue, SiteSettingsSnapshot settings)
    {
        if (!localValue.HasValue)
        {
            return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localValue.Value, DateTimeKind.Unspecified), ResolveTimeZone(settings.TimeZoneId));
    }

    public string FormatUtc(DateTime utcValue, SiteSettingsSnapshot settings)
    {
        var local = ConvertUtcToLocal(utcValue, settings);
        var suffix = settings.ShowUtcTimestamps ? $" ({utcValue:yyyy-MM-dd HH:mm} UTC)" : string.Empty;
        return $"{local:yyyy-MM-dd HH:mm} {settings.TimeZoneId}{suffix}";
    }

    public string FormatDateTimeLocal(DateTime? utcValue, SiteSettingsSnapshot settings)
    {
        return ConvertUtcToLocal(utcValue, settings)?.ToString("yyyy-MM-ddTHH:mm") ?? string.Empty;
    }

    public AdminSettingsViewModel Rebuild(AdminSettingsInput input)
    {
        var resolved = ResolveTimeZone(input.TimeZoneId);
        return new AdminSettingsViewModel
        {
            Input = input,
            TimeZoneOptions = BuildTimeZoneOptions(resolved.Id),
            ActiveTimeZoneLabel = resolved.DisplayName
        };
    }

    private static SiteSettingsSnapshot MapSnapshot(SiteSettings settings)
    {
        return new SiteSettingsSnapshot
        {
            DisplayName = settings.DisplayName,
            HomeHeroCopy = settings.HomeHeroCopy,
            TimeZoneId = settings.TimeZoneId,
            ShowUtcTimestamps = settings.ShowUtcTimestamps,
            DefaultMemoryImportance = Math.Clamp(settings.DefaultMemoryImportance, 1, 5)
        };
    }

    private static IReadOnlyCollection<SelectListItem> BuildTimeZoneOptions(string selectedTimeZoneId)
    {
        return TimeZoneInfo.GetSystemTimeZones()
            .Select(x => new SelectListItem($"{x.DisplayName} ({x.Id})", x.Id, string.Equals(x.Id, selectedTimeZoneId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
