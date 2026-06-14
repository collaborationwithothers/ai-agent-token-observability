namespace TokenObservability.Jobs;

public static class TokenObservabilityJobsCommandLine
{
    public static Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count is 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("--list-commands", StringComparer.Ordinal))
        {
            WriteCommandList(output);
            return Task.FromResult(0);
        }

        var commandName = args[0];
        var command = JobCommandCatalog.Commands.SingleOrDefault(candidate => candidate.Name == commandName);
        if (command is null)
        {
            output.WriteLine($"Unknown Token Observability job command: {commandName}");
            WriteCommandList(output);
            return Task.FromResult(2);
        }

        output.WriteLine($"Token Observability job command '{command.Name}' is a placeholder.");
        return Task.FromResult(0);
    }

    private static void WriteCommandList(TextWriter output)
    {
        output.WriteLine("Available Token Observability job commands:");
        foreach (var command in JobCommandCatalog.Commands)
        {
            output.WriteLine($"  {command.Name} - {command.Description}");
        }
    }
}
