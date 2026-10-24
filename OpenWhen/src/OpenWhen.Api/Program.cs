using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenWhen.Api.Auth;
using OpenWhen.Api.Data;
using OpenWhen.Api.Streaks;
using OpenWhen.Api.Time;

var builder = WebApplication.CreateBuilder(args);

var testing = builder.Environment.IsEnvironment("Testing");
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSingleton<IClock, SystemClock>();

if (!testing)
{
    var connectionString = DatabaseConfig.RequireConnectionString();
    builder.Services.AddDbContext<AppDb>(options => options.UseNpgsql(connectionString));
    builder.AddClerkAuthentication();
}

builder.Services.AddAuthorization();
builder.Services.AddScoped<StreakService>();

var app = builder.Build();

if (!testing)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/hello", () => Results.Ok(new { message = "hello" }));

app.MapGet("/api/config", () =>
{
    var key = Environment.GetEnvironmentVariable("CLERK_PUBLISHABLE_KEY");
    if (string.IsNullOrWhiteSpace(key))
        return Results.Problem("CLERK_PUBLISHABLE_KEY is not set.", statusCode: StatusCodes.Status500InternalServerError);
    return Results.Ok(new { clerkPublishableKey = key });
});

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", async (ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.GetMe(user, ct));
api.MapPut("/me", async (TimeZoneRequest body, ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.SetTimeZone(user, body, ct));
api.MapGet("/streaks", async (ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.List(user, ct));
api.MapPost("/streaks", async (CreateStreakRequest body, ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.Create(user, body, ct));
api.MapPost("/streaks/join", async (JoinStreakRequest body, ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.Join(user, body, ct));
api.MapGet("/streaks/{id:guid}", async (Guid id, ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.Get(user, id, ct));
api.MapPost("/streaks/{id:guid}/check-in", async (Guid id, ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.CheckIn(user, id, ct));
api.MapPost("/streaks/{id:guid}/messages", async (Guid id, LeaveMessageRequest body, ClaimsPrincipal user, StreakService streaks, CancellationToken ct) =>
    await streaks.LeaveMessage(user, id, body, ct));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
