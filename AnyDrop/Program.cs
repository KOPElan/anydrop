using AnyDrop.Components;
using AnyDrop.Api;
using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

var databasePath = builder.Configuration["Storage:DatabasePath"];
if (string.IsNullOrWhiteSpace(databasePath))
{
    throw new InvalidOperationException("Storage:DatabasePath is required.");
}

var normalizedDatabasePath = Path.GetFullPath(databasePath);
var databaseDirectory = Path.GetDirectoryName(normalizedDatabasePath);
if (databaseDirectory is not null)
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddDbContext<AnyDropDbContext>(options =>
{
    options.UseSqlite($"Data Source={normalizedDatabasePath}");
});

builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();
    // MVP phase uses EnsureCreated; switch to Database.MigrateAsync when migrations are introduced.
    await dbContext.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<ShareHub>("/hubs/share");
app.MapShareItemEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/v1.json");
}

app.Run();
