using System.Windows.Automation;
using System.Windows.Controls;
using Atap.Composition;
using Atap.Settings.Cards.Privacy.DeleteAll;
using Atap.Settings.Shell;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Settings;

public sealed class DeleteAllCardTests : IDisposable
{
    private readonly TestServices _services = new();
    private int _deleted;

    public DeleteAllCardTests()
    {
        _services.Settings.Load();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void TheCard_IsOnThePrivacyPage_AfterSavedData()
    {
        var card = new DeleteAllCard();

        Assert.Equal(SettingsPages.Privacy, SettingsPages.Find(card.SectionId)?.Id);
        Assert.NotEqual(card.TitleKey, _services.Text[card.TitleKey]);
        Assert.True(card.Order > new Atap.Settings.Cards.Privacy.ClearData.ClearDataCard().Order);
        Assert.Contains(typeof(DeleteAllCard), Discovery.SettingsCards(typeof(App).Assembly));
    }

    [Fact]
    public void Delete_RunsOnlyFromTheConfirmStep_AndOnlyOnce()
    {
        var model = Create();
        Assert.True(model.IsAsking);
        Assert.False(model.DeleteCommand.CanExecute(null));

        model.DeleteCommand.Execute(null);
        Assert.Equal(0, _deleted);

        model.AskCommand.Execute(null);
        Assert.True(model.IsConfirming);
        Assert.True(model.DeleteCommand.CanExecute(null));

        model.DeleteCommand.Execute(null);
        model.DeleteCommand.Execute(null);

        Assert.Equal(1, _deleted);
        Assert.True(model.IsDeleting);
        Assert.False(model.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void Cancel_ClosesTheConfirmStep_AndDeletesNothing()
    {
        var model = Create();
        model.AskCommand.Execute(null);

        model.CancelCommand.Execute(null);

        Assert.True(model.IsAsking);
        Assert.False(model.DeleteCommand.CanExecute(null));
        Assert.Equal(0, _deleted);
    }

    [Fact]
    public void WithoutAnAppToQuit_NothingCanBeDeleted()
    {
        var model = new DeleteAllViewModel(null);
        model.AskCommand.Execute(null);

        Assert.False(model.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void TheViews_AskFirst_ThenSayWhatGoes() => Sta.Run(() =>
    {
        var model = Create();
        var host = SettingsUi.Render(model, new DeleteAllCard().ViewsSource);

        var ask = Assert.Single(SettingsUi.Visible<Button>(host));
        Assert.Equal("Delete all my Atap data", ask.Content);
        Assert.True(ask.IsTabStop && ask.Focusable);
        Assert.Equal("Delete all data", AutomationProperties.GetName(Assert.Single(SettingsUi.Find<Border>(host), b => AutomationProperties.GetName(b).Length > 0)));
        Assert.DoesNotContain(SettingsUi.Visible<TextBlock>(host), t => t.Text.Contains("cannot be undone", StringComparison.Ordinal));

        model.AskCommand.Execute(null);
        SettingsUi.Layout(host);

        var buttons = SettingsUi.Visible<Button>(host).Select(b => b.Content).ToList();
        Assert.Equal(["Delete and close Atap", "Cancel"], buttons);
        var texts = SettingsUi.Visible<TextBlock>(host).Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.Contains("settings, layout, profiles, to-dos, notes, saved passwords and keys, saved data and logs", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Atap closes when it is done.", StringComparison.Ordinal));
    });

    private DeleteAllViewModel Create() => new(() => _deleted++);
}
