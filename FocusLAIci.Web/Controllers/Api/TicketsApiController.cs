using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers.Api;

[ApiController]
[Route("api/tickets")]
public sealed class TicketsApiController : ControllerBase
{
    private readonly TicketingService _ticketingService;

    public TicketsApiController(TicketingService ticketingService)
    {
        _ticketingService = ticketingService;
    }

    [HttpGet]
    public async Task<ActionResult<TicketBoardViewModel>> Board(string? completedSearch, int completedPage = 1, CancellationToken cancellationToken = default)
    {
        return Ok(await _ticketingService.GetBoardAsync(completedSearch, completedPage, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailsViewModel>> Details(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _ticketingService.GetDetailsAsync(id, cancellationToken));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] TicketEditorInput input, CancellationToken cancellationToken)
    {
        var id = await _ticketingService.CreateTicketAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Details), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<object>> Update(Guid id, [FromBody] TicketEditorInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _ticketingService.UpdateTicketAsync(id, input, cancellationToken);
            return Ok(new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<object>> UpdateStatus(Guid id, [FromBody] TicketStatusUpdateInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _ticketingService.UpdateTicketStatusAsync(id, input.Status, cancellationToken);
            return Ok(new { id, status = input.Status.ToString() });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult<object>> AddNote(Guid id, [FromBody] TicketNoteInput input, CancellationToken cancellationToken)
    {
        try
        {
            var noteId = await _ticketingService.AddNoteAsync(id, input, cancellationToken);
            return Ok(new { id, noteId });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/time-logs")]
    public async Task<ActionResult<object>> AddTimeLog(Guid id, [FromBody] TicketTimeLogInput input, CancellationToken cancellationToken)
    {
        try
        {
            var timeLogId = await _ticketingService.LogTimeAsync(id, input, cancellationToken);
            return Ok(new { id, timeLogId });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
