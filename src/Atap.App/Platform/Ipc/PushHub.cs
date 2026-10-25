using Atap.Core.LocalApi;
using Atap.Core.Scheduling;
using Atap.Core.Shared;

namespace Atap.Platform.Ipc;

/// <summary>
/// The activities programs pushed through the local API, at most 5, shared by the API and the push widget. Each
/// one ends at its time to live: one scheduler item waits for the earliest, and none exists while the list is
/// empty. Use it on the UI thread.
/// </summary>
internal sealed class PushHub : ISharedService<PushHub>
{
    private readonly IScheduler _scheduler;
    private readonly TimeProvider _time;
    private IDisposable? _expiry;

    private PushHub(IScheduler scheduler, TimeProvider time)
    {
        _scheduler = scheduler;
        _time = time;
        Activities.Changed += OnActivitiesChanged;
    }

    /// <summary>Raised when an activity is added, changed, ended or expired.</summary>
    public event EventHandler? Changed;

    public PushActivityList Activities { get; } = new();

    public static PushHub Create(SharedServiceContext context) => new(context.Scheduler, context.Time);

    public PushOutcome Push(PushRequest request, string? source) => Activities.Push(request, _time.GetUtcNow(), source);

    public bool End(string id) => Activities.End(id);

    public void Dispose()
    {
        Activities.Changed -= OnActivitiesChanged;
        _expiry?.Dispose();
        _expiry = null;
        Activities.Clear();
    }

    private void OnActivitiesChanged(object? sender, EventArgs e)
    {
        ArmExpiry();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ArmExpiry()
    {
        _expiry?.Dispose();
        _expiry = Activities.NextExpiry is { } due
            ? _scheduler.At(due, () => Activities.RemoveExpired(_time.GetUtcNow()))
            : null;
    }
}
