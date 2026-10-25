using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atap.Core.Custom;
using Atap.Core.Layout;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Widgets.Script;

/// <summary>
/// What a script widget runs and its approval, in state\&lt;instanceId&gt;\script.json. Machine state only, so it never
/// syncs and never goes into layout.json.
/// </summary>
public sealed class ScriptState
{
    public int Version { get; set; } = 1;

    /// <summary>Full path of the program, or null when nothing is set up.</summary>
    public string? Program { get; set; }

    public List<string>? Arguments { get; set; }

    public string? WorkingFolder { get; set; }

    /// <summary><see cref="ScriptSpec.ComputeApproval"/> of the spec above, under the key in <see cref="ApprovalKey"/>.</summary>
    public string? Approval { get; set; }

    /// <summary>"secret:&lt;id&gt;" of this instance's approval key, kept with Windows data protection for this user on this PC.</summary>
    public string? ApprovalKey { get; set; }
}

/// <summary>When the program last started on this PC, in state\&lt;instanceId&gt;\last-run.json.</summary>
public sealed class ScriptLastRun
{
    public DateTimeOffset At { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(ScriptState))]
[JsonSerializable(typeof(ScriptLastRun))]
internal sealed partial class ScriptJson : JsonSerializerContext;

/// <summary>
/// Reads and approves one instance's spec. The approval key is made on the first approval, kept in the secret store
/// (Windows data protection, this user, this PC) and referenced from the state file. So a state file copied to another
/// PC, or edited by hand, does not match its approval there.
/// </summary>
/// <remarks>
/// Every change goes through <see cref="Approve"/> or <see cref="Remove"/>, and <see cref="Changed"/> tells the running
/// widget with that instance id to check again at once. A spec changed any other way stops running until it is approved again.
/// </remarks>
public sealed class ScriptStore(string instanceId, IWidgetStorage storage, ISecretStore secrets)
{
    /// <summary>Storage adds ".json", so the files are script.json and last-run.json.</summary>
    public const string FileName = "script";
    public const string LastRunFileName = "last-run";

    /// <summary>
    /// Starts every stored approval key, so a secret that is not one, such as another widget's link, is never used as a
    /// key and never deleted as one.
    /// </summary>
    internal const string KeyPrefix = "atap-script-key:v1:";

    /// <summary>Raised on the UI thread with the instance id after an approval or a removal.</summary>
    public static event EventHandler<string>? Changed;

    public string InstanceId { get; } = instanceId;

    /// <summary>False when this PC cannot keep secrets, so nothing can be approved.</summary>
    public bool CanApprove => secrets.IsAvailable;

    /// <summary>The state from disk, read fresh each time. Empty when the file is missing or does not parse.</summary>
    public ScriptState Load() => storage.Load(FileName, ScriptJson.Default.ScriptState, new ScriptState(), DataScope.Machine);

    /// <summary>The spec in <paramref name="state"/>, approved or not. Null when no program is set.</summary>
    public static ScriptSpec? SpecOf(ScriptState state) =>
        string.IsNullOrEmpty(state.Program) ? null : new ScriptSpec(state.Program, state.Arguments, state.WorkingFolder);

