using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OpenWhen.Api.Auth;

public static class ClerkKeys
{
    public const string PublishableEnv = "CLERK_PUBLISHABLE_KEY";
    public const string SecretEnv = "CLERK_SECRET_KEY";

    public static string Issuer(string publishableKey)
    {
        var marker = publishableKey.LastIndexOf('_');
        if (marker < 0 || marker == publishableKey.Length - 1)
            throw new InvalidOperationException($"{PublishableEnv} is not a Clerk publishable key.");

        var encoded = publishableKey[(marker + 1)..].Replace('-', '+').Replace('_', '/');
        var padding = encoded.Length % 4;
        if (padding != 0)
            encoded = encoded.PadRight(encoded.Length + (4 - padding), '=');

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Trim().TrimEnd('$');
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{PublishableEnv} is not a Clerk publishable key.", ex);
        }

        if (string.IsNullOrWhiteSpace(decoded) || decoded.Contains('/') || decoded.Contains(' '))
            throw new InvalidOperationException($"{PublishableEnv} is not a Clerk publishable key.");

        return "https://" + decoded;
    }
}

public static class ClerkAuthExtensions
{
    public static void AddClerkAuthentication(this WebApplicationBuilder builder)
    {
        var publishable = Environment.GetEnvironmentVariable(ClerkKeys.PublishableEnv);
        var secret = Environment.GetEnvironmentVariable(ClerkKeys.SecretEnv);
        if (string.IsNullOrWhiteSpace(publishable) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException($"{ClerkKeys.PublishableEnv} and {ClerkKeys.SecretEnv} are required.");

        var issuer = ClerkKeys.Issuer(publishable);
        builder.Services.AddHttpClient<ClerkJwks>(client =>
        {
            client.BaseAddress = new Uri("https://api.clerk.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        builder.Services.AddSingleton(new ClerkIssuer(issuer));
        builder.Services.AddSingleton<IConfigureNamedOptions<JwtBearerOptions>, ClerkJwtOptionsSetup>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
    }
}

public sealed record ClerkIssuer(string Value);

public sealed class ClerkJwks(HttpClient http)
{
    private readonly object _gate = new();
    private IList<SecurityKey>? _keys;
    private DateTimeOffset _fetchedAt;

    public IList<SecurityKey> GetSigningKeys()
    {
        lock (_gate)
        {
            if (_keys is not null && DateTimeOffset.UtcNow - _fetchedAt < TimeSpan.FromHours(1))
                return _keys;

            using var response = http.GetAsync("v1/jwks").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var keys = new JsonWebKeySet(json).GetSigningKeys();
            if (keys.Count == 0)
                throw new InvalidOperationException("Clerk returned no signing keys.");
            _keys = keys;
            _fetchedAt = DateTimeOffset.UtcNow;
            return _keys;
        }
    }
}

public sealed class ClerkJwtOptionsSetup(ClerkJwks jwks, ClerkIssuer issuer) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
            return;

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer.Value,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(1),
            IssuerSigningKeyResolver = (_, _, _, _) => jwks.GetSigningKeys(),
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
