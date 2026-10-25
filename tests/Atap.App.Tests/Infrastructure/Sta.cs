using System.Windows;
using System.Windows.Threading;
using Atap.Core.Localization;

namespace Atap.Tests.Infrastructure;

/// <summary>
/// One shared STA thread with a running dispatcher and the same resources as <see cref="App"/>, for every
/// test that touches WPF. The dictionaries are the ones App.xaml merges, read from the file itself, so a
/// change to App.xaml reaches the guard tests. A process can have only one Application.
/// </summary>
internal static class Sta
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Dispatcher Dispatcher => Ui.Value;

    public static void Run(Action action) => Ui.Value.Invoke(action);

    public static T Run<T>(Func<T> func) => Ui.Value.Invoke(func);

    public static Task RunAsync(Func<Task> func) => Ui.Value.InvokeAsync(func).Task.Unwrap();

    private static ResourceDictionary Load(Uri uri) => (ResourceDictionary)Application.LoadComponent(uri);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                Localizer.Instance = new Localizer(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);
                // A plain Application with the dictionaries App.xaml merges, in the same order.
                // Never the real App: creating it queues its OnStartup, which would start Atap on the user's own files.
                // Assigned after all load, as App.xaml does, so templates cannot lean on the app resources while parsing.
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var resources = new ResourceDictionary();
                foreach (var source in AppXaml.MergedSources())
                    resources.MergedDictionaries.Add(Load(AppXaml.ComponentUri(source)));
                application.Resources = resources;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            ready.Set();
            if (failure is null)
                Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Atap test UI",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure is not null)
            throw new InvalidOperationException("The test UI thread could not start.", failure);
        return dispatcher!;
    }
}
