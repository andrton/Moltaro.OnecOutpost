using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
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
// The steps, each behind a spinner and after a check of the token: Files, Connector and AccessLists of the gate of its
// rule (WorkerGate), and AgentRights; per base in the policy's order WorkerAccount and WorkerStart for a base with a
// worker account, then the session of the read account, whose scripts are Connection, Subscriptions for a base with
// write, and ReadRights, before its worker closes, and then the session of the write account, whose script is its
// Connection, so that check runs one worker, and holds one client licence of 1C, at a time; then Server and ClockSkew. A
// line that needs another that failed is not checked and says which: the workers start only when the gate lets them,
// since check never starts code that somebody else may have changed or a base that somebody else may have named; a
// base with a worker account only after its logon and its probe passed; a connection only with its password. Each
// worker account is logged on once, never retried, since each failure counts toward a lockout, and closed with its base.
// A worker of a base without a worker account runs under a restricted version of the console's own token, at medium
// integrity, so that the code of a base runs without the rights of an administrator and cannot reach the elevated
// terminal of check; it is still the administrator's own account, unelevated. Spectre makes a command for each run, so
// its fields are the state of one run, written by one task at a time.
internal sealed partial class CheckCommand : AsyncCommand<CheckCommand.Settings>
{
    private const string Command = "check";

    // The lines so far, in the order of the steps; the table shows them in the order of CheckLineEnum, stably.
    private readonly List<CheckLine> _lines = [];

    // The console of the lines, the parts of the machine, and the gate of the rule before a worker starts, with the files
    // of the data folder and the worker program it found.
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    private readonly WorkerGate _gate;

    public CheckCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
        _gate = new WorkerGate(parts);
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedSteps.Elevation(_parts, Command);

        cancellationToken.ThrowIfCancellationRequested();
        FolderKindEnum dataFolder = SharedSteps.DataFolderKind(_parts);

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading policy.json, agent.json and secrets.dat…", _ => _lines.AddRange(_gate.Files()));

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Finding the COM connector…", _ => _lines.Add(_gate.Connector()));

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading the access lists…", _ => _lines.AddRange(_gate.AccessLists(dataFolder)));

        cancellationToken.ThrowIfCancellationRequested();
        _console.Status().Start("Reading the rights of the agent's account…", _ => CheckAgentRights());

        foreach (AgentBasePolicy served in _gate.Policy?.Bases ?? [])
        {
            await CheckBaseAsync(served, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CheckServerAsync(settings.ClockSkew, cancellationToken);

        return End();
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
    // 0 and 1 mirror WorkerExitCodes.Ended and BadInput of the COM worker, which the console cannot reference.
    private static CheckLine ProbeEnd(string subject, string account, string desktop, uint? exitCode, TimeSpan deadline)
    {
        return exitCode switch
        {
            0 => CheckLine.Pass(CheckLineEnum.WorkerStart, subject, $"the worker started under {account} on {desktop} and ended at the end of its input"),
            0xC0000142 => CheckLine.Fail(
                CheckLineEnum.WorkerStart,
                subject,
                "the worker failed to initialise on its desktop (STATUS_DLL_INIT_FAILED): the account or the desktop lacks access"),
            1 => CheckLine.Fail(
                CheckLineEnum.WorkerStart,
                subject,
                "the worker refused its command line (exit code 1): the program folder holds another build"),
            uint code => CheckLine.Fail(CheckLineEnum.WorkerStart, subject, $"the worker ended with exit code 0x{code:X8}"),
            null => CheckLine.Fail(CheckLineEnum.WorkerStart, subject, $"the worker did not end within {deadline:c} and was ended"),
        };
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
                ? CheckLine.Pass(CheckLineEnum.AgentRights, "", $"{account} holds \"Replace a process level token\" and \"Adjust memory quotas for a process\"")
                : CheckLine.Fail(CheckLineEnum.AgentRights, "", $"{account} {WorkerAccountReasons.AgentLacksPrivileges}");
        }
        catch (Win32Exception exception)
        {
            line = CheckLine.Fail(
                CheckLineEnum.AgentRights,
                "",
                $"the rights of {account} cannot be read: {exception.Message} (Windows error {exception.NativeErrorCode})");
        }

        _lines.Add(line);
    }

    // The lines of one base: its worker account and its probe when the policy names one, then its sessions. The account
    // is closed with the base.
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
                    _ => _gate.WorkerAccount(served.Key, workerAccount));
                _lines.Add(accountLine);

