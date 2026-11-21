using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenWhen.Api.Auth;

namespace OpenWhen.Tests;

public sealed class ClerkAuthTests
{
    [Fact]
    public void Publishable_key_selects_the_frontend_api_jwks()
    {
        var domain = "example.clerk.accounts.dev$";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(domain)).TrimEnd('=');
        var issuer = ClerkKeys.Issuer($"pk_test_{encoded}");

        Assert.Equal("https://example.clerk.accounts.dev/.well-known/jwks.json", ClerkKeys.JwksUri(issuer).ToString());
    }

    [Fact]
    public async Task Session_token_without_an_audience_verifies_against_the_frontend_jwks()
    {
        using var rsa = RSA.Create(2048);
        const string kid = "ins_test";
        var handler = new JwksHandler(PublicJwks(rsa, kid));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://clerk.example/") };
        var tokens = new JsonWebTokenHandler();
        var token = tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://clerk.example",
            NotBefore = DateTime.UtcNow.AddSeconds(-10),
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user_123",
                ["azp"] = "https://streak.me.uk",
                ["sid"] = "sess_test",
                ["sts"] = "active",
                ["v"] = 2,
            },
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                ["cat"] = "cl_test",
                ["oiat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = kid }, SecurityAlgorithms.RsaSha256),
        });

        Assert.Empty(tokens.ReadJsonWebToken(token).Audiences);

        var options = new JwtBearerOptions();
        new ClerkJwtOptionsSetup(new ClerkJwks(http), new ClerkIssuer("https://clerk.example"))
            .Configure(JwtBearerDefaults.AuthenticationScheme, options);
        tokens.MapInboundClaims = options.MapInboundClaims;

        var result = await tokens.ValidateTokenAsync(token, options.TokenValidationParameters);

        Assert.True(result.IsValid, result.Exception?.ToString());
        Assert.Equal("user_123", result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.Equal("/.well-known/jwks.json", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Null(handler.Request.Headers.Authorization);
    }

    [Fact]
    public async Task Session_token_from_another_key_is_rejected()
    {
        using var published = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var handler = new JwksHandler(PublicJwks(published, "ins_test"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://clerk.example/") };
        var tokens = new JsonWebTokenHandler();
        var token = tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://clerk.example",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "user_123" },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(other) { KeyId = "ins_other" }, SecurityAlgorithms.RsaSha256),
        });

        var options = new JwtBearerOptions();
        new ClerkJwtOptionsSetup(new ClerkJwks(http), new ClerkIssuer("https://clerk.example"))
            .Configure(JwtBearerDefaults.AuthenticationScheme, options);

        var result = await tokens.ValidateTokenAsync(token, options.TokenValidationParameters);

        Assert.False(result.IsValid);
    }

    private static string PublicJwks(RSA rsa, string kid)
    {
        var parameters = rsa.ExportParameters(false);
        var n = Base64UrlEncoder.Encode(parameters.Modulus);
        var e = Base64UrlEncoder.Encode(parameters.Exponent);
        return "{\"keys\":[{\"kty\":\"RSA\",\"use\":\"sig\",\"alg\":\"RS256\",\"kid\":\"" + kid + "\",\"n\":\"" + n + "\",\"e\":\"" + e + "\"}]}";
    }

    private sealed class JwksHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
