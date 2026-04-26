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
}