                cancellationToken.ThrowIfCancellationRequested();
                startLine = await _console.Status().StartAsync(
                    Markup.Escape($"Starting the worker under {workerAccount}…"),
                    _ => WorkerStartLineAsync(served, workerAccount, account, accountLine, cancellationToken));
                _lines.Add(startLine);
            }

            await CheckSessionAsync(served, OutpostModeEnum.Read, served.Read, account, accountLine, startLine, cancellationToken);

            if (served.Write is AgentModePolicy write)
            {
                await CheckSessionAsync(served, OutpostModeEnum.Write, write, account, accountLine, startLine, cancellationToken);
            }
        }
        finally
        {
            account?.Dispose();
        }
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

        if (CheckLine.FirstFailed(_gate.WorkerInputFailed(), accountLine) is CheckLine input)
        {
            return CheckLine.NotChecked(CheckLineEnum.WorkerStart, subject, input);
        }

        string desktop;

        try
        {
            desktop = account!.Desktop();
        }
        catch (Win32Exception exception)
        {
            return CheckLine.Fail(CheckLineEnum.WorkerStart, subject, $"{workerAccount} cannot get a desktop: {exception.Message}");
        }

        TimeSpan deadline = served.Write!.RunLimit;
        string program = _gate.Program!;
        uint? exitCode;

        try
        {
            exitCode = await _parts.Workers.ProbeAsync(account, program, served.Key, deadline, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return CheckLine.Fail(CheckLineEnum.WorkerStart, subject, ProbeFailure(exception, workerAccount, program));
        }
        catch (IOException exception)
        {
            return CheckLine.Fail(CheckLineEnum.WorkerStart, subject, CommandEndException.ReasonOf(exception));
        }

        return ProbeEnd(subject, workerAccount, desktop, exitCode, deadline);
    }

    // The scripts of the mode's session, in their order: the read account's Connection, Subscriptions for a base with
    // write, and ReadRights; the write account's Connection.
    private static IReadOnlyList<SessionScript> ScriptsOf(AgentBasePolicy served, OutpostModeEnum mode, AgentModePolicy policy)
    {
        List<SessionScript> scripts =
        [
            new(CheckLineEnum.Connection, $"{served.Key} {policy.Account}", CheckScripts.User, result => CheckScripts.Session(result, policy.Account)),
        ];

        if (mode != OutpostModeEnum.Read)
        {
            return scripts;
        }

        if (served.Write is not null)
        {
            scripts.Add(new(CheckLineEnum.Subscriptions, served.Key, CheckScripts.Subscriptions, CheckScripts.SubscriptionsOf));
        }

        scripts.Add(new(CheckLineEnum.ReadRights, served.Key, CheckScripts.Rights, result => CheckScripts.RightsOf(result, policy.Account)));

        return scripts;
    }

    // The lines of the mode's session: a worker in read mode and in safe mode, under the base's worker account or the
    // console's own token, as the service runs every worker of the base, once the gate, the password, the worker account
    // and its probe let it start.
    private async Task CheckSessionAsync(
        AgentBasePolicy served,
        OutpostModeEnum mode,
        AgentModePolicy policy,
        IWorkerAccount? account,
        CheckLine? accountLine,
        CheckLine? startLine,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionScript> scripts = ScriptsOf(served, mode, policy);
        string? password = null;
        bool hasPassword = _gate.Secrets is not null && _gate.Secrets.Passwords.TryGetValue((served.Key, mode), out password);

        CheckLine? input = _gate.WorkerInputFailed();

        if (input is null && !hasPassword)
        {
            input = _gate.SecretsLine;
        }

        input ??= CheckLine.FirstFailed(accountLine, startLine);

        if (input is not null)
        {
            AddNotChecked(scripts, CheckLine.NotChecked(scripts[0].Kind, scripts[0].Subject, input));

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _console.Status().StartAsync(
            Markup.Escape($"Opening the base {served.Key} as {policy.Account}…"),
            _ => SessionAsync(served, policy, password!, account, scripts, cancellationToken));
    }

    // A worker of the mode's account and its list of scripts, the first the line Connection: one loop runs the list,
    // and when the first line fails, the session does not open or a script ends the worker, every later one is not
    // checked by that line. The worker is closed, its job with it, before this returns.
    private async Task SessionAsync(
        AgentBasePolicy served,
        AgentModePolicy policy,
        string password,
        IWorkerAccount? account,
        IReadOnlyList<SessionScript> scripts,
        CancellationToken cancellationToken)
    {
        string program = _gate.Program!;
        WorkerConnectionLine connection = WorkerConnectionLine.For(
            served,
            policy with { SafeMode = true },
            password,
            ProgramFolderLayout.ExecutorOf(_parts.ProgramFolder));
        WorkerSession? session;
        string? reason;

        try
        {
            (session, reason) = await WorkerSession.OpenAsync(
                _parts.Workers,
                account,
                program,
                served.Key,
                connection,
                policy.RunLimit,
                _parts.Clock,
                cancellationToken);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            session = null;
            reason = $"{program} did not start: {CommandEndException.ReasonOf(exception)}";
        }

        if (session is null)
        {
            AddNotChecked(scripts, CheckLine.Fail(scripts[0].Kind, scripts[0].Subject, reason!));

            return;
        }

        using (session)
        {
            for (int index = 0; index < scripts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CheckLine line = await ScriptLineAsync(session, scripts[index], policy.RunLimit, cancellationToken);
                _lines.Add(line);

                // A script after one that ended the worker would only report its end, and a first line that failed is
                // the input of the others.
                if ((index == 0 && !line.Passed) || session.Ended)
                {
                    AddNotChecked([.. scripts.Skip(index + 1)], line, first: false);

                    return;
                }
            }
        }
    }

    // The first line of the scripts, unless first is false, then every other script as not checked by it.
    private void AddNotChecked(IReadOnlyList<SessionScript> scripts, CheckLine line, bool first = true)
    {
        int start = 0;

        if (first)
        {
            _lines.Add(line);
            start = 1;
        }

        for (int index = start; index < scripts.Count; index++)
        {
            _lines.Add(CheckLine.NotChecked(scripts[index].Kind, scripts[index].Subject, line));
        }
    }

    // The line of a script of check: its Read in the session within the run limit, its outcome judged as the pool judges
    // one, and the reason that the reader gives of its result.
    private static async Task<CheckLine> ScriptLineAsync(
        WorkerSession session,
        SessionScript script,
        TimeSpan runLimit,
        CancellationToken cancellationToken)
    {
        (OutpostOutcomeMessage? outcome, _) = await session.ReadAsync(script.Script, "{}", cancellationToken);

        if (outcome is null)
        {
            return CheckLine.Fail(script.Kind, script.Subject, $"the script of the check did not end within {runLimit:c}");
        }

        if (outcome.Kind != OutpostAgentOutcomeKindEnum.Succeeded || outcome.Result is not JsonElement result)
        {
            string ended = $"the script of the check ended {OutpostForms.WriteOutcomeKind(outcome.Kind)}";

            if (outcome.Code is string code)
            {
                ended = $"{ended} {code}";
            }

            return CheckLine.Fail(script.Kind, script.Subject, outcome.ErrorText is string text ? $"{ended}: {text}" : ended);
        }

        try
        {
            (bool passed, string reason) = script.Read(result);

            return new CheckLine(script.Kind, script.Subject, passed, reason);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return CheckLine.Fail(script.Kind, script.Subject, "the script of the check gave a result of another form");
        }
    }

    // The lines Server and ClockSkew: one outcome of a made-up command, refused as not-addressed, signed with the agent's
    // key and sent to the server of agent.json through the proxy of the policy, as the agent sends its outcomes; never a
    // poll, which would replace the poll that the running service holds.
    private async Task CheckServerAsync(TimeSpan allowed, CancellationToken cancellationToken)
    {
        CheckLine? input = null;

        if (_gate.Policy is null)
        {
            input = _gate.PolicyLine;
        }
        else if (_gate.Pairing is null)
        {
            input = _gate.PairingLine;
        }
        else if (_gate.Secrets is null)
        {
            input = _gate.SecretsLine;
        }

        if (input is not null)
        {
            _lines.Add(CheckLine.NotChecked(CheckLineEnum.Server, "", input));
            _lines.Add(CheckLine.Fail(CheckLineEnum.ClockSkew, "", "not checked: no verified answer came"));

            return;
        }

        AgentPolicy policy = _gate.Policy!;
        AgentPairingSettings pairing = _gate.Pairing!;

        // secrets.dat was checked to hold an ECDSA key on P-256 in PKCS#8, and agent.json the server key in the form of
        // §2, as the agent's host reads them.
        using ECDsa agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(_gate.Secrets!.PrivateKey, out _);
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
            ? CheckLine.Pass(
                CheckLineEnum.Server,
                "",
                $"{pairing.Server} confirmed an outcome of a made-up command from {pairing.Agent}: it is reachable and accepts the agent's key")
            : CheckLine.Fail(
                CheckLineEnum.Server,
                "",
                ServerAnswerSentences.Of(OutpostRequestKindEnum.Outcome, answer.Cause ?? ServerAnswerCauseEnum.Answer, answer.Status, answer.Code)));

        _lines.Add(answer.Time is DateTimeOffset time
            ? SkewLine(time, sent, received, allowed)
            : CheckLine.Fail(CheckLineEnum.ClockSkew, "", "not checked: no verified answer came"));
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
            return CheckLine.Pass(CheckLineEnum.ClockSkew, "", $"{measured}, within {allowed:c}");
        }

        if (size - uncertainty > allowed)
        {
            return CheckLine.Fail(
                CheckLineEnum.ClockSkew,
                "",
                $"{measured}, beyond {allowed:c}; with the server ahead, the agent refuses every command with clock-skew, and with it behind, with expired once the gap also passes the take timeout: set the clocks");
        }

        return CheckLine.Fail(CheckLineEnum.ClockSkew, "", $"cannot be judged within {allowed:c}: the round trip took {WholeMilliseconds(roundTrip)} ms");
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
                new Text(line.Name),
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
