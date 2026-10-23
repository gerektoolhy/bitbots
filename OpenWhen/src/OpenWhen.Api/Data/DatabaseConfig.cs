using Npgsql;

namespace OpenWhen.Api.Data;

public static class DatabaseConfig
{
    public const string EnvVar = "DATABASE_URL";

    public static string RequireConnectionString()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"{EnvVar} is required.");
        return ToNpgsql(raw.Trim());
    }

    public static string ToNpgsql(string raw)
    {
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return raw;

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        };

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("sslmode", out var sslMode) && Enum.TryParse<SslMode>(sslMode, true, out var parsed))
            builder.SslMode = parsed;
        else if (!IsLocal(uri.Host))
            builder.SslMode = SslMode.Require;

        return builder.ConnectionString;
    }

    private static bool IsLocal(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split('=', 2);
            if (bits.Length == 2)
                result[Uri.UnescapeDataString(bits[0])] = Uri.UnescapeDataString(bits[1]);
        }
        return result;
    }
}
