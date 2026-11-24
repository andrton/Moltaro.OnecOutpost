using System.ComponentModel;
using System.Globalization;
using Moltaro.OnecOutpost.Agent;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Workers;

namespace Moltaro.OnecOutpost.Console.Commands;

// check's rule of when a worker may start, which check and metadata both run (review 3, entry 2): the console never
// starts code that somebody else may have changed, or a base that somebody else may have named. Its steps, in their
// order: Files, Connector, AccessLists, then WorkerAccount per base with a worker account; each gives its lines, which
// check shows and metadata judges. A worker starts only when WorkerInputFailed gives null: with a registered connector,
// a data folder that admits nobody else, in which nobody could have planted the policy whose base it opens or the
// secrets it opens it with, and a program folder and a folder of the connector it starts with that nobody else may
// change, whose code it runs. Each worker account is logged on once, by the caller, never retried, since each failure
// counts toward a lockout. One run of a command makes one gate and calls it from one task at a time.
internal sealed class WorkerGate(ConsoleParts parts)
{
    // The findings of the connector and the bitness of the first registered one, null without one.
    private IReadOnlyList<ConnectorFinding> _findings = [];

    private ConnectorBitnessEnum? _bitness;

    // The lines that every worker needs: the connector's, the data folder's, the program folder's and that of the
    // folder of the connector it starts with, null without a registered one.
    private CheckLine _connectorLine = null!;

    private CheckLine _dataFolderLine = null!;

    private CheckLine _programFolderLine = null!;

    private CheckLine? _connectorFolderLine;

    private ConsoleParts Parts { get; } = parts;

    // The files of the data folder, each null when its line failed, and their lines, once Files ran.
    public AgentPolicy? Policy { get; private set; }

    public AgentPairingSettings? Pairing { get; private set; }

    public AgentSecrets? Secrets { get; private set; }

    public CheckLine PolicyLine { get; private set; } = null!;

    public CheckLine PairingLine { get; private set; } = null!;

    public CheckLine SecretsLine { get; private set; } = null!;

    // The worker program of the first registered bitness, ProgramFolderLayout.WorkerOf, once Connector ran; null
    // without a registered connector.
    public string? Program { get; private set; }

    // policy.json, agent.json and secrets.dat, read by StartChecks as the agent's start reads them, with its reasons;
    // secrets.dat also names every password that the policy needs and that it lacks, and is not judged without a policy.
    // A data folder that Windows does not list ends the command with Failure.
    public IReadOnlyList<CheckLine> Files()
    {
        IAgentFolder root = Parts.Data.Root;
        IReadOnlyList<string> names = ListRoot();

        try
        {
            Policy = StartChecks.ReadPolicy(root, names);
            PolicyLine = CheckLine.Pass(CheckLineEnum.Files, AgentFolders.PolicyName, Plural(Policy.Bases.Count, "base"));
        }
        catch (AgentPolicyException exception)
        {
            PolicyLine = CheckLine.Fail(CheckLineEnum.Files, AgentFolders.PolicyName, StartChecks.PolicyReason(exception));
        }

        try
        {
            Pairing = StartChecks.ReadPairing(root, names);
            PairingLine = CheckLine.Pass(CheckLineEnum.Files, AgentFolders.PairingName, $"paired with {Pairing.Server} as {Pairing.Agent}");
        }
        catch (AgentStopException exception)
        {
            PairingLine = CheckLine.Fail(CheckLineEnum.Files, AgentFolders.PairingName, exception.Message);
        }

        try
        {
            Secrets = StartChecks.ReadSecrets(root, names, Parts.Protection);
            SecretsLine = SecretsLineOf(Secrets);
        }
        catch (AgentStopException exception)
        {
            SecretsLine = CheckLine.Fail(CheckLineEnum.Files, AgentFolders.SecretsName, exception.Message);
        }

        return [PolicyLine, PairingLine, SecretsLine];
    }

    // The first registered connector, 64 bits first, with which the workers start as the service starts them, every
    // other copy named; without one, every copy installed, with the command that registers it.
    public CheckLine Connector()
    {
        _findings = Parts.Connectors.Find();

        ConnectorFinding? first = ConnectorFinding.FirstRegistered(_findings);
        List<string> unregistered = [.. _findings.SelectMany(finding => finding.Unregistered.Select(copy => Describe(finding.Bitness, copy)))];

        if (first is null)
        {
            string reason = unregistered.Count == 0
                ? "none is registered or installed"
                : $"none is registered; {string.Join("; ", unregistered)}; register it with connector register";

            _connectorLine = CheckLine.Fail(CheckLineEnum.Connector, "", reason);

            return _connectorLine;
        }

        _bitness = first.Bitness;
        Program = ProgramFolderLayout.WorkerOf(Parts.ProgramFolder, first.Bitness);

        List<string> copies = [$"{Describe(first.Bitness, first.Registered!)}, is registered; the workers start with it"];

        foreach (ConnectorFinding finding in _findings)
        {
            if (finding.Registered is ConnectorFile registered && finding.Bitness != first.Bitness)
            {
                copies.Add($"{Describe(finding.Bitness, registered)}, is registered too");
            }
        }

        copies.AddRange(unregistered.Select(copy => $"{copy}, is installed and not registered"));

        _connectorLine = CheckLine.Pass(CheckLineEnum.Connector, "", string.Join("; ", copies));

        return _connectorLine;
    }

