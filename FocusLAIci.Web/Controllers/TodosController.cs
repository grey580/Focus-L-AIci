using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class TodosController : Controller
{
    private readonly PalaceService _palaceService;

    public TodosController(PalaceService palaceService)
    {
        _palaceService = palaceService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetTodoBoardAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await _palaceService.GetTodoDetailsAsync(id, markAsInProgress: true, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            TempData["TodoError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TodoEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var model = await _palaceService.GetTodoBoardAsync(cancellationToken);
            return View("Index", new TodoBoardViewModel
            {
                Stats = model.Stats,
                Input = input,
                InProgressTodos = model.InProgressTodos,
                PendingTodos = model.PendingTodos,
                BlockedTodos = model.BlockedTodos,
                DoneTodos = model.DoneTodos
            });
        }

        await _palaceService.CreateTodoAsync(input, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(Guid id, TodoStatus status, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.UpdateTodoStatusAsync(id, status, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException exception)
        {
            TempData["TodoError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [Bind(Prefix = "Input")] TodoEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            try
            {
                var model = await _palaceService.GetTodoDetailsAsync(id, markAsInProgress: false, cancellationToken);
                return View("Details", new TodoDetailsViewModel
                {
                    Todo = model.Todo,
                    Input = input
                });
            }
            catch (InvalidOperationException exception)
            {
                TempData["TodoError"] = exception.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        try
        {
            await _palaceService.UpdateTodoAsync(id, input, cancellationToken);
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["TodoError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.DeleteTodoAsync(id, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException exception)
        {
            TempData["TodoError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
