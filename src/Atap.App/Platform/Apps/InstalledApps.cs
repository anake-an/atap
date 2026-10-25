using System.Diagnostics;

namespace Atap.Platform.Apps;

/// <summary>An app from Start: its name and the id Windows opens it by.</summary>
/// <param name="Name">The name Start shows, such as "Calculator".</param>
/// <param name="ParsingName">Its id in <c>shell:AppsFolder</c>: a package app id or a path.</param>
internal sealed record InstalledApp(string Name, string ParsingName);

/// <summary>The apps the command bar can open. Tests pass their own.</summary>
internal interface IAppCatalog
{
    /// <summary>Every app in Start. Read fresh each time, so the command bar keeps nothing while closed.</summary>
    Task<IReadOnlyList<InstalledApp>> LoadAsync();

    void Launch(InstalledApp app);
}

/// <summary>
/// Windows' Apps folder (<c>shell:AppsFolder</c>), which lists Store and desktop apps alike, the same as Start.
/// The shell objects need an STA thread, so they are read on a short-lived one away from the UI.
/// </summary>
internal sealed class ShellAppsFolder : IAppCatalog
{
    public Task<IReadOnlyList<InstalledApp>> LoadAsync()
    {
        var done = new TaskCompletionSource<IReadOnlyList<InstalledApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Read());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Apps: reading the Apps folder failed: {ex.Message}");
                done.SetResult([]);
            }
        })
        {
            IsBackground = true,
            Name = "Atap apps",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    public void Launch(InstalledApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.ParsingName}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Apps: launching {app.Name} failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<InstalledApp> Read()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return [];
        dynamic shell = Activator.CreateInstance(shellType)!;
        var folder = shell.NameSpace("shell:AppsFolder");
        if (folder is null)
            return [];
        var apps = new List<InstalledApp>();
        foreach (var item in folder.Items())
        {
            string? name = item.Name;
            string? path = item.Path;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                apps.Add(new InstalledApp(name.Trim(), path));
        }
        return apps;
    }
}
