using System.IO;
using Atap.Core.Storage;
using Atap.Diagnostics;

namespace Atap.Tests.Diagnostics;

public sealed class RunMarkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Atap.Tests", "run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ANormalRun_LeavesNoMarker_AndACrashedOneDoes()
    {
        var paths = new AtapPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));

        RunMarker.Start(paths);
        Assert.True(File.Exists(paths.RunMarker));
        RunMarker.Stop(paths);
        Assert.False(File.Exists(paths.RunMarker));

        // A run that never reached Stop leaves the marker for the next start to report.
        RunMarker.Start(paths);
        RunMarker.Start(paths);
        Assert.True(File.Exists(paths.RunMarker));
    }
}
