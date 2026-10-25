using Atap.Composition;

namespace Atap.Tests.Composition;

public class UninstallCleanupTests
{
    private const string Cli = @"C:\Users\me\AppData\Local\Atap\current\cli";

    [Theory]
    [InlineData(@"C:\Tools;C:\Users\me\AppData\Local\Atap\current\cli;%USERPROFILE%\bin", @"C:\Tools;%USERPROFILE%\bin")]
    [InlineData(@"C:\Users\me\AppData\Local\Atap\current\cli", "")]
    [InlineData(@"C:\Tools;c:\users\me\appdata\local\atap\current\CLI\", @"C:\Tools")]
    [InlineData(@"C:\Tools;""C:\Users\me\AppData\Local\Atap\current\cli""", @"C:\Tools")]
    [InlineData(@"C:\Tools;C:\Users\me\AppData\Local\Atap\current\cli;C:\Users\me\AppData\Local\Atap\current\cli", @"C:\Tools")]
    public void RemoveEntry_TakesOutEveryCopyOfTheEntry(string path, string expected)
    {
        Assert.Equal(expected, UninstallCleanup.RemoveEntry(path, Cli));
    }

    [Theory]
    [InlineData(@"C:\Tools;%USERPROFILE%\bin")]
    [InlineData(@"C:\Users\me\AppData\Local\Atap\current\cli-old")]
    [InlineData("")]
    public void RemoveEntry_WithoutTheEntry_ReturnsNullSoNothingIsWritten(string path)
    {
        Assert.Null(UninstallCleanup.RemoveEntry(path, Cli));
    }

    [Theory]
    [InlineData(@"C:\Tools;%LOCALAPPDATA%\Atap\current\cli;%USERPROFILE%\bin", @"C:\Tools;%USERPROFILE%\bin")]
    [InlineData(@"""%localappdata%\Atap\current\cli\""", "")]
    [InlineData(@"%USERPROFILE%\AppData\Local\Atap\current\cli;C:\Tools", @"C:\Tools")]
    public void RemoveEntry_ExpandsVariablesBeforeComparing(string path, string expected)
    {
        Assert.Equal(expected, UninstallCleanup.RemoveEntry(path, Cli, Expand));
    }

    [Fact]
    public void RemoveEntry_VariableForAnotherFolder_IsKept()
    {
        Assert.Null(UninstallCleanup.RemoveEntry(@"%LOCALAPPDATA%\Atap\current\cli-old;%TEMP%", Cli, Expand));
    }

    [Fact]
    public void CliFolder_IsNextToTheApp()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "cli"), UninstallCleanup.CliFolder);
    }

    // A fixed environment, so the tests never depend on this PC's folders.
    private static string Expand(string value) => value
        .Replace("%LOCALAPPDATA%", @"C:\Users\me\AppData\Local", StringComparison.OrdinalIgnoreCase)
        .Replace("%USERPROFILE%", @"C:\Users\me", StringComparison.OrdinalIgnoreCase);
}
