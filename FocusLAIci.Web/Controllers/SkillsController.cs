using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class SkillsController : Controller
{
    private readonly PalaceService _palaceService;

    public SkillsController(PalaceService palaceService)
    {
        _palaceService = palaceService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? query, SkillCategory? category, Guid? wingId, bool pinnedOnly, bool needsReviewOnly, CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetSkillCatalogAsync(query, category, wingId, pinnedOnly, needsReviewOnly, cancellationToken));
    }

    [HttpGet("/Skills/Skill/{slug}")]
    public async Task<IActionResult> Skill(string slug, CancellationToken cancellationToken)
    {
        if (!SlugUtility.IsNormalizedSlug(slug))
        {
            return NotFound();
        }

        var model = await _palaceService.GetSkillAsync(slug, cancellationToken, trackUsage: true);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> NewSkill(CancellationToken cancellationToken)
    {
        return View("EditSkill", await _palaceService.BuildSkillEditorAsync(null, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewSkill(SkillEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("EditSkill", await BuildDraftEditorAsync(null, input, cancellationToken));
        }

        try
        {
            var id = await _palaceService.SaveSkillAsync(input, cancellationToken);
            var editor = await _palaceService.BuildSkillEditorAsync(id, cancellationToken);
            return RedirectToAction(nameof(Skill), new { slug = SlugUtility.CreateSlug(editor.Input.Name) });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("EditSkill", await BuildDraftEditorAsync(null, input, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditSkill(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await _palaceService.BuildSkillEditorAsync(id, cancellationToken));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditSkill(Guid id, SkillEditorInput input, CancellationToken cancellationToken)
    {
        input.Id = id;
        if (!ModelState.IsValid)
        {
            return View(await BuildDraftEditorAsync(id, input, cancellationToken));
        }

        try
        {
            await _palaceService.SaveSkillAsync(input, cancellationToken);
            return RedirectToAction(nameof(Skill), new { slug = SlugUtility.CreateSlug(input.Name) });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(await BuildDraftEditorAsync(id, input, cancellationToken));
        }
    }

    private async Task<SkillEditorViewModel> BuildDraftEditorAsync(Guid? id, SkillEditorInput input, CancellationToken cancellationToken)
    {
        var editor = await _palaceService.BuildSkillEditorAsync(id, cancellationToken);
        return new SkillEditorViewModel
        {
            Heading = editor.Heading,
            SubmitLabel = editor.SubmitLabel,
            Input = input,
            WingOptions = editor.WingOptions
        };
    }
}
