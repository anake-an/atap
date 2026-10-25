using System.Xml.Linq;

namespace Atap.Tests.Infrastructure;

/// <summary>
/// Reads the real App.xaml from the repository. Tests cannot load it as XAML: that would create the App
/// itself, which starts Atap on the user's own files.
/// </summary>
internal static class AppXaml
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public static string FilePath => Path.Combine(RepoPaths.App, "App.xaml");

    /// <summary>
    /// The Source of every dictionary App.xaml merges, in order, relative to the Atap.App folder with forward
    /// slashes, for example "Ui/Theme/Theme.xaml".
    /// </summary>
    public static IReadOnlyList<string> MergedSources()
    {
        var root = XDocument.Load(FilePath).Root!;
        return root
            .Descendants(Presentation + "ResourceDictionary.MergedDictionaries")
            .Elements(Presentation + "ResourceDictionary")
            .Select(d => (string?)d.Attribute("Source") ?? string.Empty)
            .Select(Normalize)
            .ToList();
    }

    /// <summary>
    /// "/Atap;component/Widgets/Media/MediaViews.xaml", "pack://application:,,,/Atap;component/..." and
    /// "Widgets/Media/MediaViews.xaml" all become "Widgets/Media/MediaViews.xaml".
    /// </summary>
    public static string Normalize(string source)
    {
        var path = source.Replace('\\', '/');
        var component = path.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (component >= 0)
            path = path[(component + ";component/".Length)..];
        return path.TrimStart('/');
    }

    /// <summary>The pack URI that loads a merged source from Atap.dll.</summary>
    public static Uri ComponentUri(string source) => new($"/Atap;component/{Normalize(source)}", UriKind.Relative);
}
