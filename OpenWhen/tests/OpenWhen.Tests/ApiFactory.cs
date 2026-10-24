using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenWhen.Api.Data;
using OpenWhen.Api.Time;

namespace OpenWhen.Tests;

public sealed class MutableClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly bool _createSchema;
    private bool _schemaReady;

    public MutableClock Clock { get; } = new();

    public ApiFactory(bool createSchema = true)
    {
        _createSchema = createSchema;
        _connection.Open();
    }

    public HttpClient CreateReadyClient()
    {
        var client = CreateClient();
        if (_createSchema && !_schemaReady)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Database.EnsureCreated();
            _schemaReady = true;
        }
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<AppDb>(options => options.UseSqlite(_connection));
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.Dispose();
        base.Dispose(disposing);
    }
}

public static class TestTokens
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("openwhen-test-signing-key");

    public static string Issue(string userId)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(userId)).TrimEnd('=');
        var sig = Sign(payload);
        return payload + "." + sig;
    }

    public static bool TryRead(string token, out string userId)
    {
        userId = "";
        var parts = token.Split('.');
        if (parts.Length != 2)
            return false;
        var expected = Sign(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
            return false;

        var payload = parts[0];
        var padding = payload.Length % 4;
        if (padding != 0)
            payload = payload.PadRight(payload.Length + (4 - padding), '=');
        userId = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return userId.Length > 0;
    }

    private static string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash).TrimEnd('=');
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("missing bearer"));

        if (!TestTokens.TryRead(header["Bearer ".Length..].Trim(), out var userId))
            return Task.FromResult(AuthenticateResult.Fail("bad token"));

        var identity = new ClaimsIdentity([new Claim("sub", userId)], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
