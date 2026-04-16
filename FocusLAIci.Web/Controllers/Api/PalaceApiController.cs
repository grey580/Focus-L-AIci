using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers.Api;

[ApiController]
[Route("api/palace")]
public sealed class PalaceApiController : ControllerBase
{
    private readonly PalaceService _palaceService;

    public PalaceApiController(PalaceService palaceService)
    {
        _palaceService = palaceService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PalaceApiSummaryViewModel>> Summary(CancellationToken cancellationToken)
    {
        return Ok(await _palaceService.GetApiSummaryAsync(cancellationToken));
    }

    [HttpGet("memories")]
    public async Task<ActionResult<IReadOnlyCollection<MemoryCardViewModel>>> Search(
        string? query,
        Guid? wingId,
        Guid? roomId,
        MemoryKind? kind,
        string? tag,
        CancellationToken cancellationToken)
    {
        return Ok(await _palaceService.SearchMemoriesAsync(query, wingId, roomId, kind, tag, cancellationToken));
    }

    [HttpGet("memories/{id:guid}")]
    public async Task<ActionResult<MemoryDetailViewModel>> Memory(Guid id, CancellationToken cancellationToken)
    {
        var model = await _palaceService.GetMemoryAsync(id, cancellationToken);
        return model is null ? NotFound() : Ok(model);
    }

    [HttpPost("memories")]
    public async Task<ActionResult<object>> CreateMemory([FromBody] MemoryEditorInput input, CancellationToken cancellationToken)
    {
        if (input.Id.HasValue)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(MemoryEditorInput.Id)] = ["Use PUT /api/palace/memories/{id} to update an existing memory."]
            }));
        }

        try
        {
            var id = await _palaceService.SaveMemoryAsync(input, cancellationToken);
            return CreatedAtAction(nameof(Memory), new { id }, new { id });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(MemoryEditorInput.RoomId)] = [exception.Message]
            }));
        }
    }

    [HttpPut("memories/{id:guid}")]
    public async Task<ActionResult<object>> UpdateMemory(Guid id, [FromBody] MemoryEditorInput input, CancellationToken cancellationToken)
    {
        input.Id = id;

        try
        {
            await _palaceService.SaveMemoryAsync(input, cancellationToken);
            return Ok(new { id });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(MemoryEditorInput.Id)] = [exception.Message]
            }));
        }
    }

    [HttpPost("wings")]
    public async Task<ActionResult<object>> CreateWing([FromBody] WingEditorInput input, CancellationToken cancellationToken)
    {
        var id = await _palaceService.CreateWingAsync(input, cancellationToken);
        return Ok(new { id });
    }

    [HttpPost("rooms")]
    public async Task<ActionResult<object>> CreateRoom([FromBody] RoomEditorInput input, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _palaceService.CreateRoomAsync(input, cancellationToken);
            return Ok(new { id });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(RoomEditorInput.WingId)] = [exception.Message]
            }));
        }
    }
}
