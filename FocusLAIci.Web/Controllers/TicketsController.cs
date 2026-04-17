using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class TicketsController : Controller
{
    private readonly TicketingService _ticketingService;

    public TicketsController(TicketingService ticketingService)
    {
        _ticketingService = ticketingService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _ticketingService.GetBoardAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "CreateInput")] TicketEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var model = await _ticketingService.GetBoardAsync(cancellationToken);
            return View("Index", new TicketBoardViewModel
            {
                Stats = model.Stats,
                CreateInput = input,
                NewTickets = model.NewTickets,
                InProgressTickets = model.InProgressTickets,
                BlockedTickets = model.BlockedTickets,
                CompletedTickets = model.CompletedTickets
            });
        }

        try
        {
            var id = await _ticketingService.CreateTicketAsync(input, cancellationToken);
            TempData["TicketMessage"] = "Ticket created.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await _ticketingService.GetDetailsAsync(id, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [Bind(Prefix = "EditInput")] TicketEditorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["TicketError"] = GetFirstModelError() ?? "Fix the highlighted ticket fields and try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _ticketingService.UpdateTicketAsync(id, input, cancellationToken);
            TempData["TicketMessage"] = "Ticket updated.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubTicket(Guid id, [Bind(Prefix = "SubTicketInput")] TicketSubTicketInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["TicketError"] = GetFirstModelError() ?? "Provide the sub-ticket details and try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _ticketingService.CreateSubTicketAsync(id, input, cancellationToken);
            TempData["TicketMessage"] = "Sub-ticket added.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateSubTickets(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var createdCount = await _ticketingService.GenerateSubTicketsAsync(id, cancellationToken);
            TempData["TicketMessage"] = $"Generated {createdCount} subtickets.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(Guid id, [Bind(Prefix = "NoteInput")] TicketNoteInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["TicketError"] = GetFirstModelError() ?? "Provide the note details and try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _ticketingService.AddNoteAsync(id, input, cancellationToken);
            TempData["TicketMessage"] = "Note added.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNote(Guid id, Guid noteId, TicketNoteInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["TicketError"] = GetFirstModelError() ?? "Provide the updated note details and try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _ticketingService.UpdateNoteAsync(id, noteId, input, cancellationToken);
            TempData["TicketMessage"] = "Note updated.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNote(Guid id, Guid noteId, CancellationToken cancellationToken)
    {
        try
        {
            await _ticketingService.DeleteNoteAsync(id, noteId, cancellationToken);
            TempData["TicketMessage"] = "Note removed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogTime(Guid id, [Bind(Prefix = "TimeLogInput")] TicketTimeLogInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["TicketError"] = GetFirstModelError() ?? "Provide the time log details and try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _ticketingService.LogTimeAsync(id, input, cancellationToken);
            TempData["TicketMessage"] = "Time logged.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["TicketError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private string? GetFirstModelError()
    {
        return ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }
}
