namespace OpenWhen.Api.Data;

public sealed class AppUser
{
    public string Id { get; set; } = "";
    public string TimeZone { get; set; } = "Europe/Vilnius";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Streak
{
    public Guid Id { get; set; }
    public string HabitName { get; set; } = "";
    public int TargetDays { get; set; }
    public string JoinCode { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<Membership> Members { get; set; } = [];
    public List<MemberMessage> Messages { get; set; } = [];
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid StreakId { get; set; }
    public Streak Streak { get; set; } = null!;
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public int CurrentStreak { get; set; }
    public DateOnly? LastCheckInDate { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public List<CheckIn> CheckIns { get; set; } = [];
}

public sealed class CheckIn
{
    public Guid Id { get; set; }
    public Guid MembershipId { get; set; }
    public Membership Membership { get; set; } = null!;
    public DateOnly LocalDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MemberMessage
{
    public Guid Id { get; set; }
    public Guid StreakId { get; set; }
    public Streak Streak { get; set; } = null!;
    public string FromUserId { get; set; } = "";
    public string ToUserId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset? UnlockedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
