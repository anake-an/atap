using System.Text.RegularExpressions;
using System.Windows;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Guards;

/// <summary>
/// Every window icon names Atap.dll in its path. A path such as "/Assets/atap.ico" is read from the
/// assembly that started the process, so it fails whenever that is not Atap.exe, as in these tests.
/// </summary>
public partial class WindowIconTests
{
    private const string ComponentPrefix = "/Atap;component/";

    [Fact]
    public void EveryXamlIcon_LoadsFromAtapDll()
    {
        var icons = RepoPaths.Files(RepoPaths.App, "*.xaml")
            .SelectMany(file => IconAttribute().Matches(File.ReadAllText(file))
                .Select(match => (File: RepoPaths.Relative(file), Source: match.Groups["source"].Value)))
            .ToList();
        Assert.NotEmpty(icons);

        Sta.Run(() =>
        {
            foreach (var (file, source) in icons)
            {
                Assert.True(
                    source.StartsWith(ComponentPrefix, StringComparison.Ordinal),
                    $"{file}: Icon=\"{source}\" must start with {ComponentPrefix}.");
                var resource = Application.GetResourceStream(new Uri(source, UriKind.Relative));
                Assert.True(resource is not null, $"{file}: {source} is not a resource in Atap.dll.");
                resource.Stream.Dispose();
            }
        });
    }

    [GeneratedRegex("""\bIcon="(?<source>[^"{]+\.(?:ico|png))"(?=[\s/>])""")]
    private static partial Regex IconAttribute();
}
