using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Polls;
using Moltaro.OnecOutpost.Agent.Records;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Start;

// What a start finds in the data folder, after a restart included.
internal static class AgentStart
{
    // Runs the steps of a start, and the first that fails decides: the audit log, whose IOException passes and records
    // nothing; a Fault of the log; the access lists of the data folder, its folders and its files; the access lists of
    // the program folder, programFolder, its full path, through which nobody else may change the code that the agent's
    // account runs; agent.json and secrets.dat; the outbox, the records of commands and the poll numbers; policy.json,
    // with a password in the secrets for every mode it serves and for the worker account of every base that names one;
    // and the logon of each such worker account, in the policy's order, before any worker starts. A failed step after
    // the first records started with the error and withoutSafeMode empty, records policy invalid when policy.json
    // failed, disposes the log and throws AgentStopException with the same reason. When every step passes, it records
    // started with the bases that write without safe mode and policy as valid, calls Recover and returns the run.
    // version is the agent's version as given.
    public static AgentRun Open(
        AgentFolders folders,
        IAccessLists access,
        IMachineProtection protection,
        IWorkerLauncher workers,
        string programFolder,
        TimeProvider clock,
        string version)
    {
        // A second copy of the agent, or a missing audit\, fails here and records nothing.
        AuditLog log = AuditLog.Open(folders.Audit, clock);

        try
        {
            AgentRun run;

            try
            {
                run = Check(folders, access, protection, workers, programFolder, clock, log);
            }
            catch (AgentStopException exception)
            {
                log.Started(version, [], exception.Message);
                throw;
            }
            catch (AgentPolicyException exception)
            {
                // An invalid file serves nothing: its outbox waits on the disk for a start with a valid policy.
                string reason = StartChecks.PolicyReason(exception);

                log.Started(version, [], reason);
                log.Policy(exception.Message);

                throw new AgentStopException(reason);
            }

            List<string> withoutSafeMode =
            [
                .. run.Policy.Bases
                    .Where(basePolicy => basePolicy.Write is { SafeMode: false })
                    .Select(basePolicy => basePolicy.Key),
            ];

            log.Started(version, withoutSafeMode, null);
            log.Policy(null);
            run.Records.Recover();

            return run;
        }
        catch
        {
            // The caller disposes the log of a run that it is given; that of a start that failed is disposed here.
            log.Dispose();
            throw;
        }
    }

    // Steps 2 to 7, which write nothing: each failure is an AgentStopException with its reason, and an invalid policy
    // an AgentPolicyException.
    private static AgentRun Check(
        AgentFolders folders,
        IAccessLists access,
        IMachineProtection protection,
        IWorkerLauncher workers,
        string programFolder,
        TimeProvider clock,
        AuditLog log)
    {
        if (log.Fault is string fault)
        {
            throw new AgentStopException(fault);
        }

        IReadOnlyList<string> rootNames = List(folders.Root, "the data folder");

        CheckAccess(folders, access, log, rootNames);
        CheckProgramFolder(access, programFolder);

        AgentPairingSettings pairing = StartChecks.ReadPairing(folders.Root, rootNames);
        AgentSecrets secrets = StartChecks.ReadSecrets(folders.Root, rootNames, protection);

        Outbox outbox = Outbox.Open(folders.Outcomes);
        CommandRecords records = CommandRecords.Open(log, outbox);
        PollNumbers numbers = new(log, pairing.Agent, clock);

        AgentPolicy policy = StartChecks.ReadPolicy(folders.Root, rootNames);

        CheckPasswords(policy, secrets);
        LogOn(policy, secrets, workers);

        return new AgentRun(policy, pairing, secrets, log, outbox, records, numbers);
    }

    // The data folder admits nobody else: the folder, its two folders and every file the agent reads, since a file
    // moved into the folder keeps its own list. The log is read and cut before this, so the started of a failed check
    // goes after a cut torn line (§13 of docs/protocol.md).
    private static void CheckAccess(AgentFolders folders, IAccessLists access, AuditLog log, IReadOnlyList<string> rootNames)
    {
        IReadOnlyList<string> outbox = List(folders.Outcomes, $@"{AgentFolders.OutcomesName}\");

        foreach (string path in StartChecks.DataPaths(rootNames, log.Files, outbox))
        {
            OtherAccounts others;

            try
            {
                others = access.Others(path);
            }
            catch (IOException)
            {
                throw new AgentStopException($"the access list of {StartChecks.EntryOf(path)} cannot be read");
            }

            if (StartChecks.AccessReason(path, others) is string reason)
            {
                throw new AgentStopException(reason);
            }
        }
    }

    // An account that could replace the code that the agent's account runs, the agent's own program, the worker or the
    // executor, would get the agent's secrets; the first entry through which one could stops the start.
    private static void CheckProgramFolder(IAccessLists access, string programFolder)
    {
        IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> writers;

        try
        {
            writers = access.Writers(programFolder);
        }
        catch (IOException)
        {
            throw new AgentStopException("the access lists of the program folder cannot be read");
        }

        if (writers is [(string path, IReadOnlyList<string> accounts), ..])
        {
            throw new AgentStopException(StartChecks.WritersReason(path, accounts));
        }
    }

    // A mode that the policy serves needs the password of its account: a start without one would only fail later, in
    // every command of that mode. The first one missing stops it.
    private static void CheckPasswords(AgentPolicy policy, AgentSecrets secrets)
    {
        if (StartChecks.MissingPasswords(policy, secrets) is [string first, ..])
        {
            throw new AgentStopException(first);
        }
    }

    // Every COM worker of a base with a worker account runs under it, Reads included, so its logon comes before any
    // worker starts. The launcher's AgentStopException passes, and no later base is logged on.
    private static void LogOn(AgentPolicy policy, AgentSecrets secrets, IWorkerLauncher workers)
    {
        foreach (AgentBasePolicy basePolicy in policy.Bases)
        {
            if (basePolicy.Write?.WorkerAccount is string account)
            {
                workers.LogOn(basePolicy.Key, account, secrets.WorkerPasswords[basePolicy.Key]);
            }
        }
    }

    // The names of the files of a folder; an IOException stops the start, naming the folder.
    private static IReadOnlyList<string> List(IAgentFolder folder, string entry)
    {
        try
        {
            return folder.Names();
        }
        catch (IOException)
        {
            throw new AgentStopException($"{entry} cannot be listed");
        }
    }
}
