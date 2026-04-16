using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class AdminController : Controller
{
    private readonly SiteSettingsService _siteSettingsService;

    public AdminController(SiteSettingsService siteSettingsService)
    {
        _siteSettingsService = siteSettingsService;
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        return View(await _siteSettingsService.BuildAdminSettingsAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(AdminSettingsInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(_siteSettingsService.Rebuild(input));
        }

        await _siteSettingsService.SaveAsync(input, cancellationToken);
        TempData["SettingsMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CleanupConcurrentWings(CancellationToken cancellationToken)
    {
        var removedCount = await _siteSettingsService.CleanupConcurrentTestWingsAsync(cancellationToken);
        TempData["SettingsMessage"] = removedCount == 0
            ? "No stray Concurrent Wing test entries were found."
            : $"Removed {removedCount} stray Concurrent Wing test entr{(removedCount == 1 ? "y" : "ies")}.";
        return RedirectToAction(nameof(Settings));
    }
}
