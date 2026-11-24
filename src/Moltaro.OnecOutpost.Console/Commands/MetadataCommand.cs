using System.Globalization;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
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
// server, and works while the service runs. The token is checked before each step and each Read, so Ctrl+C ends it
// with Cancelled before any file exists. Spectre makes a command for each run, so its fields are the state of one run.
internal sealed partial class MetadataCommand : AsyncCommand<MetadataCommand.Settings>
{
    private const string Command = "metadata";

    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    private readonly WorkerGate _gate;

    public MetadataCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
        _gate = new WorkerGate(parts);
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string key = settings.Base;

        cancellationToken.ThrowIfCancellationRequested();
        SharedSteps.Elevation(_parts, Command);

        cancellationToken.ThrowIfCancellationRequested();
        string file = NewFile(settings.File);

        cancellationToken.ThrowIfCancellationRequested();
        FolderKindEnum dataFolder = SharedSteps.DataFolderKind(_parts);

        cancellationToken.ThrowIfCancellationRequested();
        (AgentBasePolicy served, string password) = Gate(key, dataFolder, cancellationToken);

        IWorkerAccount? account = null;

        try
        {
            account = LogOn(served, cancellationToken);

            MetadataWalk walk = new(_parts, _gate.Program!, served, password, account);

            try
            {
                await WalkAsync(walk, served, cancellationToken);
            }
            finally
            {
                await _console.Status().StartAsync(
                    Markup.Escape($"Closing the base {key}…"),
                    _ => walk.CloseSessionAsync().AsTask());
            }

            cancellationToken.ThrowIfCancellationRequested();
            End(walk, key, file);
        }
        finally
        {
            account?.Dispose();
        }

