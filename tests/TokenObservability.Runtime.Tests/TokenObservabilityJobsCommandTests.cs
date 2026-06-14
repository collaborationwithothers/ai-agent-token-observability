using TokenObservability.Jobs;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityJobsCommandTests
{
    private static readonly string[] ExpectedCommands =
    [
        "normalize-telemetry",
        "detect-hotspots",
        "generate-recommendations",
        "redact-content",
        "refresh-pricing",
        "retention-cleanup",
        "reprocess-session",
        "tenant-maintenance"
    ];

    [Fact]
    public void TokenObservabilityJobsExposeDocumentedCommandCatalog()
    {
        var actualCommands = JobCommandCatalog.Commands.Select(command => command.Name).ToArray();

        Assert.Equal(ExpectedCommands, actualCommands);
    }

    [Fact]
    public async Task TokenObservabilityJobsListCommandsEntryPointReturnsSuccess()
    {
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(["--list-commands"], writer);

        Assert.Equal(0, exitCode);
        foreach (var expectedCommand in ExpectedCommands)
        {
            Assert.Contains(expectedCommand, writer.ToString());
        }
    }
}