    /// <summary>
    /// True only when <paramref name="spec"/> is the one in <paramref name="state"/> and its approval matches under this
    /// PC's key. A spec that fails <see cref="ScriptSpec.Validate"/> is never approved.
    /// </summary>
    public bool IsApproved(ScriptSpec spec, ScriptState state)
    {
        if (!spec.Equals(SpecOf(state)) || ReadKey(secrets, state.ApprovalKey) is not { } key)
            return false;
        try
        {
            return spec.IsApprovedBy(state.Approval, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>The spec to run right now, read from disk: only an approved one. Null otherwise.</summary>
    public ScriptSpec? LoadApproved()
    {
        var state = Load();
        return SpecOf(state) is { } spec && IsApproved(spec, state) ? spec : null;
    }

    /// <summary>
    /// Saves <paramref name="spec"/> with its approval on this PC. False when it is not valid, approvals cannot be kept
    /// here, or the file could not be written.
    /// </summary>
    /// <remarks>
    /// When the stored key does not open, a new one is made. The old reference is never deleted here, since a state file
    /// edited by hand could point it at another widget's secret.
    /// </remarks>
    public bool Approve(ScriptSpec spec)
    {
        if (spec.Validate() != ScriptSpecProblem.None || !secrets.IsAvailable)
            return false;
        var state = Load();
        var keyRef = state.ApprovalKey;
        var key = ReadKey(secrets, keyRef);
        try
        {
            if (key is null)
            {
                key = ScriptSpec.CreateApprovalKey();
                keyRef = secrets.Put(KeyPrefix + Convert.ToBase64String(key));
            }
            var approved = new ScriptState
            {
                Program = spec.Executable,
                Arguments = [.. spec.Arguments],
                WorkingFolder = spec.WorkingFolder,
                Approval = spec.ComputeApproval(key),
                ApprovalKey = keyRef,
            };
            return Save(approved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Clears the spec and its approval. The key stays for the next approval. False when the file could not be written.</summary>
    public bool Remove()
    {
        try
        {
            return Save(new ScriptState { ApprovalKey = Load().ApprovalKey });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>When the program last started, or null when it never has here.</summary>
    public DateTimeOffset? LoadLastRun()
    {
        var last = storage.Load(LastRunFileName, ScriptJson.Default.ScriptLastRun, new ScriptLastRun(), DataScope.Machine);
        return last.At == default ? null : last.At;
    }

    /// <summary>Records a start. A failed write is ignored, so the run still happens.</summary>
    public void SaveLastRun(DateTimeOffset at)
    {
        try
        {
            storage.Save(LastRunFileName, new ScriptLastRun { At = at }, ScriptJson.Default.ScriptLastRun, DataScope.Machine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next start can come sooner. The interval timer still holds.
        }
    }

    /// <summary>
    /// The instance ids in the layout: the main list and every profile's. An instance in none of them has been removed.
    /// </summary>
    public static IReadOnlySet<string> InstancesIn(LayoutDocument layout)
    {
        var ids = new HashSet<string>(layout.EnabledWidgets, StringComparer.Ordinal);
        foreach (var profile in layout.Profiles)
        {
            if (profile.EnabledWidgets is { } enabled)
                ids.UnionWith(enabled);
        }
        return ids;
    }

    /// <summary>
    /// Forgets every script instance with a folder in <paramref name="machineState"/> that is not in <paramref name="keep"/>:
    /// its folder with the spec, the approval and the last run time, and its approval key in the secret store.
    /// So a new instance that gets the same id later, from this PC or from a synced layout, starts unapproved.
    /// Folders of other widget types are never touched. One folder that cannot be removed does not stop the rest.
    /// </summary>
    /// <param name="machineState">%LOCALAPPDATA%\AtapData\state, with one folder per instance id.</param>
    /// <returns>The instance ids that were forgotten.</returns>
    public static IReadOnlyList<string> ForgetRemoved(string machineState, IReadOnlySet<string> keep, ISecretStore secrets)
    {
        var forgotten = new List<string>();
        IEnumerable<string> folders;
        try
        {
            folders = Directory.Exists(machineState) ? Directory.GetDirectories(machineState) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return forgotten;
        }

        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            if (!WidgetInstanceId.TryParse(name, out var id) || id.TypeId != ScriptDescriptor.TypeIdValue || keep.Contains(name))
                continue;
            try
            {
                DeleteKeyOf(Path.Combine(folder, FileName + ".json"), secrets);
                Directory.Delete(folder, recursive: true);
                forgotten.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked for now. The next layout change or start tries again.
            }
        }
        return forgotten;
    }

    /// <summary>Deletes the approval key named in a state file, only when the secret really is a script approval key.</summary>
    private static void DeleteKeyOf(string stateFile, ISecretStore secrets)
    {
        ScriptState? state;
        try
        {
            state = JsonSerializer.Deserialize(File.ReadAllBytes(stateFile), ScriptJson.Default.ScriptState);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return;
        }
        if (state?.ApprovalKey is not { } reference || ReadKey(secrets, reference) is not { } key)
            return;
        CryptographicOperations.ZeroMemory(key);
        secrets.Delete(reference);
    }

    private bool Save(ScriptState state)
    {
        storage.Save(FileName, state, ScriptJson.Default.ScriptState, DataScope.Machine);
        Changed?.Invoke(this, InstanceId);
        return true;
    }

    /// <summary>
    /// The key, or null when there is none, it does not open on this PC, it is too short, or the secret is not a script
    /// approval key at all.
    /// </summary>
    private static byte[]? ReadKey(ISecretStore secrets, string? reference)
    {
        if (reference is null)
            return null;
        string? text;
        try
        {
            text = secrets.Get(reference);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
        if (text is null || !text.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return null;
        try
        {
            var key = Convert.FromBase64String(text[KeyPrefix.Length..]);
            if (key.Length >= ScriptSpec.ApprovalKeyBytes)
                return key;
            CryptographicOperations.ZeroMemory(key);
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
