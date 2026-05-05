using FocusLAIci.Web.Models;
using FocusLAIci.Web.Security;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class ContextController : Controller
{
    private readonly ContextService _contextService;
    private readonly PalaceService _palaceService;

    public ContextController(ContextService contextService, PalaceService palaceService)
    {
        _contextService = contextService;
        _palaceService = palaceService;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLink(ContextLinkCreateInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = GetFirstModelError() ?? "Fix the invalid context link input and try again.";
            return ReturnToLocalOrHome(input.ReturnUrl);
        }

        try
        {
            await _contextService.AddLinkAsync(input, cancellationToken);
            TempData["StatusMessage"] = "Context link added.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return ReturnToLocalOrHome(input.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLink(ContextLinkDeleteInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _contextService.RemoveLinkAsync(input.LinkId, cancellationToken);
            TempData["StatusMessage"] = "Context link removed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        if (!string.IsNullOrWhiteSpace(input.ReturnUrl) && Url.IsLocalUrl(input.ReturnUrl))
        {
            return Redirect(input.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSuggestedLinks(ContextSuggestedLinksInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = GetFirstModelError() ?? "Fix the invalid suggested-link input and try again.";
            return ReturnToLocalOrHome(input.ReturnUrl);
        }

        try
        {
            var linkedCount = await _contextService.AddSuggestedLinksAsync(input, cancellationToken);
            TempData["StatusMessage"] = linkedCount == 0
                ? "No new suggested links were available."
                : $"Added {linkedCount} suggested context link{(linkedCount == 1 ? string.Empty : "s")}.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return ReturnToLocalOrHome(input.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePack(ContextBriefInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = GetFirstModelError() ?? "Provide a valid context question before saving a context pack.";
            return RedirectToAction("Index", "Home");
        }

        input = RequestInputPolicy.NormalizeBoundContextBriefInput(input);
        var pack = await _contextService.BuildContextPackAsync(input, cancellationToken);
        if (pack is null)
        {
            TempData["ErrorMessage"] = "Provide a question before saving a context pack.";
            return RedirectToAction("Index", "Home");
        }

        var memoryId = await _palaceService.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = $"Context pack: {TrimTitle(pack.Question, 120)}",
            Summary = pack.Summary,
            Content = pack.ExportText,
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.Research,
            SourceReference = "/",
            Importance = 3,
            TagsText = string.Join(", ", pack.SearchTokens.Take(5).Prepend("context-pack"))
        }, cancellationToken);

        TempData["StatusMessage"] = "Context pack saved as a memory.";
        return RedirectToAction("Memory", "Palace", new { id = memoryId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPack(ContextBriefInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = GetFirstModelError() ?? "Provide a valid context question before exporting a context pack.";
            return RedirectToAction("Index", "Home");
        }

        input = RequestInputPolicy.NormalizeBoundContextBriefInput(input);
        var pack = await _contextService.BuildContextPackAsync(input, cancellationToken);
        if (pack is null)
        {
            TempData["ErrorMessage"] = "Provide a question before exporting a context pack.";
            return RedirectToAction("Index", "Home");
        }

        var fileName = $"focus-context-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt";
        return File(System.Text.Encoding.UTF8.GetBytes(pack.ExportText), "text/plain; charset=utf-8", fileName);
    }

    private static string TrimTitle(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)].TrimEnd() + "...";
    }

    private IActionResult ReturnToLocalOrHome(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    private string? GetFirstModelError()
    {
        return ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }
}