    // The access lists for the agent's account: of the data folder and of each entry that the agent's start judges; of
    // the program folder; and of the folder of each registered connector, by the rule of the program folder. Connector
    // runs first.
    public IReadOnlyList<CheckLine> AccessLists(FolderKindEnum dataFolder)
    {
        IAccessLists access = Parts.AccessLists(Parts.AgentSid);
        List<CheckLine> lines = [];

        _dataFolderLine = DataFolderLine(access, dataFolder);
        lines.Add(_dataFolderLine);

        _programFolderLine = WritersLine(access, Parts.ProgramFolder);
        lines.Add(_programFolderLine);

        foreach (ConnectorFinding finding in _findings)
        {
            if (finding.Registered is not ConnectorFile registered)
            {
                continue;
            }

            CheckLine line = WritersLine(access, Path.GetDirectoryName(registered.Path)!);
            lines.Add(line);

            if (finding.Bitness == _bitness)
            {
                _connectorFolderLine = line;
            }
        }

        return lines;
    }

    // The line WorkerAccount: one batch logon with the password of secrets.dat, then the rule of the token. The account
    // is given only when the line passed, and the caller disposes it. Files runs first.
    public (CheckLine Line, IWorkerAccount? Account) WorkerAccount(string baseKey, string workerAccount)
    {
        string subject = $"{baseKey} {workerAccount}";

        if (Secrets is null || !Secrets.WorkerPasswords.TryGetValue(baseKey, out string? password))
        {
            return (CheckLine.NotChecked(CheckLineEnum.WorkerAccount, subject, SecretsLine), null);
        }

        IWorkerAccount account;

        try
        {
            account = Parts.Workers.LogOn(baseKey, workerAccount, password);
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

            return (CheckLine.Fail(CheckLineEnum.WorkerAccount, subject, $"{workerAccount} {WorkerAccountReasons.NotOrdinary}"), null);
        }

        return (CheckLine.Pass(CheckLineEnum.WorkerAccount, subject, $"{workerAccount} logs on for a batch job, and its token is an ordinary user's"), account);
    }

    // The first line that every worker needs and that failed: the connector; the data folder, in which somebody else
    // could have planted the policy whose base it opens and the secrets it opens it with; the program folder and the
    // folder of the connector it starts with, whose code it runs. Connector and AccessLists run first.
    public CheckLine? WorkerInputFailed()
    {
        return CheckLine.FirstFailed(_connectorLine, _dataFolderLine, _programFolderLine, _connectorFolderLine);
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

    private static CheckLine CannotLogOn(string subject, string workerAccount, string baseKey, Win32Exception exception)
    {
        return CheckLine.Fail(
            CheckLineEnum.WorkerAccount,
            subject,
            $"{workerAccount} cannot log on: {WorkerAccountReasons.CannotLogOn(baseKey, exception.NativeErrorCode)}");
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

    // The names of the files of the data folder; a folder that Windows does not list ends the command with Failure.
    private IReadOnlyList<string> ListRoot()
    {
        try
        {
            return Parts.Data.Root.Names();
        }
        catch (IOException exception)
        {
            throw CommandEndException.StepFailed("files", exception);
        }
    }

    // The line of secrets.dat that decrypted: every password that the policy needs, one per line of the reason.
    private CheckLine SecretsLineOf(AgentSecrets secrets)
    {
        if (Policy is null)
        {
            return CheckLine.NotChecked(CheckLineEnum.Files, AgentFolders.SecretsName, PolicyLine);
        }

        IReadOnlyList<string> missing = StartChecks.MissingPasswords(Policy, secrets);

        if (missing.Count > 0)
        {
            return CheckLine.Fail(CheckLineEnum.Files, AgentFolders.SecretsName, string.Join("\n", missing));
        }

        return CheckLine.Pass(CheckLineEnum.Files, AgentFolders.SecretsName, "decrypts and holds every password the policy needs");
    }

    // Each entry that the start judges, StartChecks.DataPaths of the files listed: the audit files by the log's own rule
    // and the files of the outbox. The service may cut, rotate or send a file while the console lists the folder, so a
    // file gone before its list counts as gone, the one difference from the start, which owns the folder.
    private CheckLine DataFolderLine(IAccessLists access, FolderKindEnum dataFolder)
    {
        string folder = Parts.DataFolder;

        if (dataFolder == FolderKindEnum.ReparsePoint)
        {
            return CheckLine.Fail(CheckLineEnum.AccessLists, folder, SharedSteps.ReparsePointReason(folder));
        }

        List<string> reasons = [];
        IReadOnlyList<string> audit = [.. List(Parts.Data.Audit, AgentFolders.AuditName, reasons).Where(AuditLog.IsAuditFile).Order(StringComparer.Ordinal)];
        IReadOnlyList<string> outbox = List(Parts.Data.Outcomes, AgentFolders.OutcomesName, reasons);
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
                reasons.Add($"the access list of {StartChecks.EntryOf(path)} cannot be read: {CommandEndException.ReasonOf(exception)}");
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
            return CheckLine.Fail(CheckLineEnum.AccessLists, folder, string.Join("\n", reasons));
        }

        return CheckLine.Pass(CheckLineEnum.AccessLists, folder, $"admits nobody else, nor do its {Plural(files, "file")}");
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
            return CheckLine.Fail(CheckLineEnum.AccessLists, folder, $"the access lists of {folder} cannot be read: {CommandEndException.ReasonOf(exception)}");
        }

        if (writers.Count == 0)
        {
            return CheckLine.Pass(CheckLineEnum.AccessLists, folder, "nobody else may change it");
        }

        return CheckLine.Fail(
            CheckLineEnum.AccessLists,
            folder,
            string.Join("\n", writers.Select(writer => StartChecks.WritersReason(writer.Path, writer.Accounts))));
    }
}
