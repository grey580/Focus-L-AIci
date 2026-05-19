using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class AgentsController(PalaceService palaceService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? query, ContextPackGoal? goal, bool supportsWriteActionsOnly, CancellationToken cancellationToken)
    {
        return View(await palaceService.GetAgentCatalogAsync(query, goal, supportsWriteActionsOnly, cancellationToken));
    }

    [HttpGet("/Agents/Agent/{slug}")]
    public async Task<IActionResult> Agent(string slug, CancellationToken cancellationToken)
    {
        if (!SlugUtility.IsNormalizedSlug(slug))
        {
            return NotFound();
        }

        var model = await palaceService.GetAgentAsync(slug, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("/Agents/Agent/{slug}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Agent(string slug, [Bind(Prefix = "RunInput")] AgentRunInput input, CancellationToken cancellationToken)
    {
        if (!SlugUtility.IsNormalizedSlug(slug))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await palaceService.GetAgentAsync(slug, cancellationToken);
            if (invalidModel is null)
            {
                return NotFound();
            }

            return View(new AgentDetailViewModel
            {
                Agent = invalidModel.Agent,
                Inputs = invalidModel.Inputs,
                Outputs = invalidModel.Outputs,
                SuggestedPrompt = invalidModel.SuggestedPrompt,
                SuggestedQuestion = invalidModel.SuggestedQuestion,
                RelatedContext = invalidModel.RelatedContext,
                CompanionSkills = invalidModel.CompanionSkills,
                RecommendedPeers = invalidModel.RecommendedPeers,
                RunInput = input
            });
        }

        var model = await palaceService.RunAgentAsync(slug, input, cancellationToken);
        return model is null ? NotFound() : View(model);
    }
}
