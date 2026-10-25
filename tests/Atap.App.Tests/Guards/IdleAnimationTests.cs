using Atap.Tests.Infrastructure;

namespace Atap.Tests.Guards;

/// <summary>
/// Nothing animates forever while the notch is idle, so it never wakes the GPU on its own.
/// Only the layout editor's wiggle may loop, and only while editing.
/// </summary>
public class IdleAnimationTests
{
    [Fact]
    public void NoXamlRepeatsForeverOutsideTheEditSurface()
    {
        var allowed = Path.Combine(RepoPaths.App, "Notch", "Surfaces", "Edit") + Path.DirectorySeparatorChar;
        var files = RepoPaths.Files(RepoPaths.Src, "*.xaml").ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(f => !f.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Contains("RepeatBehavior=\"Forever\"", StringComparison.OrdinalIgnoreCase))
            .Select(RepoPaths.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, $"RepeatBehavior=\"Forever\" in: {string.Join(", ", offenders)}");
    }
}
