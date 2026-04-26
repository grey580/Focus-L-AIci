using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers.Api;

[ApiController]
[Route("api/context")]
public sealed class ContextApiController : ControllerBase
{
    private readonly ContextService _contextService;

    public ContextApiController(ContextService contextService)
    {
        _contextService = contextService;
    }

    [HttpPost("brief")]
    public async Task<ActionResult<ContextPackViewModel>> Brief([FromBody] ContextBriefInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var pack = await _contextService.BuildContextPackAsync(input, cancellationToken);
        return pack is null ? BadRequest("Provide a question to build a context pack.") : Ok(pack);
    }
}
