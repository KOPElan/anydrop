using AnyDrop.Components;
using AnyDrop.Api;
using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
const string defaultDatabasePath = "data/anydrop.db";
var dbPath = builder.Configuration["Storage:DatabasePath"] ?? defaultDatabasePath;
var fullDbPath = Path.GetFullPath(dbPath);
var dbDirectory = Path.GetDirectoryName(fullDbPath);
if (!string.IsNullOrWhiteSpace(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<AnyDropDbContext>(options =>
    options.UseSqlite($"Data Source={fullDbPath}"));
builder.Services.AddSignalR();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<ITopicService, TopicService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
else
{
    app.UseSwagger(options => { options.RouteTemplate = "openapi/{documentName}.json"; });
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "AnyDrop API v1");
        options.RoutePrefix = "swagger";
    });
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<ShareHub>("/hubs/share");
app.MapShareItemEndpoints();
app.MapTopicEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();
    await db.MigrateWithCompatibilityAsync();
}

app.Run();
