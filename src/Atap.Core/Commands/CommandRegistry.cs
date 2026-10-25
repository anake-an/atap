namespace Atap.Core.Commands;

/// <summary>Holds every registered <see cref="AtapCommand"/>. Not thread-safe: use it on the UI thread.</summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, AtapCommand> _commands = new(StringComparer.Ordinal);

    public IReadOnlyCollection<AtapCommand> All => _commands.Values;

    public event EventHandler? Changed;

    public IDisposable Register(AtapCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Id);
        if (!_commands.TryAdd(command.Id, command))
            throw new InvalidOperationException($"A command with id '{command.Id}' is already registered.");
        Changed?.Invoke(this, EventArgs.Empty);
        return new Registration(this, command);
    }

    public bool Contains(string id) => _commands.ContainsKey(id);

    /// <summary>Exceptions from the command reach the caller.</summary>
    public async Task<bool> TryRunAsync(string id, string? argument = null)
    {
        if (!_commands.TryGetValue(id, out var command) || command.IsAvailable?.Invoke() == false)
            return false;
        await command.Run(argument);
        return true;
    }

    private void Remove(AtapCommand command)
    {
        // Only the registration that added it may remove it.
        if (_commands.TryGetValue(command.Id, out var current) && ReferenceEquals(current, command))
        {
            _commands.Remove(command.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Registration(CommandRegistry owner, AtapCommand command) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.Remove(command);
        }
    }
}
