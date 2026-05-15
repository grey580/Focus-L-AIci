using System.Text.Json.Serialization;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Services;
using FocusLAIci.Web.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ResolveContentRoot()
});
const string DefaultLocalUrl = "http://127.0.0.1:5191";

if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls(DefaultLocalUrl);
}

// Add services to the container.
builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "focus-antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSingleton(serviceProvider => new LocalPathPolicy(serviceProvider.GetRequiredService<IHostEnvironment>()));
builder.Services.AddSingleton<FocusDatabaseTargetService>();
builder.Services.AddDbContext<FocusMemoryContext>((serviceProvider, options) =>
    options.UseSqlite(serviceProvider.GetRequiredService<FocusDatabaseTargetService>().GetCurrentTarget().ConnectionString));
builder.Services.AddScoped<PalaceService>();
builder.Services.AddScoped<TicketingService>();
builder.Services.AddScoped<SiteSettingsService>();
builder.Services.AddScoped<CodeGraphService>();
builder.Services.AddScoped<ContextService>();
builder.Services.AddSingleton<FocusAgentCatalogService>();
builder.Services.AddSingleton<FocusMcpSessionService>();
builder.Services.AddSingleton<FocusMcpEventBus>();
builder.Services.AddSingleton<IFocusEventPublisher>(serviceProvider => serviceProvider.GetRequiredService<FocusMcpEventBus>());
builder.Services.AddSingleton<FocusMcpAuthService>();
builder.Services.AddSingleton<FocusMcpToolRegistry>();
builder.Services.AddSingleton<FocusMcpResourceRegistry>();
builder.Services.AddSingleton<FocusDiagnosticsService>();

var app = builder.Build();

await app.Services.GetRequiredService<FocusDatabaseTargetService>().EnsureCurrentDatabaseReadyAsync();

var seedDemoData = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("FocusPalace:SeedDemoData");
if (seedDemoData)
{
    await MemorySeeder.SeedSampleDataAsync(app.Services);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ApiWriteOriginGuardMiddleware>();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "palace-wing",
    pattern: "Palace/Wing/{slug}",
    defaults: new { controller = "Palace", action = "Wing" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features
        .Get<IServerAddressesFeature>()?
        .Addresses?
        .ToArray()
        ?? app.Urls.ToArray();

    if (addresses.Length == 0)
    {
        addresses = [DefaultLocalUrl];
    }

    app.Logger.LogInformation("Focus L-AIci is ready. Open {Addresses}", string.Join(", ", addresses));
    Console.WriteLine();
    Console.WriteLine("Focus L-AIci is ready.");
    foreach (var address in addresses)
    {
        Console.WriteLine($"Open: {address}");
    }
    Console.WriteLine();
});

app.Run();

static string ResolveContentRoot()
{
    foreach (var candidate in GetContentRootCandidates())
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            continue;
        }

        var fullPath = Path.GetFullPath(candidate);
        if (Directory.Exists(Path.Combine(fullPath, "wwwroot")))
        {
            return fullPath;
        }
    }

    return Directory.GetCurrentDirectory();
}

static IEnumerable<string> GetContentRootCandidates()
{
    yield return Directory.GetCurrentDirectory();
    yield return AppContext.BaseDirectory;
    yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
}
