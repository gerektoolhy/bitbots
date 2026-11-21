using System.Net.Http.Headers;

namespace OpenWhen.Api.Auth;

public static class ClerkFrontendProxy
{
    public const string Path = "/__clerk";
    public const string HttpClientName = "clerk-frontend-proxy";
    public const string FrontendApi = "https://frontend-api.clerk.dev";

    private static readonly string[] HopByHop =
    [
        "connection",
        "keep-alive",
        "proxy-authenticate",
        "proxy-authorization",
        "te",
        "trailer",
        "transfer-encoding",
        "upgrade",
        "host",
        "content-length",
    ];

    public static bool ShouldProxy(string? host, string publishableKey)
    {
        if (string.IsNullOrWhiteSpace(host) || !publishableKey.StartsWith("pk_live_", StringComparison.Ordinal))
            return false;
        if (!TryApplicationDomain(publishableKey, out var domain))
            return false;

        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        var port = host.LastIndexOf(':');
        if (port > 0)
            host = host[..port];

        return host != domain && !host.EndsWith("." + domain, StringComparison.Ordinal);
    }

    public static async Task Forward(HttpContext context, HttpClient http)
    {
        var publishable = Environment.GetEnvironmentVariable(ClerkKeys.PublishableEnv);
        var secret = Environment.GetEnvironmentVariable(ClerkKeys.SecretEnv);
        if (string.IsNullOrWhiteSpace(publishable) || string.IsNullOrWhiteSpace(secret))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Clerk proxy is not configured." });
            return;
        }

        await Forward(context, http, publishable, secret);
    }

    public static async Task Forward(HttpContext context, HttpClient http, string publishableKey, string secret)
    {
        if (!TryTarget(context.Request.Path.Value, context.Request.QueryString.Value, out var target))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var origin = $"{context.Request.Scheme}://{context.Request.Host.Value}";
        using var outbound = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Request.ContentLength is > 0 || HasBody(context.Request.Method))
            outbound.Content = new StreamContent(context.Request.Body);
        CopyRequestHeaders(context.Request, outbound);

        outbound.Headers.Remove("Clerk-Secret-Key");
        outbound.Headers.TryAddWithoutValidation("Clerk-Secret-Key", secret);
        outbound.Headers.Remove("Clerk-Proxy-Url");
        outbound.Headers.TryAddWithoutValidation("Clerk-Proxy-Url", origin + Path);
        outbound.Headers.Remove("Accept-Encoding");
        outbound.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

        var clientIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            outbound.Headers.Remove("X-Forwarded-For");
            outbound.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        }

        using var response = await http.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response, context.Response, origin + Path);
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    public static bool TryApplicationDomain(string publishableKey, out string domain)
    {
        domain = "";
        string issuer;
        try
        {
            issuer = ClerkKeys.Issuer(publishableKey);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var frontend = new Uri(issuer).Host;
        const string prefix = "clerk.";
        if (!frontend.StartsWith(prefix, StringComparison.Ordinal) || frontend.EndsWith(".accounts.dev", StringComparison.Ordinal))
            return false;

        domain = frontend[prefix.Length..];
        return domain.Contains('.');
    }

    private static bool TryTarget(string? path, string? query, out Uri target)
    {
        target = null!;
        if (string.IsNullOrEmpty(path) || !path.StartsWith(Path, StringComparison.Ordinal))
            return false;

        var rest = path[Path.Length..];
        if (rest.Contains('\\') || rest.Contains("//") || rest.Split('/').Contains(".."))
            return false;
        if (rest.Length > 0 && !rest.StartsWith('/'))
            return false;

        var combined = FrontendApi + (rest.Length == 0 ? "/" : rest) + (query ?? "");
        if (!Uri.TryCreate(combined, UriKind.Absolute, out var uri) || uri.Host != "frontend-api.clerk.dev")
            return false;

        target = uri;
        return true;
    }

    private static bool HasBody(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method);

    private static void CopyRequestHeaders(HttpRequest request, HttpRequestMessage outbound)
    {
        foreach (var header in request.Headers)
        {
            if (IsHopByHop(header.Key))
                continue;
            if (outbound.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value) || outbound.Content is null)
                continue;
            outbound.Content.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage response, HttpResponse destination, string proxyUrl)
    {
        Copy(response.Headers);
        Copy(response.Content.Headers);
        if (!destination.Headers.TryGetValue("Location", out var values))
            return;
        var location = values.ToString();
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || !uri.Host.Equals("frontend-api.clerk.dev", StringComparison.OrdinalIgnoreCase))
            return;
        destination.Headers["Location"] = proxyUrl + uri.PathAndQuery + uri.Fragment;
        return;

        void Copy(HttpHeaders headers)
        {
            foreach (var header in headers)
            {
                if (IsHopByHop(header.Key) || header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var value in header.Value)
                    destination.Headers.Append(header.Key, value);
            }
        }
    }

    private static bool IsHopByHop(string name)
    {
        foreach (var header in HopByHop)
        {
            if (name.Equals(header, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
