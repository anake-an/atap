using System.Collections.ObjectModel;
using System.Text;

namespace Atap.Core.LocalApi;

/// <summary>What <see cref="PushActivityList.Push"/> did with the request.</summary>
public enum PushChange
{
    /// <summary>The id was new.</summary>
    Added,

    /// <summary>The id was already there and its activity was replaced.</summary>
    Updated,
}

/// <param name="EvictedId">The activity dropped to make room, when the list was full.</param>
/// <param name="ExpiredIds">Activities whose TTL had passed, removed before the push was applied.</param>
public sealed record PushOutcome(PushChange Change, PushActivity Activity, string? EvictedId, IReadOnlyList<string> ExpiredIds);

/// <summary>
/// The live activities from the local API: at most <see cref="Capacity"/>, kept in the order their ids were
/// first pushed. Each push sets a fresh expiry from its TTL. The list owns no timer: the owner arms one
/// scheduler <c>At</c> for <see cref="NextExpiry"/> and calls <see cref="RemoveExpired"/> when it fires.
/// Memory only, so a restart clears it. Not thread-safe: use it from one thread.
/// </summary>
public sealed class PushActivityList
{
    public const int Capacity = 5;

    /// <summary>Longest <see cref="PushActivity.Source"/> kept, in Unicode scalar values.</summary>
    public const int MaxSourceLength = 32;

    private readonly List<PushActivity> _items = new(Capacity);

    public PushActivityList()
    {
        Items = _items.AsReadOnly();
    }

    /// <summary>Raised after any call that changed the list.</summary>
    public event EventHandler? Changed;

    /// <summary>Oldest id first. Updating an activity keeps its place.</summary>
    public ReadOnlyCollection<PushActivity> Items { get; }

    public int Count => _items.Count;

    /// <summary>The push widget is live while this is false.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>The most recently pushed activity, or null when the list is empty.</summary>
    public PushActivity? Latest
    {
        get
        {
            PushActivity? latest = null;
            foreach (var item in _items)
            {
                if (latest is null || item.UpdatedAt >= latest.UpdatedAt)
                    latest = item;
            }
            return latest;
        }
    }

    /// <summary>The earliest expiry, or null when the list is empty.</summary>
    public DateTimeOffset? NextExpiry
    {
        get
        {
            DateTimeOffset? next = null;
            foreach (var item in _items)
            {
                if (next is null || item.ExpiresAt < next)
                    next = item.ExpiresAt;
            }
            return next;
        }
    }

    public PushActivity? Find(string id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : _items[index];
    }

    /// <summary>
    /// Adds the activity, or replaces the one with the same id. Expired activities go first. A new id on a
    /// full list drops the activity pushed least recently. Throws <see cref="ArgumentException"/> when the
    /// request fails <see cref="PushProtocol.Validate"/>.
    /// </summary>
    /// <param name="now">The current time, from the same clock the owner uses for its scheduler.</param>
    /// <param name="source">A label for where the push came from. It is cleaned and cut to <see cref="MaxSourceLength"/>.</param>
    public PushOutcome Push(PushRequest request, DateTimeOffset now, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var error = PushProtocol.Validate(request);
        if (error is not null)
            throw new ArgumentException(error.Message, nameof(request));

        var expired = RemoveExpiredCore(now);

        PushChange change;
        string? evicted = null;
        var index = IndexOf(request.Id);
        var activity = new PushActivity(request.Id, request.Title)
        {
            Subtitle = request.Subtitle,
            Progress = request.Progress,
            State = request.State,
            Icon = request.Icon,
            Accent = request.Accent,
            Source = CleanSource(source),
            CreatedAt = index < 0 ? now : _items[index].CreatedAt,
            UpdatedAt = now,
            ExpiresAt = now + request.EffectiveTtl,
        };

        if (index >= 0)
        {
            _items[index] = activity;
            change = PushChange.Updated;
        }
        else
        {
            if (_items.Count >= Capacity)
            {
                var oldest = IndexOfLeastRecentlyPushed();
                evicted = _items[oldest].Id;
                _items.RemoveAt(oldest);
            }
            _items.Add(activity);
            change = PushChange.Added;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new PushOutcome(change, activity, evicted, expired);
    }

    /// <summary>Removes the activity with this id. Returns false when there was none.</summary>
    public bool End(string id)
    {
        var index = IndexOf(id);
        if (index < 0)
            return false;

        _items.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Removes every activity whose expiry is at or before <paramref name="now"/>. Returns their ids.</summary>
    public IReadOnlyList<string> RemoveExpired(DateTimeOffset now)
    {
        var expired = RemoveExpiredCore(now);
        if (expired.Count > 0)
            Changed?.Invoke(this, EventArgs.Empty);
        return expired;
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<string> RemoveExpiredCore(DateTimeOffset now)
    {
        List<string>? expired = null;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (!_items[i].IsExpired(now))
                continue;
            (expired ??= []).Insert(0, _items[i].Id);
            _items.RemoveAt(i);
        }
        return expired is null ? [] : expired;
    }

    private int IndexOf(string? id)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Id, id, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>Lowest <see cref="PushActivity.UpdatedAt"/>. A tie goes to the earlier place in the list.</summary>
    private int IndexOfLeastRecentlyPushed()
    {
        var oldest = 0;
        for (var i = 1; i < _items.Count; i++)
        {
            if (_items[i].UpdatedAt < _items[oldest].UpdatedAt)
                oldest = i;
        }
        return oldest;
    }

    private static string? CleanSource(string? source)
    {
        var clean = TextSanitizer.Sanitize(source);
        if (clean.Length == 0)
            return null;
        if (TextSanitizer.CountCharacters(clean) <= MaxSourceLength)
            return clean;

        var builder = new StringBuilder(MaxSourceLength);
        var count = 0;
        foreach (var rune in clean.EnumerateRunes())
        {
            if (++count > MaxSourceLength)
                break;
            builder.Append(rune.ToString());
        }
        return builder.ToString().TrimEnd();
    }
}
