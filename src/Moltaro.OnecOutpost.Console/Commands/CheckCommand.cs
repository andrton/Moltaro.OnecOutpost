using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// check [--clock-skew <TIME>]: elevation; the data folder, absent giving NotInstalled; then the lines of CheckLineEnum,
// each passed or failed with its reason, shown as one table in that order, and Success only when every line passed,
// else CheckFailed. It writes nothing to a base or to the data folder, runs only Reads of its own scripts, never polls,
// and works while the service runs.
//
// The steps, each behind a spinner and after a check of the token: Files, Connector, AccessLists and AgentRights; per
// base in the policy's order WorkerAccount and WorkerStart for a base with a worker account, then the connection of the
// read account, in whose session the scripts of Subscriptions, for a base with write, and of ReadRights run before its
// worker closes, and then the connection of the write account, so that check runs one worker, and holds one client
// licence of 1C, at a time; then Server and ClockSkew. A line that needs another that failed is not checked and says
// which: the workers start only with a data folder that admits nobody else, from a program folder and with a connector
// whose folders nobody else may change, since check never starts code that somebody else may have changed or a base
// that somebody else may have named; a base with a worker account only after its logon and its probe passed; a
// connection only with its password. Each worker account is logged on once, never retried, since each failure counts
// toward a lockout, and closed with its base. A worker of a base without a worker account runs under a restricted
// version of the console's own token, so that the code of a base does not run as an administrator. Spectre makes a
// command for each run, so its fields are the state of one run, written by one task at a time.
internal sealed partial class CheckCommand : AsyncCommand<CheckCommand.Settings>
{
    private const string Command = "check";

    // The lines so far, in the order of the steps; the table shows them in the order of CheckLineEnum, stably.
    private readonly List<CheckLine> _lines = [];

    // The console of the lines and the parts of the machine.
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    // The files of the data folder, each null when its line failed, and their lines.
    private AgentPolicy? _policy;

    private AgentPairingSettings? _pairing;

    private AgentSecrets? _secrets;

    private CheckLine _policyLine = null!;

    private CheckLine _pairingLine = null!;

    private CheckLine _secretsLine = null!;

    // The findings of the connector, the worker program of the first registered bitness, null without one, and the
    // lines that a worker needs: the connector's, the data folder's, the program folder's and that of the folder of the
    // connector it starts with, null without a registered one.
    private IReadOnlyList<ConnectorFinding> _findings = [];

    private ConnectorBitnessEnum? _bitness;

    private string? _program;

    private CheckLine _connectorLine = null!;

    private CheckLine _dataFolderLine = null!;

    private CheckLine _programFolderLine = null!;

    private CheckLine? _connectorFolderLine;

    public CheckCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedSteps.Elevation(_parts, Command);

        cancellationToken.ThrowIfCancellationRequested();
        FolderKindEnum dataFolder = SharedSteps.DataFolderKind(_parts);

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading policy.json, agent.json and secrets.dat…", _ => CheckFiles());

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Finding the COM connector…", _ => CheckConnector());

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading the access lists…", _ => CheckAccessLists(dataFolder));

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading the rights of the agent's account…", _ => CheckAgentRights());

