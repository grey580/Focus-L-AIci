using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class PalaceController : Controller
{
    private readonly PalaceService _palaceService;
    private readonly SiteSettingsService _siteSettingsService;

    public PalaceController(PalaceService palaceService, SiteSettingsService siteSettingsService)
    {
        _palaceService = palaceService;
        _siteSettingsService = siteSettingsService;
    }

    [HttpGet]
    public async Task<IActionResult> Explore(string? query, Guid? wingId, Guid? roomId, MemoryKind? kind, string? tag, CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetExploreAsync(query, wingId, roomId, kind, tag, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Wings(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetWingCatalogAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Rooms(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetRoomCatalogAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Tags(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetTagCatalogAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Visualizer(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetVisualizerAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Wing(string slug, string? room, CancellationToken cancellationToken)
    {
        if (!SlugUtility.IsNormalizedSlug(slug)
            || (!string.IsNullOrWhiteSpace(room) && !SlugUtility.IsNormalizedSlug(room)))
        {
            return NotFound();
        }

        var model = await _palaceService.GetWingAsync(slug, room, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Memory(Guid id, CancellationToken cancellationToken)
    {
        var model = await _palaceService.GetMemoryAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyMemory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.MarkMemoryVerifiedAsync(id, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkMemoryNeedsReview(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.MarkMemoryNeedsReviewAsync(id, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ArchiveMemory(Guid id, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.ArchiveMemoryAsync(id, reason, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreMemory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.MarkMemoryActiveAsync(id, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupersedeMemory(Guid id, MemorySupersedeInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.SupersedeMemoryAsync(id, input.ReplacementMemoryId, input.Reason, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpGet]
    public async Task<IActionResult> NewMemory(Guid? wingId, CancellationToken cancellationToken)
    {
        var model = await _palaceService.BuildMemoryEditorAsync(null, wingId, cancellationToken);
        var settings = await _siteSettingsService.GetSettingsAsync(cancellationToken);
        model.Input.Importance = settings.DefaultMemoryImportance;
        return View("EditMemory", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewMemory(MemoryEditorInput input, CancellationToken cancellationToken)
    {
        var settings = await _siteSettingsService.GetSettingsAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return View("EditMemory", await BuildDraftEditorAsync(null, input, cancellationToken));
        }

        try
        {
            input.OccurredUtc = _siteSettingsService.ConvertLocalToUtc(input.OccurredUtc, settings);
            var id = await _palaceService.SaveMemoryAsync(input, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("EditMemory", await BuildDraftEditorAsync(null, input, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditMemory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var model = await _palaceService.BuildMemoryEditorAsync(id, null, cancellationToken);
            var settings = await _siteSettingsService.GetSettingsAsync(cancellationToken);
            model.Input.OccurredUtc = _siteSettingsService.ConvertUtcToLocal(model.Input.OccurredUtc, settings);
            return View(model);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMemory(Guid id, MemoryEditorInput input, CancellationToken cancellationToken)
    {
        input.Id = id;
        var settings = await _siteSettingsService.GetSettingsAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return View(await BuildDraftEditorAsync(id, input, cancellationToken));
        }

        try
        {
            input.OccurredUtc = _siteSettingsService.ConvertLocalToUtc(input.OccurredUtc, settings);
            await _palaceService.SaveMemoryAsync(input, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(await BuildDraftEditorAsync(id, input, cancellationToken));
        }
    }

    private async Task<MemoryEditorViewModel> BuildDraftEditorAsync(Guid? id, MemoryEditorInput input, CancellationToken cancellationToken)
    {
        var editor = await _palaceService.BuildMemoryEditorAsync(id, input.WingId, cancellationToken);
        return new MemoryEditorViewModel
        {
            Heading = editor.Heading,
            SubmitLabel = editor.SubmitLabel,
            Input = input,
            WingOptions = editor.WingOptions,
            RoomOptions = editor.RoomOptions,
            SuggestedTags = _palaceService.SuggestTagsForDraft(input),
            DuplicateSuggestions = await _palaceService.FindDuplicateSuggestionsAsync(input, cancellationToken)
        };
    }

    [HttpGet]
    public IActionResult NewWing()
    {
        return View(new WingEditorInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewWing(WingEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        try
        {
            var wingId = await _palaceService.CreateWingAsync(input, cancellationToken);
            var wingSlug = await _palaceService.GetWingSlugAsync(wingId, cancellationToken);
            return RedirectToAction(nameof(Wing), new { slug = wingSlug });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(input);
        }
    }

    [HttpGet]
    public async Task<IActionResult> NewRoom(CancellationToken cancellationToken)
    {
        return View(await _palaceService.BuildRoomEditorAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewRoom(RoomEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var editor = await _palaceService.BuildRoomEditorAsync(cancellationToken);
            return View(new RoomEditorViewModel
            {
                Input = input,
                WingOptions = editor.WingOptions
            });
        }

        try
        {
            await _palaceService.CreateRoomAsync(input, cancellationToken);
            var wingSlug = await _palaceService.GetWingSlugAsync(input.WingId, cancellationToken);
            return RedirectToAction(nameof(Wing), new { slug = wingSlug });
        }
        catch (InvalidOperationException exception)
        {
            var editor = await _palaceService.BuildRoomEditorAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(new RoomEditorViewModel
            {
                Input = input,
                WingOptions = editor.WingOptions
            });
        }
    }
}
