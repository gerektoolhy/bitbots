using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpenWhen.Api.Data;
using OpenWhen.Api.Time;

namespace OpenWhen.Api.Streaks;

public sealed record TimeZoneRequest(string? TimeZone);
public sealed record CreateStreakRequest(string? HabitName, int? TargetDays);
public sealed record JoinStreakRequest(string? Code);
public sealed record LeaveMessageRequest(string? ToUserId, string? Body);

public sealed record MessageView(Guid Id, string Direction, bool? Locked, string? Body, DateTimeOffset CreatedAt);

public sealed class StreakService(AppDb db, IClock clock)
{
    public async Task<IResult> GetMe(ClaimsPrincipal principal, CancellationToken ct)
    {
        var user = await EnsureUser(UserId(principal), ct);
        return Results.Ok(new { userId = user.Id, timeZone = user.TimeZone });
    }

    public async Task<IResult> SetTimeZone(ClaimsPrincipal principal, TimeZoneRequest body, CancellationToken ct)
    {
        var zone = body.TimeZone?.Trim() ?? "";
        if (!StreakRules.IsKnownTimeZone(zone))
            return Results.BadRequest(new { error = "Unknown timezone." });

        var user = await EnsureUser(UserId(principal), ct);
        user.TimeZone = zone;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { userId = user.Id, timeZone = user.TimeZone });
    }

    public async Task<IResult> List(ClaimsPrincipal principal, CancellationToken ct)
    {
        var user = await EnsureUser(UserId(principal), ct);
        var today = StreakRules.LocalToday(clock.UtcNow, user.TimeZone);
        var rows = await db.Memberships.AsNoTracking()
            .Where(membership => membership.UserId == user.Id)
            .Select(membership => new
            {
                membership.StreakId,
                membership.Streak.HabitName,
                membership.Streak.TargetDays,
                membership.Streak.JoinCode,
                membership.CurrentStreak,
                membership.LastCheckInDate,
            })
            .OrderBy(row => row.HabitName)
            .ToListAsync(ct);

        var payload = rows.Select(row => new
        {
            id = row.StreakId,
            habitName = row.HabitName,
            targetDays = row.TargetDays,
            joinCode = row.JoinCode,
            currentStreak = StreakRules.Effective(row.CurrentStreak, row.LastCheckInDate, today),
            checkedInToday = row.LastCheckInDate == today,
        });
        return Results.Ok(payload);
    }

    public async Task<IResult> Create(ClaimsPrincipal principal, CreateStreakRequest body, CancellationToken ct)
    {
        var habit = body.HabitName?.Trim() ?? "";
        if (habit.Length is < 1 or > 80)
            return Results.BadRequest(new { error = "Habit name must be 1–80 characters." });

        var target = body.TargetDays ?? StreakRules.DefaultTargetDays;
        if (target is < 1 or > 365)
            return Results.BadRequest(new { error = "Target length must be between 1 and 365 days." });

        var user = await EnsureUser(UserId(principal), ct);
        var streak = new Streak
        {
            Id = Guid.NewGuid(),
            HabitName = habit,
            TargetDays = target,
            JoinCode = NewJoinCode(),
            CreatedByUserId = user.Id,
            CreatedAt = clock.UtcNow,
        };
        db.Streaks.Add(streak);
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            StreakId = streak.Id,
            UserId = user.Id,
            JoinedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/streaks/{streak.Id}", Summary(streak, 0, false));
    }

    public async Task<IResult> Join(ClaimsPrincipal principal, JoinStreakRequest body, CancellationToken ct)
    {
        var code = body.Code?.Trim() ?? "";
        if (code.Length is < 8 or > 64)
            return Results.NotFound(new { error = "Unknown join code." });

        var streak = await db.Streaks.SingleOrDefaultAsync(item => item.JoinCode == code, ct);
        if (streak is null)
            return Results.NotFound(new { error = "Unknown join code." });

        var user = await EnsureUser(UserId(principal), ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(
            item => item.StreakId == streak.Id && item.UserId == user.Id, ct);
        if (membership is null)
        {
            membership = new Membership
            {
                Id = Guid.NewGuid(),
                StreakId = streak.Id,
                UserId = user.Id,
                JoinedAt = clock.UtcNow,
            };
            db.Memberships.Add(membership);
            await db.SaveChangesAsync(ct);
        }

        var today = StreakRules.LocalToday(clock.UtcNow, user.TimeZone);
        return Results.Ok(Summary(
            streak,
            StreakRules.Effective(membership.CurrentStreak, membership.LastCheckInDate, today),
            membership.LastCheckInDate == today));
    }

    public async Task<IResult> Get(ClaimsPrincipal principal, Guid streakId, CancellationToken ct)
    {
        var user = await EnsureUser(UserId(principal), ct);
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(item => item.StreakId == streakId && item.UserId == user.Id, ct);
        if (membership is null)
            return Results.NotFound(new { error = "Streak not found." });

        var streak = await db.Streaks.AsNoTracking().SingleAsync(item => item.Id == streakId, ct);
        var today = StreakRules.LocalToday(clock.UtcNow, user.TimeZone);
        var members = (await db.Memberships.AsNoTracking()
            .Where(item => item.StreakId == streakId)
            .Select(item => new { item.UserId, item.JoinedAt })
            .ToListAsync(ct))
            .OrderBy(item => item.JoinedAt)
            .Select(item => item.UserId)
            .ToList();

        var messages = await MessagesFor(user.Id, streak, today, ct);
        return Results.Ok(new
        {
            id = streak.Id,
            habitName = streak.HabitName,
            targetDays = streak.TargetDays,
            joinCode = streak.JoinCode,
            currentStreak = StreakRules.Effective(membership.CurrentStreak, membership.LastCheckInDate, today),
            checkedInToday = membership.LastCheckInDate == today,
            members,
            messages,
        });
    }

    public async Task<IResult> CheckIn(ClaimsPrincipal principal, Guid streakId, CancellationToken ct)
    {
        var user = await EnsureUser(UserId(principal), ct);
        var membership = await db.Memberships.Include(item => item.Streak)
            .SingleOrDefaultAsync(item => item.StreakId == streakId && item.UserId == user.Id, ct);
        if (membership is null)
            return Results.NotFound(new { error = "Streak not found." });

        var today = StreakRules.LocalToday(clock.UtcNow, user.TimeZone);
        var (next, counted) = StreakRules.ApplyCheckIn(membership.CurrentStreak, membership.LastCheckInDate, today);
        if (!counted)
        {
            return Results.Ok(new
            {
                currentStreak = membership.CurrentStreak,
                checkedInToday = true,
                counted = false,
            });
        }

        membership.CurrentStreak = next;
        membership.LastCheckInDate = today;
        db.CheckIns.Add(new CheckIn
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            LocalDate = today,
            CreatedAt = clock.UtcNow,
        });
        if (next >= membership.Streak.TargetDays)
            await MarkUnlocked(streakId, user.Id, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            membership = await db.Memberships.SingleAsync(
                item => item.StreakId == streakId && item.UserId == user.Id, ct);
            next = membership.CurrentStreak;
            counted = false;
        }

        return Results.Ok(new { currentStreak = next, checkedInToday = true, counted });
    }

    public async Task<IResult> LeaveMessage(ClaimsPrincipal principal, Guid streakId, LeaveMessageRequest body, CancellationToken ct)
    {
        var text = body.Body?.Trim() ?? "";
        if (text.Length is < 1 or > 2000)
            return Results.BadRequest(new { error = "Message must be 1–2000 characters." });

        var recipientId = body.ToUserId?.Trim() ?? "";
        if (recipientId.Length is < 1 or > 128)
            return Results.BadRequest(new { error = "Choose a member to leave the message for." });

        var user = await EnsureUser(UserId(principal), ct);
        if (recipientId == user.Id)
            return Results.BadRequest(new { error = "Leave the message for someone else." });

        var memberIds = await db.Memberships
            .Where(item => item.StreakId == streakId && (item.UserId == user.Id || item.UserId == recipientId))
            .Select(item => item.UserId)
            .ToListAsync(ct);
        if (!memberIds.Contains(user.Id))
            return Results.NotFound(new { error = "Streak not found." });
        if (!memberIds.Contains(recipientId))
            return Results.BadRequest(new { error = "That person is not in this streak." });

        var message = new MemberMessage
        {
            Id = Guid.NewGuid(),
            StreakId = streakId,
            FromUserId = user.Id,
            ToUserId = recipientId,
            Body = text,
            CreatedAt = clock.UtcNow,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/streaks/{streakId}", new
        {
            id = message.Id,
            direction = "out",
            createdAt = message.CreatedAt,
        });
    }

    private async Task<List<MessageView>> MessagesFor(string userId, Streak streak, DateOnly today, CancellationToken ct)
    {
        var membership = await db.Memberships.SingleAsync(
            item => item.StreakId == streak.Id && item.UserId == userId, ct);
        var effective = StreakRules.Effective(membership.CurrentStreak, membership.LastCheckInDate, today);
        var goalMet = effective >= streak.TargetDays;

        var incoming = (await db.Messages
            .Where(message => message.StreakId == streak.Id && message.ToUserId == userId)
            .ToListAsync(ct))
            .OrderBy(message => message.CreatedAt)
            .ToList();

        var changed = false;
        if (goalMet)
        {
            foreach (var message in incoming.Where(message => message.UnlockedAt is null))
            {
                message.UnlockedAt = clock.UtcNow;
                changed = true;
            }
        }
        if (changed)
            await db.SaveChangesAsync(ct);

        var views = new List<MessageView>();
        foreach (var message in incoming)
        {
            var open = message.UnlockedAt is not null;
            views.Add(open
                ? new MessageView(message.Id, "in", false, message.Body, message.CreatedAt)
                : new MessageView(message.Id, "in", true, null, message.CreatedAt));
        }

        var outgoing = (await db.Messages.AsNoTracking()
            .Where(message => message.StreakId == streak.Id && message.FromUserId == userId && message.ToUserId != userId)
            .Select(message => new { message.Id, message.CreatedAt })
            .ToListAsync(ct))
            .OrderBy(message => message.CreatedAt);
        foreach (var message in outgoing)
            views.Add(new MessageView(message.Id, "out", null, null, message.CreatedAt));

        return views;
    }

    private async Task MarkUnlocked(Guid streakId, string userId, CancellationToken ct)
    {
        var pending = await db.Messages
            .Where(message => message.StreakId == streakId && message.ToUserId == userId && message.UnlockedAt == null)
            .ToListAsync(ct);
        foreach (var message in pending)
            message.UnlockedAt = clock.UtcNow;
    }

    private async Task<AppUser> EnsureUser(string userId, CancellationToken ct)
    {
        var existing = await db.Users.SingleOrDefaultAsync(user => user.Id == userId, ct);
        if (existing is not null)
            return existing;

        var created = new AppUser
        {
            Id = userId,
            TimeZone = StreakRules.DefaultTimeZone,
            CreatedAt = clock.UtcNow,
        };
        db.Users.Add(created);
        try
        {
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.Users.SingleAsync(user => user.Id == userId, ct);
        }
    }

    private static object Summary(Streak streak, int currentStreak, bool checkedInToday) => new
    {
        id = streak.Id,
        habitName = streak.HabitName,
        targetDays = streak.TargetDays,
        joinCode = streak.JoinCode,
        currentStreak,
        checkedInToday,
    };

    private static string UserId(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Authenticated request has no subject.");
        return id;
    }

    public static string NewJoinCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
