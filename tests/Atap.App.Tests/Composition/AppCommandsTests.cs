using Atap.Composition;
using Atap.Core.Commands;
using Atap.Updates;

namespace Atap.Tests.Composition;

public sealed class AppCommandsTests
{
    private readonly CommandRegistry _commands = new();

    [Fact]
    public async Task Quit_RunsTheQuitAction()
    {
        var quits = 0;
        AppCommands.Register(_commands, () => quits++, new FakeUpdates());

        Assert.True(await _commands.TryRunAsync(AppCommands.Quit));

        Assert.Equal(1, quits);
    }

    [Fact]
    public async Task CheckForUpdates_ChecksOnlyWhenIdle()
    {
        var updates = new FakeUpdates();
        AppCommands.Register(_commands, () => { }, updates);
        var command = _commands.All.Single(c => c.Id == AppCommands.CheckForUpdates);

        Assert.True(command.IsAvailable!());
        Assert.True(await _commands.TryRunAsync(AppCommands.CheckForUpdates));
        Assert.Equal(1, updates.Checks);

        updates.Status = UpdateStatus.Downloading;
        Assert.False(command.IsAvailable!());
        updates.Status = UpdateStatus.Installing;
        Assert.False(command.IsAvailable!());

        // A downloaded update: pressing Check installs it now.
        updates.Status = UpdateStatus.ReadyToInstall;
        Assert.True(command.IsAvailable!());
        updates.IsSupported = false;
        updates.Status = UpdateStatus.Idle;
        Assert.False(command.IsAvailable!());
    }

    [Fact]
    public void StoreBuilds_HaveNoUpdateCommand()
    {
        AppCommands.Register(_commands, () => { }, updates: null);

        Assert.True(_commands.Contains(AppCommands.Quit));
        Assert.False(_commands.Contains(AppCommands.CheckForUpdates));
    }

    [Fact]
    public void TitleKeys_HaveText()
    {
        var text = new Atap.Core.Localization.Localizer(
            new Atap.Core.Localization.AssemblyStringResources(typeof(App).Assembly), Atap.Core.Localization.Localizer.English);
        AppCommands.Register(_commands, () => { }, new FakeUpdates());

        foreach (var command in _commands.All)
            Assert.NotEqual(command.TitleKey, text[command.TitleKey]);
    }

    private sealed class FakeUpdates : IUpdateService
    {
        public bool IsSupported { get; set; } = true;

        public UpdateStatus Status { get; set; }

        public int Checks { get; private set; }

        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public Task CheckNowAsync()
        {
            Checks++;
            return Task.CompletedTask;
        }

        public void ApplyPendingOnExit()
        {
        }

        public void Dispose()
        {
        }
    }
}