        foreach (AgentBasePolicy served in _policy?.Bases ?? [])
        {
            await CheckBaseAsync(served, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CheckServerAsync(settings.ClockSkew, cancellationToken);

        return End();
    }

    // The name of a line in the cell Check of the table: its kind and its subject.
    private static string NameOf(CheckLine line)
    {
        return line.Subject.Length == 0 ? line.Kind.ToString() : $"{line.Kind} {line.Subject}";
    }

    private static CheckLine Passed(CheckLineEnum kind, string subject, string reason)
    {
        return new CheckLine(kind, subject, true, reason);
    }

    private static CheckLine Failed(CheckLineEnum kind, string subject, string reason)
    {
        return new CheckLine(kind, subject, false, reason);
    }

    // A line that needs the input, which failed.
    private static CheckLine NotChecked(CheckLineEnum kind, string subject, CheckLine input)
    {
        return Failed(kind, subject, $"not checked: {NameOf(input)} failed");
    }

    // The first of the lines that failed, or null.
    private static CheckLine? FirstFailed(params CheckLine?[] inputs)
    {
        return inputs.FirstOrDefault(input => input is { Passed: false });
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.InvariantCulture)} {noun}s";
    }

    // The connector of the bitness at its path, with its version.
    private static string Describe(ConnectorBitnessEnum bitness, ConnectorFile file)
    {
        string bits = bitness == ConnectorBitnessEnum.X64 ? "64-bit" : "32-bit";

        return $"{bits}: {file.Path}, {file.Version}";
    }

    // The account of the SID as Windows names it, or the SID when Windows cannot name it, as off Windows, where the
    // tests on every platform run.
    private static string AccountNameOf(string sid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return sid;
        }

        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid;
        }
    }

    // What the worker wrote to its standard error, which holds no password, or that it wrote nothing.
    private static string DiagnosticsOf(string diagnostics)
    {
        string text = diagnostics.ReplaceLineEndings("\n").Trim();

        return text.Length == 0 ? "the worker gave no reason" : text;
    }

    // The sentence of an error of the call that starts the probe. The program's folder is the worker's working folder,
    // which the creation of the process judges before the program, with 267, ERROR_DIRECTORY.
    private static string ProbeFailure(Win32Exception exception, string account, string program)
    {
        return exception.NativeErrorCode switch
        {
            1314 => "the console lacks \"Impersonate a client after authentication\"",
            1058 => "the Secondary Logon service is disabled; check does not start it",
            2 or 3 => $"{program} is missing",
            5 => $"{account} may not read or run {program}",
            267 => $"{account} may not read the folder of {program}, or it does not exist",
            _ => $"{exception.Message} (Windows error {exception.NativeErrorCode})",
        };
    }

    // The sentence of the probe's end: 0 at the end of its input, which the worker reads first; or the code of a worker
    // that ended otherwise, 1 being the COM worker's refusal of its command line; or null for one ended at the deadline.
    private static CheckLine ProbeEnd(string subject, string account, string desktop, uint? exitCode, TimeSpan deadline)
    {
        return exitCode switch
        {
            0 => Passed(CheckLineEnum.WorkerStart, subject, $"the worker started under {account} on {desktop} and ended at the end of its input"),
            0xC0000142 => Failed(
                CheckLineEnum.WorkerStart,
                subject,
                "the worker failed to initialise on its desktop (STATUS_DLL_INIT_FAILED): the account or the desktop lacks access"),
            1 => Failed(
                CheckLineEnum.WorkerStart,
                subject,
                "the worker refused its command line (exit code 1): the program folder holds another build"),
            uint code => Failed(CheckLineEnum.WorkerStart, subject, $"the worker ended with exit code 0x{code:X8}"),
            null => Failed(CheckLineEnum.WorkerStart, subject, $"the worker did not end within {deadline:c} and was ended"),
        };
    }

    // policy.json, agent.json and secrets.dat, read by StartChecks as the agent's start reads them, with its reasons;
    // secrets.dat also names every password that the policy needs and that it lacks, and is not judged without a policy.
    private void CheckFiles()
    {
        IAgentFolder root = _parts.Data.Root;
        IReadOnlyList<string> names = ListRoot();

        try
        {
            _policy = StartChecks.ReadPolicy(root, names);
            _policyLine = Passed(CheckLineEnum.Files, AgentFolders.PolicyName, Plural(_policy.Bases.Count, "base"));
        }
        catch (AgentPolicyException exception)
        {
            _policyLine = Failed(CheckLineEnum.Files, AgentFolders.PolicyName, $"{AgentFolders.PolicyName} is not valid: {exception.Message}");
        }

        try
        {
            _pairing = StartChecks.ReadPairing(root, names);
            _pairingLine = Passed(CheckLineEnum.Files, AgentFolders.PairingName, $"paired with {_pairing.Server} as {_pairing.Agent}");
        }
        catch (AgentStopException exception)
        {
            _pairingLine = Failed(CheckLineEnum.Files, AgentFolders.PairingName, exception.Message);
        }

        try
        {
            _secrets = StartChecks.ReadSecrets(root, names, _parts.Protection);
            _secretsLine = SecretsLine(_secrets);
        }
        catch (AgentStopException exception)
        {
            _secretsLine = Failed(CheckLineEnum.Files, AgentFolders.SecretsName, exception.Message);
        }

        _lines.AddRange([_policyLine, _pairingLine, _secretsLine]);
    }

    // The names of the files of the data folder; a folder that Windows does not list ends check with Failure.
    private IReadOnlyList<string> ListRoot()
    {
        try
        {
            return _parts.Data.Root.Names();
        }
        catch (IOException exception)
        {
            throw CommandEndException.StepFailed("files", exception);
        }
    }

    // The line of secrets.dat that decrypted: every password that the policy needs, one per line of the reason.
    private CheckLine SecretsLine(AgentSecrets secrets)
    {
        if (_policy is null)
        {
            return NotChecked(CheckLineEnum.Files, AgentFolders.SecretsName, _policyLine);
        }

        IReadOnlyList<string> missing = StartChecks.MissingPasswords(_policy, secrets);

        if (missing.Count > 0)
        {
            return Failed(CheckLineEnum.Files, AgentFolders.SecretsName, string.Join("\n", missing));
        }

        return Passed(CheckLineEnum.Files, AgentFolders.SecretsName, "decrypts and holds every password the policy needs");
    }

    // The first registered connector, 64 bits first, with which the workers start as the service starts them, every
    // other copy named; without one, every copy installed, with the command that registers it.
    private void CheckConnector()
    {
        _findings = _parts.Connectors.Find();

        ConnectorFinding? first = _findings.FirstOrDefault(finding => finding.Registered is not null);
        List<string> unregistered = [.. _findings.SelectMany(finding => finding.Unregistered.Select(copy => Describe(finding.Bitness, copy)))];

        if (first is null)
        {
            string reason = unregistered.Count == 0
                ? "none is registered or installed"
                : $"none is registered; {string.Join("; ", unregistered)}; register it with connector register";

            _connectorLine = Failed(CheckLineEnum.Connector, "", reason);
        }
        else
        {
            _bitness = first.Bitness;
            _program = ProgramFolderLayout.WorkerOf(_parts.ProgramFolder, first.Bitness);

            List<string> copies = [$"{Describe(first.Bitness, first.Registered!)}, is registered; the workers start with it"];

            foreach (ConnectorFinding finding in _findings)
            {
                if (finding.Registered is ConnectorFile registered && finding.Bitness != first.Bitness)
                {
                    copies.Add($"{Describe(finding.Bitness, registered)}, is registered too");
                }
            }

            copies.AddRange(unregistered.Select(copy => $"{copy}, is installed and not registered"));

            _connectorLine = Passed(CheckLineEnum.Connector, "", string.Join("; ", copies));
        }

        _lines.Add(_connectorLine);
    }

    // The access lists for the agent's account: of the data folder and of each entry that the agent's start judges; of
    // the program folder; and of the folder of each registered connector, by the rule of the program folder.
    private void CheckAccessLists(FolderKindEnum dataFolder)
    {
        IAccessLists access = _parts.AccessLists(_parts.AgentSid);

        _dataFolderLine = DataFolderLine(access, dataFolder);
        _lines.Add(_dataFolderLine);

        _programFolderLine = WritersLine(access, _parts.ProgramFolder);
        _lines.Add(_programFolderLine);

        foreach (ConnectorFinding finding in _findings)
        {
            if (finding.Registered is not ConnectorFile registered)
            {
                continue;
            }

            CheckLine line = WritersLine(access, Path.GetDirectoryName(registered.Path)!);
            _lines.Add(line);

            if (finding.Bitness == _bitness)
            {
                _connectorFolderLine = line;
            }
        }
    }

    // Each entry that the start judges, StartChecks.DataPaths of the files listed: the audit files by the log's own rule
    // and the files of the outbox. The service may cut, rotate or send a file while check lists the folder, so a file
    // gone before its list counts as gone, the one difference from the start, which owns the folder.
    private CheckLine DataFolderLine(IAccessLists access, FolderKindEnum dataFolder)
    {
        string folder = _parts.DataFolder;

        if (dataFolder == FolderKindEnum.ReparsePoint)
        {
            return Failed(CheckLineEnum.AccessLists, folder, $"{folder} is a reparse point; inspect it and correct it, never remove what it holds");
        }

        List<string> reasons = [];
        IReadOnlyList<string> audit = [.. List(_parts.Data.Audit, AgentFolders.AuditName, reasons).Where(AuditLog.IsAuditFile).Order(StringComparer.Ordinal)];
        IReadOnlyList<string> outbox = List(_parts.Data.Outcomes, AgentFolders.OutcomesName, reasons);
        string[] folders = ["", AgentFolders.AuditName, AgentFolders.OutcomesName];
        int files = 0;

        foreach (string path in StartChecks.DataPaths(ListRoot(), audit, outbox))
        {
            bool isFile = !folders.Contains(path);
            OtherAccounts others;

            try
            {
                others = access.Others(path);
            }
            catch (FileNotFoundException) when (isFile)
            {
                continue;
            }
            catch (IOException exception)
            {
                reasons.Add($"the access list of {(path.Length == 0 ? "the data folder" : path)} cannot be read: {CommandEndException.ReasonOf(exception)}");
                continue;
            }

            if (isFile)
            {
                files++;
            }

            if (StartChecks.AccessReason(path, others) is string reason)
            {
                reasons.Add(reason);
            }
        }

        if (reasons.Count > 0)
        {
            return Failed(CheckLineEnum.AccessLists, folder, string.Join("\n", reasons));
        }

        return Passed(CheckLineEnum.AccessLists, folder, $"admits nobody else, nor do its {Plural(files, "file")}");
    }

    // The names of the files of a folder of the data folder; one that cannot be listed adds its reason and lists none.
    private static IReadOnlyList<string> List(IAgentFolder folder, string name, List<string> reasons)
    {
        try
        {
            return folder.Names();
        }
        catch (IOException exception)
        {
            reasons.Add($"{name} cannot be listed: {CommandEndException.ReasonOf(exception)}");

            return [];
        }
    }

    // The entries through which somebody else may change the code of the folder, one per line of the reason.
    private CheckLine WritersLine(IAccessLists access, string folder)
    {
        IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> writers;

        try
        {
            writers = access.Writers(folder);
        }
        catch (IOException exception)
        {
            return Failed(CheckLineEnum.AccessLists, folder, $"the access lists of {folder} cannot be read: {CommandEndException.ReasonOf(exception)}");
        }

        if (writers.Count == 0)
        {
            return Passed(CheckLineEnum.AccessLists, folder, "nobody else may change it");
        }

        return Failed(
            CheckLineEnum.AccessLists,
            folder,
            string.Join("\n", writers.Select(writer => StartChecks.WritersReason(writer.Path, writer.Accounts))));
    }

    // The two privileges that LSA shows the agent's account itself, by AgentSid. Limitations: a grant through a group,
    // such as NT SERVICE\ALL SERVICES, is not seen, and the service has a right granted after its start from its next
    // start.
    private void CheckAgentRights()
    {
        string account = AccountNameOf(_parts.AgentSid);
        CheckLine line;

        try
        {
            IReadOnlyList<string> held = _parts.Rights.Held(_parts.AgentSid);

            line = AccountRightNames.AgentPrivileges.All(held.Contains)
                ? Passed(CheckLineEnum.AgentRights, "", $"{account} holds \"Replace a process level token\" and \"Adjust memory quotas for a process\"")
                : Failed(CheckLineEnum.AgentRights, "", $"{account} {WorkerAccountReasons.AgentLacksPrivileges}");
        }
        catch (Win32Exception exception)
        {
            line = Failed(
                CheckLineEnum.AgentRights,
                "",
                $"the rights of {account} cannot be read: {exception.Message} (Windows error {exception.NativeErrorCode})");
        }

        _lines.Add(line);
    }

    // The lines of one base: its worker account and its probe when the policy names one, then its connections. The
    // account is closed with the base.
    private async Task CheckBaseAsync(AgentBasePolicy served, CancellationToken cancellationToken)
    {
        IWorkerAccount? account = null;
        CheckLine? accountLine = null;
        CheckLine? startLine = null;

        try
        {
            if (served.Write?.WorkerAccount is string workerAccount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (accountLine, account) = _console.Status().Start(
                    Markup.Escape($"Logging {workerAccount} on for the base {served.Key}…"),
                    _ => WorkerAccountLine(served.Key, workerAccount));
                _lines.Add(accountLine);

                cancellationToken.ThrowIfCancellationRequested();
                startLine = await _console.Status().StartAsync(
                    Markup.Escape($"Starting the worker under {workerAccount}…"),
                    _ => WorkerStartLineAsync(served, workerAccount, account, accountLine, cancellationToken));
                _lines.Add(startLine);
            }

            await CheckConnectionAsync(served, OutpostModeEnum.Read, served.Read, account, accountLine, startLine, cancellationToken);

            if (served.Write is AgentModePolicy write)
            {
                await CheckConnectionAsync(served, OutpostModeEnum.Write, write, account, accountLine, startLine, cancellationToken);
            }
        }
        finally
        {
            account?.Dispose();
        }
    }

    // The line WorkerAccount: one batch logon with the password of secrets.dat, then the rule of the token. The account
    // is given only when the line passed.
    private (CheckLine Line, IWorkerAccount? Account) WorkerAccountLine(string baseKey, string workerAccount)
    {
        string subject = $"{baseKey} {workerAccount}";

        if (_secrets is null || !_secrets.WorkerPasswords.TryGetValue(baseKey, out string? password))
        {
            return (NotChecked(CheckLineEnum.WorkerAccount, subject, _secretsLine), null);
        }

        IWorkerAccount account;

        try
        {
            account = _parts.Workers.LogOn(baseKey, workerAccount, password);
        }
        catch (Win32Exception exception)
        {
            return (CannotLogOn(subject, workerAccount, baseKey, exception), null);
        }

        bool ordinary;

        try
        {
            ordinary = account.IsOrdinary();
        }
        catch (Win32Exception exception)
        {
            account.Dispose();

            return (CannotLogOn(subject, workerAccount, baseKey, exception), null);
        }

        if (!ordinary)
        {
            account.Dispose();

            return (Failed(CheckLineEnum.WorkerAccount, subject, $"{workerAccount} {WorkerAccountReasons.NotOrdinary}"), null);
        }

        return (Passed(CheckLineEnum.WorkerAccount, subject, $"{workerAccount} logs on for a batch job, and its token is an ordinary user's"), account);
    }

    private static CheckLine CannotLogOn(string subject, string workerAccount, string baseKey, Win32Exception exception)
    {
        return Failed(
            CheckLineEnum.WorkerAccount,
            subject,
            $"{workerAccount} cannot log on: {WorkerAccountReasons.CannotLogOn(baseKey, exception.NativeErrorCode)}");
    }

    // The line WorkerStart: the account's desktop, then the probe of the program under it, within the run limit of the
    // base's write mode, the deadline of the service for a worker of those Writes to open.
    private async Task<CheckLine> WorkerStartLineAsync(
        AgentBasePolicy served,
        string workerAccount,
        IWorkerAccount? account,
        CheckLine accountLine,
        CancellationToken cancellationToken)
    {
        string subject = $"{served.Key} {workerAccount}";

        if (FirstFailed(WorkerInputFailed(), accountLine) is CheckLine input)
        {
            return NotChecked(CheckLineEnum.WorkerStart, subject, input);
        }

        string desktop;

        try
        {
            desktop = account!.Desktop();
        }
        catch (Win32Exception exception)
        {
            return Failed(CheckLineEnum.WorkerStart, subject, $"{workerAccount} cannot get a desktop: {exception.Message}");
        }

        TimeSpan deadline = served.Write!.RunLimit;
        uint? exitCode;

        try
        {
            exitCode = await _parts.Workers.ProbeAsync(account, _program!, served.Key, deadline, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return Failed(CheckLineEnum.WorkerStart, subject, ProbeFailure(exception, workerAccount, _program!));
        }
        catch (IOException exception)
        {
            return Failed(CheckLineEnum.WorkerStart, subject, CommandEndException.ReasonOf(exception));
        }

        return ProbeEnd(subject, workerAccount, desktop, exitCode, deadline);
    }

    // The first line that every worker needs and that failed: the connector; the data folder, in which somebody else
    // could have planted the policy whose base it opens and the secrets it opens it with; the program folder and the
    // folder of the connector it starts with, whose code it runs.
    private CheckLine? WorkerInputFailed()
    {
        return FirstFailed(_connectorLine, _dataFolderLine, _programFolderLine, _connectorFolderLine);
    }

    // The line Connection of the mode's account, and for the read account the lines Subscriptions, for a base with
    // write, and ReadRights, whose scripts run in its session: a worker in read mode and in safe mode, under the base's
    // worker account or the console's own token, as the service runs every worker of the base.
    private async Task CheckConnectionAsync(
        AgentBasePolicy served,
        OutpostModeEnum mode,
        AgentModePolicy policy,
        IWorkerAccount? account,
        CheckLine? accountLine,
        CheckLine? startLine,
        CancellationToken cancellationToken)
    {
        string subject = $"{served.Key} {policy.Account}";
        bool isRead = mode == OutpostModeEnum.Read;
        string? password = null;
        bool hasPassword = _secrets is not null && _secrets.Passwords.TryGetValue((served.Key, mode), out password);

        CheckLine? input = WorkerInputFailed()
            ?? (hasPassword ? null : _secretsLine)
            ?? FirstFailed(accountLine, startLine);

        if (input is not null)
        {
            CheckLine notChecked = NotChecked(CheckLineEnum.Connection, subject, input);

            _lines.Add(notChecked);
            AddSessionLinesNotChecked(served, isRead, notChecked);

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _console.Status().StartAsync(
            Markup.Escape($"Opening the base {served.Key} as {policy.Account}…"),
            _ => SessionAsync(served, policy, password!, isRead, account, cancellationToken));
    }

    // A worker of the mode's account: its line Connection, and for the read account the lines of the scripts that run in
    // its session once that passed. The worker is closed, its job with it, before this returns.
    private async Task SessionAsync(
        AgentBasePolicy served,
        AgentModePolicy policy,
        string password,
        bool isRead,
        IWorkerAccount? account,
        CancellationToken cancellationToken)
    {
        CheckWorker worker;

        try
        {
            worker = _parts.Workers.Start(account, _program!, served.Key);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            string reason = $"{_program} did not start: {CommandEndException.ReasonOf(exception)}";
            CheckLine failed = Failed(CheckLineEnum.Connection, $"{served.Key} {policy.Account}", reason);

            _lines.Add(failed);
            AddSessionLinesNotChecked(served, isRead, failed);

            return;
        }

        try
        {
            CheckLine connection = await ConnectAsync(worker, served, policy, password, cancellationToken);
            _lines.Add(connection);

            if (!isRead || !connection.Passed)
            {
                AddSessionLinesNotChecked(served, isRead, connection);

                return;
            }

            if (served.Write is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (CheckLine subscriptions, bool ended) = await ScriptLineAsync(
                    worker,
                    CheckLineEnum.Subscriptions,
                    served.Key,
                    CheckScripts.Subscriptions,
                    policy.RunLimit,
                    CheckScripts.SubscriptionsOf,
                    cancellationToken);
                _lines.Add(subscriptions);

                // A script after one that ended the worker would only report its end.
                if (ended)
                {
                    _lines.Add(NotChecked(CheckLineEnum.ReadRights, served.Key, subscriptions));

                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            (CheckLine rights, _) = await ScriptLineAsync(
                worker,
                CheckLineEnum.ReadRights,
                served.Key,
                CheckScripts.Rights,
                policy.RunLimit,
                result => CheckScripts.RightsOf(result, policy.Account),
                cancellationToken);
            _lines.Add(rights);
        }
        finally
        {
            worker.Process.Dispose();
        }
    }

    // The lines that run in the session of the read account, not checked since its line failed.
    private void AddSessionLinesNotChecked(AgentBasePolicy served, bool isRead, CheckLine connection)
    {
        if (!isRead)
        {
            return;
        }

        if (served.Write is not null)
        {
            _lines.Add(NotChecked(CheckLineEnum.Subscriptions, served.Key, connection));
        }

        _lines.Add(NotChecked(CheckLineEnum.ReadRights, served.Key, connection));
    }

    // Opens the worker as the account in safe mode and runs the script of its user, each within the mode's run limit.
    // A worker that gives no ready is closed, its job with it, before its diagnostics are awaited, which then end.
    private async Task<CheckLine> ConnectAsync(
        CheckWorker worker,
        AgentBasePolicy served,
        AgentModePolicy policy,
        string password,
        CancellationToken cancellationToken)
    {
        string subject = $"{served.Key} {policy.Account}";
        WorkerConnectionLine connection = WorkerConnectionLine.For(
            served,
            policy with { SafeMode = true },
            password,
            ProgramFolderLayout.ExecutorOf(_parts.ProgramFolder));

        (bool opened, bool overran) = await WithinAsync(
            worker.Process,
            policy.RunLimit,
            () => worker.Process.OpenAsync(connection, cancellationToken));

        if (overran)
        {
            return Failed(CheckLineEnum.Connection, subject, $"1C opened no session within {policy.RunLimit:c}, the mode's runLimit");
        }

        if (!opened)
        {
            worker.Process.Dispose();

            string diagnostics = await worker.Diagnostics.WaitAsync(cancellationToken);

            return Failed(CheckLineEnum.Connection, subject, $"1C opened no session: {DiagnosticsOf(diagnostics)}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // A script that succeeded never ends its worker, and a line that failed is the input of the session's others.
        (CheckLine line, _) = await ScriptLineAsync(
            worker,
            CheckLineEnum.Connection,
            subject,
            CheckScripts.User,
            policy.RunLimit,
            result => CheckScripts.Session(result, policy.Account),
            cancellationToken);

        return line;
    }

    // The line of a script of check: its run within the run limit, its outcome judged as the pool judges one, and the
    // reason that the reader gives of its result; and whether the worker ended, at the run limit or as the judgment
    // says, so that no further script runs in it.
    private async Task<(CheckLine Line, bool EndedWorker)> ScriptLineAsync(
        CheckWorker worker,
        CheckLineEnum kind,
        string subject,
        string script,
        TimeSpan runLimit,
        Func<JsonElement, (bool Passed, string Reason)> read,
        CancellationToken cancellationToken)
    {
        WorkerCommandLine command = new(script, "{}", null, OutpostProtocol.MaxOutcomeSizeLimit);

        ((long Size, byte[] Outcome)? given, bool overran) = await WithinAsync(
            worker.Process,
            runLimit,
            () => worker.Process.RunAsync(command, cancellationToken));

        if (overran)
        {
            return (Failed(kind, subject, $"the script of the check did not end within {runLimit:c}"), true);
        }

        JudgedOutcome judged = WorkerOutcome.Judge(OutpostModeEnum.Read, given);
        OutpostOutcomeMessage outcome = OutpostCodec.ReadOutcome(OutpostModeEnum.Read, judged.Outcome);

        if (outcome.Kind != OutpostAgentOutcomeKindEnum.Succeeded || outcome.Result is not JsonElement result)
        {
            string ended = $"the script of the check ended {OutpostForms.WriteOutcomeKind(outcome.Kind)}";

            if (outcome.Code is string code)
            {
                ended = $"{ended} {code}";
            }

            return (Failed(kind, subject, outcome.ErrorText is string text ? $"{ended}: {text}" : ended), judged.EndsWorker);
        }

        try
        {
            (bool passed, string reason) = read(result);

            return (new CheckLine(kind, subject, passed, reason), judged.EndsWorker);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return (Failed(kind, subject, "the script of the check gave a result of another form"), judged.EndsWorker);
        }
    }

    // Runs a call of the worker raced with the run limit on the clock, the worker killed at it, which ends the call; and
    // whether the limit came. The timer goes with the race.
    private async Task<(T Result, bool Overran)> WithinAsync<T>(IWorkerProcess process, TimeSpan runLimit, Func<Task<T>> call)
    {
        using CancellationTokenSource limit = new(runLimit, _parts.Clock);
        T result;

        using (limit.Token.Register(process.Kill))
        {
            result = await call();
        }

        return (result, limit.IsCancellationRequested);
    }

    // The lines Server and ClockSkew: one outcome of a made-up command, refused as not-addressed, signed with the agent's
    // key and sent to the server of agent.json through the proxy of the policy, as the agent sends its outcomes; never a
    // poll, which would replace the poll that the running service holds.
    private async Task CheckServerAsync(TimeSpan allowed, CancellationToken cancellationToken)
    {
        CheckLine? input = _policy is null ? _policyLine : _pairing is null ? _pairingLine : _secrets is null ? _secretsLine : null;

        if (input is not null)
        {
            _lines.Add(NotChecked(CheckLineEnum.Server, "", input));
            _lines.Add(Failed(CheckLineEnum.ClockSkew, "", "not checked: no verified answer came"));

            return;
        }

        AgentPolicy policy = _policy!;
        AgentPairingSettings pairing = _pairing!;

        // secrets.dat was checked to hold an ECDSA key on P-256 in PKCS#8, and agent.json the server key in the form of
        // §2, as the agent's host reads them.
        using ECDsa agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(_secrets!.PrivateKey, out _);
        using ECDsa serverKey = OutpostKeys.ImportPublicKey(pairing.ServerKey);
        using AgentConnection connection = new(
            pairing.Server,
            pairing.Agent,
            agentKey,
            serverKey,
            _parts.Handlers(pairing.Server, policy.Proxy),
            _parts.Clock);

        DateTimeOffset sent = _parts.Clock.GetUtcNow();
        OutcomeAnswer answer = await _console.Status().StartAsync(
            Markup.Escape($"Sending {pairing.Server} an outcome of a made-up command…"),
            _ => connection.SendOutcomeAsync(
                Guid.NewGuid(),
                OwnOutcome.Write(OutpostAgentOutcomeKindEnum.Refused, OutpostOutcomeCodes.NotAddressed),
                policy.RequestTimeout,
                cancellationToken));
        DateTimeOffset received = _parts.Clock.GetUtcNow();

        _lines.Add(answer.Result == OutcomeResultEnum.Confirmed
            ? Passed(
                CheckLineEnum.Server,
                "",
                $"{pairing.Server} confirmed an outcome of a made-up command from {pairing.Agent}: it is reachable and accepts the agent's key")
            : Failed(
                CheckLineEnum.Server,
                "",
                ServerAnswerSentences.Of(OutpostRequestKindEnum.Outcome, answer.Cause ?? ServerAnswerCauseEnum.Answer, answer.Status, answer.Code)));

        _lines.Add(answer.Time is DateTimeOffset time
            ? SkewLine(time, sent, received, allowed)
            : Failed(CheckLineEnum.ClockSkew, "", "not checked: no verified answer came"));
    }

    // Cristian's method: the server's time against the middle of the round trip on this machine's clock, known within
    // half the round trip and the millisecond of §2, judged within the allowed skew only when that holds either way.
    private static CheckLine SkewLine(DateTimeOffset time, DateTimeOffset sent, DateTimeOffset received, TimeSpan allowed)
    {
        TimeSpan roundTrip = received - sent;
        TimeSpan skew = time - (sent + (roundTrip / 2));
        TimeSpan uncertainty = (roundTrip / 2) + TimeSpan.FromMilliseconds(1);
        TimeSpan size = skew.Duration();

        string clock = skew < TimeSpan.Zero ? "behind" : "ahead of";
        string measured = $"the server's clock is {clock} this machine's by {WholeMilliseconds(size)} ms ± {WholeMilliseconds(uncertainty)} ms";

        if (size + uncertainty <= allowed)
        {
            return Passed(CheckLineEnum.ClockSkew, "", $"{measured}, within {allowed:c}");
        }

        if (size - uncertainty > allowed)
        {
            return Failed(
                CheckLineEnum.ClockSkew,
                "",
                $"{measured}, beyond {allowed:c}; with the server ahead, the agent refuses every command with clock-skew, and with it behind, with expired once the gap also passes the take timeout: set the clocks");
        }

        return Failed(CheckLineEnum.ClockSkew, "", $"cannot be judged within {allowed:c}: the round trip took {WholeMilliseconds(roundTrip)} ms");
    }

    private static string WholeMilliseconds(TimeSpan span)
    {
        return (span.Ticks / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture);
    }

    // The table of the lines in the order of CheckLineEnum, then of the steps, and the end of check.
    private int End()
    {
        Table table = new Table()
            .AddColumn("Check")
            .AddColumn("Result")
            .AddColumn("Reason");

        foreach (CheckLine line in _lines.OrderBy(line => line.Kind))
        {
            table.AddRow(
                new Text(NameOf(line)),
                new Markup(line.Passed ? "[green]passed[/]" : "[red]failed[/]"),
                new Text(line.Reason));
        }

        _console.Write(table);

        int failed = _lines.Count(line => !line.Passed);

        if (failed > 0)
        {
            throw new CommandEndException(ConsoleExitCodes.CheckFailed, $"{failed} of {_lines.Count} lines failed");
        }

        _console.WriteLine("Every line passed.");

        return ConsoleExitCodes.Success;
    }
}
