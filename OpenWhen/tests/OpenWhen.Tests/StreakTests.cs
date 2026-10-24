using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenWhen.Api.Auth;
using OpenWhen.Api.Data;

namespace OpenWhen.Tests;

public sealed class StreakTests
{
    private const string Note = "sealed-note-body";

    [Fact]
    public async Task Health_returns_ok_without_a_database()
    {
        await using var factory = new ApiFactory(createSchema: false);
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", json);
    }

    [Fact]
    public async Task Api_rejects_calls_without_a_valid_token()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateReadyClient();

        var missing = await client.PostAsJsonAsync("/api/streaks", new { habitName = "Read" });
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-signed-token");
        var bogus = await client.PostAsJsonAsync("/api/streaks", new { habitName = "Read" });
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task Check_in_continues_ignores_same_day_and_resets_after_a_missed_day()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateReadyClient();
        As(client, "ada");

        var created = await PostOk(client, "/api/streaks", new { habitName = "Read", targetDays = 7 });
        var id = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("joinCode").GetString()!.Length >= 32);

        factory.Clock.UtcNow = Local(2026, 6, 1);
        var first = await PostOk(client, $"/api/streaks/{id}/check-in", new { });
        Assert.Equal(1, first.GetProperty("currentStreak").GetInt32());
        Assert.True(first.GetProperty("counted").GetBoolean());

        factory.Clock.UtcNow = Local(2026, 6, 1, 20);
        var sameDay = await PostOk(client, $"/api/streaks/{id}/check-in", new { });
        Assert.Equal(1, sameDay.GetProperty("currentStreak").GetInt32());
        Assert.False(sameDay.GetProperty("counted").GetBoolean());

        factory.Clock.UtcNow = Local(2026, 6, 2);
        var nextDay = await PostOk(client, $"/api/streaks/{id}/check-in", new { });
        Assert.Equal(2, nextDay.GetProperty("currentStreak").GetInt32());
        Assert.True(nextDay.GetProperty("counted").GetBoolean());

        factory.Clock.UtcNow = Local(2026, 6, 4);
        var afterMiss = await PostOk(client, $"/api/streaks/{id}/check-in", new { });
        Assert.Equal(1, afterMiss.GetProperty("currentStreak").GetInt32());
        Assert.True(afterMiss.GetProperty("counted").GetBoolean());
    }

    [Fact]
    public async Task Message_stays_hidden_until_the_goal_and_stays_open_after_a_reset()
    {
        await using var factory = new ApiFactory();
        var ada = factory.CreateReadyClient();
        var ben = factory.CreateReadyClient();
        var cy = factory.CreateReadyClient();
        As(ada, "ada");
        As(ben, "ben");
        As(cy, "cy");

        factory.Clock.UtcNow = Local(2026, 6, 1);
        var created = await PostOk(ada, "/api/streaks", new { habitName = "Stretch", targetDays = 2 });
        var id = created.GetProperty("id").GetGuid();
        var code = created.GetProperty("joinCode").GetString()!;

        var joined = await PostOk(ben, "/api/streaks/join", new { code });
        Assert.False(joined.TryGetProperty("messages", out _));
        Assert.DoesNotContain(Note, joined.GetRawText());

        await PostOk(ada, $"/api/streaks/{id}/messages", new { toUserId = "ben", body = Note });

        var locked = await GetOk(ben, $"/api/streaks/{id}");
        Assert.DoesNotContain(Note, locked.GetRawText());
        var waiting = Incoming(locked);
        Assert.True(waiting.GetProperty("locked").GetBoolean());
        Assert.False(waiting.TryGetProperty("body", out _));

        await PostOk(ben, $"/api/streaks/{id}/check-in", new { });
        var stillLocked = await GetOk(ben, $"/api/streaks/{id}");
        Assert.Equal(1, stillLocked.GetProperty("currentStreak").GetInt32());
        Assert.DoesNotContain(Note, stillLocked.GetRawText());

        factory.Clock.UtcNow = Local(2026, 6, 2);
        var reached = await PostOk(ben, $"/api/streaks/{id}/check-in", new { });
        Assert.Equal(2, reached.GetProperty("currentStreak").GetInt32());

        var open = await GetOk(ben, $"/api/streaks/{id}");
        Assert.Equal(Note, Incoming(open).GetProperty("body").GetString());

        var senderView = await GetOk(ada, $"/api/streaks/{id}");
        Assert.DoesNotContain(Note, senderView.GetRawText());
        Assert.Equal("out", senderView.GetProperty("messages")[0].GetProperty("direction").GetString());

        var cyJoin = await PostOk(cy, "/api/streaks/join", new { code });
        Assert.DoesNotContain(Note, cyJoin.GetRawText());
        var cyView = await GetOk(cy, $"/api/streaks/{id}");
        Assert.DoesNotContain(Note, cyView.GetRawText());

        factory.Clock.UtcNow = Local(2026, 6, 4);
        var reset = await PostOk(ben, $"/api/streaks/{id}/check-in", new { });
        Assert.Equal(1, reset.GetProperty("currentStreak").GetInt32());

        var stillOpen = await GetOk(ben, $"/api/streaks/{id}");
        Assert.Equal(1, stillOpen.GetProperty("currentStreak").GetInt32());
        Assert.Equal(Note, Incoming(stillOpen).GetProperty("body").GetString());
        Assert.False(Incoming(stillOpen).GetProperty("locked").GetBoolean());
    }

    [Fact]
    public async Task Join_with_an_unknown_code_fails()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateReadyClient();
        As(client, "ada");

        var response = await client.PostAsJsonAsync("/api/streaks/join", new { code = "this-code-does-not-exist" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var empty = await client.PostAsJsonAsync("/api/streaks/join", new { code = "" });
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
    }

    [Fact]
    public void Publishable_key_decodes_to_the_clerk_issuer()
    {
        var domain = "example.clerk.accounts.dev$";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(domain)).TrimEnd('=');
        Assert.Equal("https://example.clerk.accounts.dev", ClerkKeys.Issuer($"pk_test_{encoded}"));
    }

    [Fact]
    public void Database_url_becomes_an_npgsql_connection_string()
    {
        var connection = DatabaseConfig.ToNpgsql("postgres://app:p%40ss@db.internal:5432/openwhen?sslmode=require");
        Assert.Contains("Host=db.internal", connection);
        Assert.Contains("Database=openwhen", connection);
        Assert.Contains("Username=app", connection);
        Assert.Contains("Password=p@ss", connection);
        Assert.Contains("SSL Mode=Require", connection, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset Local(int year, int month, int day, int utcHour = 9) =>
        new(year, month, day, utcHour, 0, 0, TimeSpan.Zero);

    private static void As(HttpClient client, string userId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue(userId));

    private static async Task<JsonElement> PostOk(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> GetOk(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static JsonElement Incoming(JsonElement streak) =>
        streak.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("direction").GetString() == "in");
}
