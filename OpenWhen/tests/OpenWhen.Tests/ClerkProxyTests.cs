using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenWhen.Api.Auth;

namespace OpenWhen.Tests;

public sealed class ClerkProxyTests
{
    private const string LiveKey = "pk_live_Y2xlcmsuc3RyZWFrLm1lLnVrJA";

    [Fact]
    public void Production_domain_talks_to_clerk_directly()
    {
        Assert.False(ClerkFrontendProxy.ShouldProxy("streak.me.uk", LiveKey));
        Assert.False(ClerkFrontendProxy.ShouldProxy("www.streak.me.uk", LiveKey));
        Assert.False(ClerkFrontendProxy.ShouldProxy("accounts.streak.me.uk", LiveKey));
        Assert.True(ClerkFrontendProxy.ShouldProxy("bitbots.onrender.com", LiveKey));
        Assert.True(ClerkFrontendProxy.ShouldProxy("localhost", LiveKey));
        Assert.False(ClerkFrontendProxy.ShouldProxy("bitbots.onrender.com", "pk_test_not-used"));
    }

    [Fact]
    public async Task Config_proxies_the_render_host_and_not_the_production_domain()
    {
        var previousKey = Environment.GetEnvironmentVariable(ClerkKeys.PublishableEnv);
        var previousSecret = Environment.GetEnvironmentVariable(ClerkKeys.SecretEnv);
        Environment.SetEnvironmentVariable(ClerkKeys.PublishableEnv, LiveKey);
        Environment.SetEnvironmentVariable(ClerkKeys.SecretEnv, "sk_live_test");
        try
        {
            await using var factory = new ApiFactory(createSchema: false);
            var client = factory.CreateClient();

            var render = await client.GetAsync("/api/config");
            var renderJson = await render.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, render.StatusCode);
            Assert.Contains("\"clerkProxyPath\":\"/__clerk\"", renderJson);

            var home = new HttpRequestMessage(HttpMethod.Get, "/api/config");
            home.Headers.Host = "streak.me.uk";
            var direct = await client.SendAsync(home);
            var directJson = await direct.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
            Assert.DoesNotContain("clerkProxyPath", directJson);

            Environment.SetEnvironmentVariable(ClerkKeys.SecretEnv, null);
            var missing = await client.GetAsync("/api/config");
            Assert.Equal(HttpStatusCode.InternalServerError, missing.StatusCode);
            Assert.Contains("CLERK_SECRET_KEY", await missing.Content.ReadAsStringAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClerkKeys.PublishableEnv, previousKey);
            Environment.SetEnvironmentVariable(ClerkKeys.SecretEnv, previousSecret);
        }
    }

    [Fact]
    public async Task Proxy_forwards_to_the_frontend_api_and_rewrites_redirects()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://frontend-api.clerk.dev/v1/oauth_callback?state=1") },
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        using var http = new HttpClient(handler);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("bitbots.onrender.com");
        context.Request.Path = "/__clerk/v1/client";
        context.Request.QueryString = new QueryString("?_clerk_js_version=6");
        context.Request.Headers["Clerk-Secret-Key"] = "sk_live_from_the_browser";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.8";
        context.Response.Body = new MemoryStream();

        await ClerkFrontendProxy.Forward(context, http, LiveKey, "sk_live_server");

        Assert.Equal("https://frontend-api.clerk.dev/v1/client?_clerk_js_version=6", handler.Request!.RequestUri!.ToString());
        Assert.Equal("sk_live_server", handler.Request.Headers.GetValues("Clerk-Secret-Key").Single());
        Assert.Equal("https://bitbots.onrender.com/__clerk", handler.Request.Headers.GetValues("Clerk-Proxy-Url").Single());
        Assert.Equal("203.0.113.8", handler.Request.Headers.GetValues("X-Forwarded-For").Single());
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("https://bitbots.onrender.com/__clerk/v1/oauth_callback?state=1", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Proxy_rejects_a_path_that_escapes_the_frontend_api()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("upstream should not be called"));
        using var http = new HttpClient(handler);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/__clerk//evil.example";
        context.Response.Body = new MemoryStream();

        await ClerkFrontendProxy.Forward(context, http, LiveKey, "sk_live_server");

        Assert.Null(handler.Request);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(responder(request));
        }
    }
}
