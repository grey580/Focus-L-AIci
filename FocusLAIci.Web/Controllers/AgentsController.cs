using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class AgentsController(FocusAgentCatalogService agentCatalogService) : Controller
{
    [HttpGet]
    public IActionResult Index(string? query)
    {
        return View(new AgentBrowseViewModel
        {
            Query = query?.Trim() ?? string.Empty,
            Agents = agentCatalogService.GetCatalog(query)
        });
    }

    [HttpGet("/Agents/Agent/{slug}")]
    public IActionResult Agent(string slug)
    {
        if (!SlugUtility.IsNormalizedSlug(slug))
        {
            return NotFound();
        }

        var model = agentCatalogService.GetAgent(slug);
        return model is null ? NotFound() : View(model);
    }
}
