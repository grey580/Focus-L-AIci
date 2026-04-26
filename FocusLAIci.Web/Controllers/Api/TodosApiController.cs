using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers.Api;

[ApiController]
[Route("api/todos")]
public sealed class TodosApiController : ControllerBase
{
    private readonly PalaceService _palaceService;

    public TodosApiController(PalaceService palaceService)
    {
        _palaceService = palaceService;
    }

    [HttpGet]
    public async Task<ActionResult<TodoBoardViewModel>> Board(CancellationToken cancellationToken)
    {
        return Ok(await _palaceService.GetTodoBoardAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TodoDetailsViewModel>> Details(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _palaceService.GetTodoDetailsAsync(id, cancellationToken));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
