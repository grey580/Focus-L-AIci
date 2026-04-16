using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class PalaceController : Controller
{
    private readonly PalaceService _palaceService;

    public PalaceController(PalaceService palaceService)
    {
        _palaceService = palaceService;
    }

    [HttpGet]
    public async Task<IActionResult> Explore(string? query, Guid? wingId, Guid? roomId, MemoryKind? kind, string? tag, CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetExploreAsync(query, wingId, roomId, kind, tag, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Wing(string slug, CancellationToken cancellationToken)
    {
        var model = await _palaceService.GetWingAsync(slug, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Memory(Guid id, CancellationToken cancellationToken)
    {
        var model = await _palaceService.GetMemoryAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> NewMemory(Guid? wingId, CancellationToken cancellationToken)
    {
        return View("EditMemory", await _palaceService.BuildMemoryEditorAsync(null, wingId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewMemory(MemoryEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var editor = await _palaceService.BuildMemoryEditorAsync(null, input.WingId, cancellationToken);
            return View("EditMemory", new MemoryEditorViewModel
            {
                Heading = "Add memory",
                SubmitLabel = "Add Memory",
                Input = input,
                WingOptions = editor.WingOptions,
                RoomOptions = editor.RoomOptions
            });
        }

        try
        {
            var id = await _palaceService.SaveMemoryAsync(input, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException exception)
        {
            var editor = await _palaceService.BuildMemoryEditorAsync(null, input.WingId, cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("EditMemory", new MemoryEditorViewModel
            {
                Heading = "Add memory",
                SubmitLabel = "Add Memory",
                Input = input,
                WingOptions = editor.WingOptions,
                RoomOptions = editor.RoomOptions
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditMemory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var model = await _palaceService.BuildMemoryEditorAsync(id, null, cancellationToken);
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
        if (!ModelState.IsValid)
        {
            var editor = await _palaceService.BuildMemoryEditorAsync(id, input.WingId, cancellationToken);
            return View(new MemoryEditorViewModel
            {
                Heading = editor.Heading,
                SubmitLabel = editor.SubmitLabel,
                Input = input,
                WingOptions = editor.WingOptions,
                RoomOptions = editor.RoomOptions
            });
        }

        try
        {
            await _palaceService.SaveMemoryAsync(input, cancellationToken);
            return RedirectToAction(nameof(Memory), new { id });
        }
        catch (InvalidOperationException exception)
        {
            var editor = await _palaceService.BuildMemoryEditorAsync(id, input.WingId, cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(new MemoryEditorViewModel
            {
                Heading = editor.Heading,
                SubmitLabel = editor.SubmitLabel,
                Input = input,
                WingOptions = editor.WingOptions,
                RoomOptions = editor.RoomOptions
            });
        }
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

        var wingId = await _palaceService.CreateWingAsync(input, cancellationToken);
        var wingSlug = await _palaceService.GetWingSlugAsync(wingId, cancellationToken);
        return RedirectToAction(nameof(Wing), new { slug = wingSlug });
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