        return ConsoleExitCodes.Success;
    }

    // Step 4's refusal: the line of check's rule that failed, as check names it, with its reason.
    private static CommandEndException Refused(CheckLine line)
    {
        return new CommandEndException(
            ConsoleExitCodes.CheckFailed,
            $"the line {line.Name} of check failed, so metadata starts no worker: {line.Reason}");
    }

    // The sentence of StartChecks.MissingPasswords for a password that the worker of the base needs and secrets.dat
    // lacks: its read password, then its worker account's. The write password, which metadata never uses, and every
    // other base's are not judged.
    private static string? MissingPassword(AgentPolicy policy, AgentBasePolicy served, AgentSecrets secrets)
    {
        // The base without its write mode needs its read password alone.
        if (StartChecks.MissingPasswords(policy with { Bases = [served with { Write = null }] }, secrets) is [string read, ..])
        {
            return read;
        }

        // MissingPasswords names the worker account last of the passwords of a base.
        if (served.Write?.WorkerAccount is not null && !secrets.WorkerPasswords.ContainsKey(served.Key))
        {
            return StartChecks.MissingPasswords(policy with { Bases = [served] }, secrets)[^1];
        }

        return null;
    }

    // Step 2: the file made full, which may be neither a file nor a folder that exists, and whose folder exists and is
    // not, by its final path judged now, the data folder or a folder inside it, so that a short name, a subst drive or a
    // junction does not lead the report into the data folder.
    private string NewFile(string given)
    {
        string file = Path.GetFullPath(given);

        if (File.Exists(file) || Directory.Exists(file))
        {
            throw new CommandEndException(ConsoleExitCodes.Usage, $"{file} exists; metadata never replaces a file: name a new one");
        }

        if (Path.GetDirectoryName(file) is not string folder || !Directory.Exists(folder))
        {
            throw new CommandEndException(ConsoleExitCodes.Usage, $"the folder of {file} does not exist");
        }

        string finalFolder;
        string dataFolder;

        try
        {
            finalFolder = _parts.Folders.FinalPath(folder);
            dataFolder = _parts.Folders.FinalPath(_parts.DataFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("file", exception);
        }

        if (string.Equals(finalFolder, dataFolder, StringComparison.OrdinalIgnoreCase) || SharedSteps.IsInside(finalFolder, dataFolder))
        {
            throw new CommandEndException(
                ConsoleExitCodes.Usage,
                $"{file} lies in the data folder {_parts.DataFolder}, which metadata never writes");
        }

        return file;
    }

    // Step 4, check's rule before a worker starts, behind check's spinners: the files, of which policy.json and
    // secrets.dat have to be read; the base in the policy; the passwords its worker needs; then the connector and the
    // access lists, a worker starting only when WorkerInputFailed gives none. The base and its read password.
    private (AgentBasePolicy Served, string Password) Gate(string key, FolderKindEnum dataFolder, CancellationToken cancellationToken)
    {
        _console.Status().Start("Reading policy.json, agent.json and secrets.dat…", _ => _gate.Files());

        if (!_gate.PolicyLine.Passed)
        {
            throw Refused(_gate.PolicyLine);
        }

        if (_gate.Secrets is not AgentSecrets secrets)
        {
            throw Refused(_gate.SecretsLine);
        }

        AgentPolicy policy = _gate.Policy!;

        if (policy.Bases.SingleOrDefault(basePolicy => basePolicy.Key == key) is not AgentBasePolicy served)
        {
            throw new CommandEndException(ConsoleExitCodes.NotInPolicy, $"the policy names no base {key}");
        }

        if (MissingPassword(policy, served, secrets) is string missing)
        {
            throw Refused(CheckLine.Fail(CheckLineEnum.Files, AgentFolders.SecretsName, missing));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Finding the COM connector…", _ => _gate.Connector());

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading the access lists…", _ => _gate.AccessLists(dataFolder));

        if (_gate.WorkerInputFailed() is CheckLine failed)
        {
            throw Refused(failed);
        }

        return (served, secrets.Passwords[(key, OutpostModeEnum.Read)]);
    }

    // The last line of check's rule: the one logon of the base's worker account, when the policy names one, never
    // retried, since each failure counts toward a lockout. The caller disposes the account.
    private IWorkerAccount? LogOn(AgentBasePolicy served, CancellationToken cancellationToken)
    {
        if (served.Write?.WorkerAccount is not string workerAccount)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        (CheckLine line, IWorkerAccount? account) = _console.Status().Start(
            Markup.Escape($"Logging {workerAccount} on for the base {served.Key}…"),
            _ => _gate.WorkerAccount(served.Key, workerAccount));

        if (!line.Passed)
        {
            throw Refused(line);
        }

        return account;
    }

    // Steps 5 to 9: the session, behind its spinner; the census and the probe, behind theirs; then the pages and the
    // common storage, behind one progress bar, in which a session reopened shows no spinner, since Spectre runs one live
    // display at a time; and the end of a walk in which no page was read.
    private async Task WalkAsync(MetadataWalk walk, AgentBasePolicy served, CancellationToken cancellationToken)
    {
        string key = served.Key;
        string account = served.Read.Account;

        cancellationToken.ThrowIfCancellationRequested();

        string? reason = await _console.Status().StartAsync(
            Markup.Escape($"Opening the base {key} as {account}…"),
            _ => walk.OpenAsync(cancellationToken));

        if (reason is not null)
        {
            throw new CommandEndException(ConsoleExitCodes.ReportFailed, $"the base {key} did not open as {account}: {reason}; run check");
        }

        await _console.Status().StartAsync(
            Markup.Escape($"Reading the census and the probe of {key}…"),
            async _ =>
            {
                await walk.CensusAsync(cancellationToken);
                await walk.ProbeAsync(cancellationToken);
            });

        await _console.Progress().StartAsync(async progress =>
        {
            ProgressTask task = progress.AddTask(Markup.Escape($"Reading the objects of {key}"), maxValue: walk.Steps);

            await walk.PagesAsync(steps => task.Increment(steps), cancellationToken);
        });

        if (walk.NoPageRead is string end)
        {
            throw new CommandEndException(ConsoleExitCodes.ReportFailed, $"no page of the walk was read: {end}");
        }
    }

    // The table of the categories in the census's order, the storage that could not be read, step 10, and the line of
    // the report written.
    private void End(MetadataWalk walk, string key, string file)
    {
        Table table = new Table()
            .AddColumn("Category")
            .AddColumn(new TableColumn("Found").RightAligned())
            .AddColumn(new TableColumn("Read").RightAligned())
            .AddColumn(new TableColumn("Failed").RightAligned());

        foreach (WalkCategory category in walk.Categories)
        {
            table.AddRow(
                new Text(category.Name),
                new Text(category.Found.ToString(CultureInfo.InvariantCulture)),
                new Text(category.Read.ToString(CultureInfo.InvariantCulture)),
                new Text(category.Failed.ToString(CultureInfo.InvariantCulture)));
        }

        if (walk.CensusErrors.Count > 0)
        {
            table.Caption(Markup.Escape($"Not read: {string.Join(", ", walk.CensusErrors)}"));
        }

        _console.Write(table);

        if (walk.StorageReason is string reason)
        {
            _console.WriteLine($"The storage structure could not be read: {reason}; the report holds the objects and their fields without it.");
        }

        _console.Status().Start(Markup.Escape($"Writing the report to {file}…"), _ => WriteReport(walk, key, file));

        int found = walk.Categories.Sum(category => category.Found);
        int read = walk.Categories.Sum(category => category.Read);
        int failed = walk.Categories.Sum(category => category.Failed);

        _console.WriteLine($"Wrote the metadata report of the base {key} to {file}: {read} of {found} objects read, {failed} failed.");
    }

    // Step 10: the report under a temporary name of its own beside the file, created new, flushed to the disk and
    // closed, then moved to the file without overwriting, so that it is written whole or not at all and never replaces a
    // file, not even one made during the walk. A failure removes the temporary file.
    private void WriteReport(MetadataWalk walk, string key, string file)
    {
        string temporary = $"{file}.{Guid.NewGuid():N}.tmp";

        try
        {
            try
            {
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    MetadataJson.Write(stream, key, _parts.Clock.GetUtcNow(), walk);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, file, overwrite: false);
            }
            catch
            {
                File.Delete(temporary);
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CommandEndException.StepFailed("report", exception);
        }
    }
}
