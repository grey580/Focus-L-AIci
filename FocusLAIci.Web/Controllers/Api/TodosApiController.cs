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

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] TodoEditorInput input, CancellationToken cancellationToken)
    {
        var id = await _palaceService.CreateTodoAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Details), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<object>> Update(Guid id, [FromBody] TodoEditorInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.UpdateTodoAsync(id, input, cancellationToken);
            return Ok(new { id });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<object>> UpdateStatus(Guid id, [FromBody] TodoStatusUpdateInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.UpdateTodoStatusAsync(id, input.Status, cancellationToken);
            return Ok(new { id, status = input.Status.ToString() });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
