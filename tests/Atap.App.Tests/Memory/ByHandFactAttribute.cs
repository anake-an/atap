namespace Atap.Tests.Memory;

/// <summary>
/// A memory guard that runs only when ATAP_MEMORY_TESTS=1. In the full suite, what other tests leave on the shared
/// UI thread makes it pass or fail by test order, so it is run by hand: `set ATAP_MEMORY_TESTS=1` then `dotnet test`.
/// </summary>
public sealed class ByHandFactAttribute : FactAttribute
{
    public ByHandFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ATAP_MEMORY_TESTS") != "1")
            Skip = "Memory guard. Set ATAP_MEMORY_TESTS=1 to run it.";
    }
}
