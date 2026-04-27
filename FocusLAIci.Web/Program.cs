using System.Text.Json.Serialization;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
const string DefaultLocalUrl = "http://127.0.0.1:5187";

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
builder.Services.AddSingleton<FocusDatabaseTargetService>();
builder.Services.AddDbContext<FocusMemoryContext>((serviceProvider, options) =>
    options.UseSqlite(serviceProvider.GetRequiredService<FocusDatabaseTargetService>().GetCurrentTarget().ConnectionString));
builder.Services.AddScoped<PalaceService>();
builder.Services.AddScoped<TicketingService>();
builder.Services.AddScoped<SiteSettingsService>();
builder.Services.AddScoped<CodeGraphService>();
builder.Services.AddScoped<ContextService>();

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
app.UseRouting();

app.UseAuthorization();

app.UseStaticFiles();

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
