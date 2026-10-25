namespace Atap.Core.Storage;

/// <summary>
/// Secrets (tokens, private URLs) kept with Windows data protection for the current user, in
/// %LOCALAPPDATA%\AtapData\secrets.json. Settings files hold only the "secret:&lt;id&gt;" reference.
/// </summary>
public interface ISecretStore
{
    /// <summary>False when secrets cannot be stored on this PC. Secret fields then show NeedsSetup.</summary>
    bool IsAvailable { get; }

    /// <summary>Stores a value and returns its "secret:&lt;id&gt;" reference.</summary>
    string Put(string value);

    /// <summary>Null when the reference does not resolve on this PC (made on another PC, or typed by hand).</summary>
    string? Get(string reference);

    void Delete(string reference);
}
