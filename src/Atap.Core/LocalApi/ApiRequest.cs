namespace Atap.Core.LocalApi;

/// <summary>The operations the local API accepts. Wire names are in <see cref="ApiNames.Ops"/>.</summary>
public enum ApiOp
{
    /// <summary>Checks that Atap is running with the API on.</summary>
    Ping,

    /// <summary>Adds a live activity, or replaces the one with the same id.</summary>
    Push,

    /// <summary>Removes a live activity by id.</summary>
    End,

    /// <summary>Shows one transient alert.</summary>
    Alert,
}

/// <summary>State of a pushed activity.</summary>
public enum PushState
{
    Running,
    Done,
    Failed,
}

/// <summary>The fixed icons a push or alert may name. The App maps each one to its own glyph.</summary>
public enum PushIcon
{
    Info,
    Check,
    Warning,
    Error,
    Download,
    Upload,
    Sync,
    Backup,
    Server,
    Build,
    Timer,
    Bell,
    Terminal,
}

/// <summary>The fixed accent colours a push may name. They are the notch group accents.</summary>
public enum PushAccent
{
    /// <summary>Media accent.</summary>
    Teal,

    /// <summary>Productivity accent.</summary>
    Amber,

    /// <summary>System accent.</summary>
    Coral,

    /// <summary>Custom accent.</summary>
    Blue,

    /// <summary>Malaysia accent.</summary>
    Violet,
}

/// <summary>
/// One request to the local API. Only <see cref="PushProtocol.Parse(string?)"/> should build these from
/// outside input, since it cleans text and checks every limit.
/// </summary>
public abstract record ApiRequest
{
    private protected ApiRequest()
    {
    }

    public abstract ApiOp Op { get; }
}

/// <summary><c>{"op":"ping"}</c></summary>
public sealed record PingRequest : ApiRequest
{
    public override ApiOp Op => ApiOp.Ping;
}

/// <summary>
/// <c>{"op":"push","id":"backup","title":"Backup NAS","progress":64}</c>. A push with a known id replaces
/// that activity in full, so fields left out go back to their defaults.
/// </summary>
public sealed record PushRequest(string Id, string Title) : ApiRequest
{
    public override ApiOp Op => ApiOp.Push;

    public string? Subtitle { get; init; }

    /// <summary>0 to 100. Null shows no progress.</summary>
    public double? Progress { get; init; }

    public PushState State { get; init; } = PushState.Running;

    /// <summary>Null lets the push widget pick a glyph from <see cref="State"/>.</summary>
    public PushIcon? Icon { get; init; }

    /// <summary>Null uses the push widget's own accent.</summary>
    public PushAccent? Accent { get; init; }

    /// <summary>Whole seconds from <see cref="PushProtocol.MinTtl"/> to <see cref="PushProtocol.MaxTtl"/>. Null means <see cref="PushProtocol.DefaultTtl"/>.</summary>
    public TimeSpan? Ttl { get; init; }

    public TimeSpan EffectiveTtl => Ttl ?? PushProtocol.DefaultTtl;
}

/// <summary><c>{"op":"end","id":"backup"}</c></summary>
public sealed record EndRequest(string Id) : ApiRequest
{
    public override ApiOp Op => ApiOp.End;
}

/// <summary><c>{"op":"alert","title":"Backup done"}</c></summary>
public sealed record AlertRequest(string Title) : ApiRequest
{
    public override ApiOp Op => ApiOp.Alert;

    public string? Subtitle { get; init; }

    public PushIcon? Icon { get; init; }
}
