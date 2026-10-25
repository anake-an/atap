using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Atap.Tests.Guards;

/// <summary>
/// The isolation check loads a second copy of Atap.dll. WPF finds component URIs by assembly name, so
/// nothing else may run while that copy exists: this collection runs alone, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IsolationCollection
{
    public const string Name = "Load context isolation";
}

/// <summary>
/// "Disabled never loads": reading the widget catalog, the feature list and the settings cards must not
/// pull in any heavy package, and neither may what startup asks every feature descriptor (its settings,
/// its EnabledBy keys and IsEnabled). Atap.dll is loaded into its own load context, so every assembly it
/// asks for is seen, even ones this test process has already loaded.
/// </summary>
[Collection(IsolationCollection.Name)]
public class CatalogIsolationTests
{
    private static readonly string[] Forbidden =
    [
        "NAudio", "MQTTnet", "System.Management", "Velopack",
        // The Windows Runtime projections, used by media sessions and other Windows sources.
        "Microsoft.Windows.SDK.NET", "WinRT.Runtime",
    ];

    [Fact]
    public void Discovery_RequestsNoHeavyAssembly()
    {
        var context = Check(AppContext.BaseDirectory);

        // Wait for the second copy to go, so WPF never resolves "Atap" to it.
        for (var i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Check(string folder)
    {
        var context = new TrackingLoadContext(folder);
        try
        {
            var atap = context.LoadFromAssemblyPath(Path.Combine(folder, "Atap.dll"));
            var discovery = atap.GetType("Atap.Composition.Discovery", throwOnError: true)!;
            const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var catalog = discovery.GetMethod("Widgets", Static)!.Invoke(null, [atap])!;
            var descriptors = (IEnumerable)catalog.GetType().GetProperty("Descriptors")!.GetValue(catalog)!;
            Assert.True(descriptors.Cast<object>().Count() >= 2, "The built-in widgets were not discovered.");

            var problems = new List<string>();
            var features = ((IEnumerable)discovery.GetMethod("Features", Static)!.Invoke(null, [atap, problems])!).Cast<object>().ToList();
            Assert.Empty(problems);
            Assert.True(features.Count >= 1, "The built-in features were not discovered.");
            AskLikeStartup(atap, features);
            discovery.GetMethod("SettingsCards", Static)!.Invoke(null, [atap]);

            var loaded = context.Requested.Where(name => Forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.True(loaded.Count == 0, $"Discovery loaded: {string.Join(", ", loaded)}");
            Assert.Contains("Atap.Core", context.Requested);
        }
        finally
        {
            context.Unload();
        }
        return new WeakReference(context);
    }

    /// <summary>
    /// What startup asks every feature, whether it is on or not: its settings (registered with the app
    /// settings), its EnabledBy keys and IsEnabled, in normal and safe mode, against the defaults.
    /// </summary>
    private static void AskLikeStartup(Assembly atap, List<object> features)
    {
        var descriptorType = atap.GetType("Atap.Composition.IFeatureDescriptor", throwOnError: true)!;
        var modeType = atap.GetType("Atap.Composition.AppMode", throwOnError: true)!;
        var settingsProperty = descriptorType.GetProperty("Settings")!;
        var enabledByProperty = descriptorType.GetProperty("EnabledBy")!;
        var isEnabled = descriptorType.GetMethod("IsEnabled")!;

        // SettingDefinition and SettingValues from the copy of Atap.Core in the same load context.
        var definitionType = settingsProperty.PropertyType.GetGenericArguments()[0];
        var definitions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(definitionType))!;
        foreach (var feature in features)
        {
            foreach (var definition in (IEnumerable)settingsProperty.GetValue(feature)!)
                definitions.Add(definition);
            Assert.All(((IEnumerable)enabledByProperty.GetValue(feature)!).Cast<object>(), key => Assert.IsType<string>(key));
        }

        var valuesType = definitionType.Assembly.GetType("Atap.Core.Widgets.SettingValues", throwOnError: true)!;
        var values = Activator.CreateInstance(valuesType, [definitions, null])!;
        foreach (var feature in features)
        {
            foreach (var mode in Enum.GetValues(modeType))
                isEnabled.Invoke(feature, [values, mode]);
        }
    }

    private sealed class TrackingLoadContext(string folder) : AssemblyLoadContext("Atap isolation check", isCollectible: true)
    {
        private readonly object _gate = new();
        private readonly List<string> _requested = [];

        public IReadOnlyList<string> Requested
        {
            get
            {
                lock (_gate)
                    return [.. _requested];
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? string.Empty;
            lock (_gate)
                _requested.Add(name);

            // App and package assemblies come from the test folder into this context; the framework stays shared.
            var path = Path.Combine(folder, name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
