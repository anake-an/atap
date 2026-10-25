using Atap.Core.Commands;

namespace Atap.Core.Tests.Commands;

public class CommandRegistryTests
{
    [Fact]
    public async Task Register_ThenRun_CallsTheCommand()
    {
        var registry = new CommandRegistry();
        string? received = null;
        registry.Register(new AtapCommand("media.playPause", "media.playPause", argument =>
        {
            received = argument ?? "none";
            return Task.CompletedTask;
        }));

        Assert.True(await registry.TryRunAsync("media.playPause"));
        Assert.Equal("none", received);
    }

    [Fact]
    public async Task TryRun_UnknownCommand_ReturnsFalse()
    {
        Assert.False(await new CommandRegistry().TryRunAsync("settings.open"));
    }

    [Fact]
    public async Task TryRun_UnavailableCommand_DoesNothing()
    {
        var registry = new CommandRegistry();
        var runs = 0;
        registry.Register(new AtapCommand("a.b", "a.b", _ => { runs++; return Task.CompletedTask; }) { IsAvailable = () => false });

        Assert.False(await registry.TryRunAsync("a.b"));
        Assert.Equal(0, runs);
    }

    [Fact]
    public void Register_DuplicateIdThrows()
    {
        var registry = new CommandRegistry();
        registry.Register(new AtapCommand("a.b", "a.b", _ => Task.CompletedTask));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new AtapCommand("a.b", "a.b", _ => Task.CompletedTask)));
    }

    [Fact]
    public void Dispose_RemovesTheCommandAndRaisesChanged()
    {
        var registry = new CommandRegistry();
        var changes = 0;
        registry.Changed += (_, _) => changes++;
        var registration = registry.Register(new AtapCommand("a.b", "a.b", _ => Task.CompletedTask));

        registration.Dispose();
        registration.Dispose();

        Assert.False(registry.Contains("a.b"));
        Assert.Empty(registry.All);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Dispose_OfAnOldRegistration_LeavesANewerOneAlone()
    {
        var registry = new CommandRegistry();
        var old = registry.Register(new AtapCommand("a.b", "a.b", _ => Task.CompletedTask));
        old.Dispose();
        registry.Register(new AtapCommand("a.b", "a.b", _ => Task.CompletedTask));

        old.Dispose();

        Assert.True(registry.Contains("a.b"));
    }
}
