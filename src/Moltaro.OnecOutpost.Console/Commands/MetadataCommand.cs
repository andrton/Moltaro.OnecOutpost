using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// metadata <BASE> <FILE> (the S10 brief, "Internal of S10c"): elevation; the file, a new one in a folder that exists and
// outside the data folder, judged now; the data folder, absent giving NotInstalled; the gate of check's rule
// (WorkerGate), a line of it that failed giving CheckFailed and no worker started, and a base the policy does not name
// NotInPolicy; then one worker session of the base's read account, in read mode and in safe mode, under its worker
// account or the console's own restricted token, which reads the census, the probe, the pages and the common storage by
// MetadataScripts.Walk; and the report written whole under a temporary name and renamed, never replacing a file.
// ReportFailed when the base does not open, the census or the probe fails, the configuration has no object or no page
// is read, with no file written. It writes nothing to a base or the data folder, runs Reads alone, never reaches the
// server, and works while the service runs.
internal sealed partial class MetadataCommand : AsyncCommand<MetadataCommand.Settings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public MetadataCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("metadata is S10c's implementation.");
    }
}
