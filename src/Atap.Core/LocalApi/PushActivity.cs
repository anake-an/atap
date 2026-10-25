namespace Atap.Core.LocalApi;

/// <summary>One live activity pushed through the local API, as the push widget shows it.</summary>
public sealed record PushActivity(string Id, string Title)
{
    public string? Subtitle { get; init; }

    /// <summary>0 to 100, or null for no progress.</summary>
    public double? Progress { get; init; }

    public PushState State { get; init; }

    public PushIcon? Icon { get; init; }

    public PushAccent? Accent { get; init; }

    /// <summary>Where the push came from, such as "powershell". Shown only. Null when unknown.</summary>
    public string? Source { get; init; }

    /// <summary>When the id was first pushed. Updates keep it.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
