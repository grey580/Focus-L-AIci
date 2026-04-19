using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class CodeGraphController : Controller
{
    private readonly CodeGraphService _codeGraphService;

    public CodeGraphController(CodeGraphService codeGraphService)
    {
        _codeGraphService = codeGraphService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _codeGraphService.GetBoardAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CodeGraphProjectInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var board = await _codeGraphService.GetBoardAsync(cancellationToken);
            return View(new CodeGraphBoardViewModel
            {
                Input = input,
                Projects = board.Projects
            });
        }

        try
        {
            var projectId = await _codeGraphService.CreateProjectAsync(input, cancellationToken);
            TempData["StatusMessage"] = "Code graph created and scanned.";
            return RedirectToAction(nameof(Project), new { id = projectId });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var board = await _codeGraphService.GetBoardAsync(cancellationToken);
            return View(new CodeGraphBoardViewModel
            {
                Input = input,
                Projects = board.Projects
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rescan(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _codeGraphService.RescanProjectAsync(id, cancellationToken);
            TempData["StatusMessage"] = "Code graph refreshed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Project), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Project(Guid id, string? query, Guid? selectedNodeId, CancellationToken cancellationToken)
    {
        var model = await _codeGraphService.GetProjectAsync(id, query, selectedNodeId, cancellationToken);
        return model is null ? NotFound() : View(model);
    }
}
