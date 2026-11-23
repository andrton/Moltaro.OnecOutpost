using System.Security.Cryptography;
using System.Text;
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
    private const string PolicyName = "policy.json";

    private const string PairingName = "agent.json";

    private const string SecretsName = "secrets.dat";

    // policy.json is read as UTF-8; bytes that are not make the file invalid, never U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // The byte order mark that an editor may put first in policy.json: U+FEFF in UTF-8.
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    // Runs the steps of a start, and the first that fails decides: the audit log, whose IOException passes and records
    // nothing; a Fault of the log; the access lists of the data folder, its folders and its files; the access lists of
    // the program folder, programs, its full path, through which nobody else may change the code that the agent's
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
        string programs,
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
                run = Check(folders, access, protection, workers, programs, clock, log);
            }
            catch (AgentStopException exception)
            {
                log.Started(version, [], exception.Message);
                throw;
            }
            catch (AgentPolicyException exception)
            {
                // An invalid file serves nothing: its outbox waits on the disk for a start with a valid policy.
                string reason = $"{PolicyName} is not valid: {exception.Message}";

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
        string programs,
        TimeProvider clock,
        AuditLog log)
    {
        if (log.Fault is string fault)
        {
            throw new AgentStopException(fault);
        }

        IReadOnlyList<string> rootNames = List(folders.Root, "the data folder");

        CheckAccess(folders, access, log, rootNames);
        CheckPrograms(access, programs);

        AgentPairingSettings pairing = ReadPairing(folders.Root, rootNames);
        AgentSecrets secrets = ReadSecrets(folders.Root, rootNames, protection);

        Outbox outbox = Outbox.Open(folders.Outcomes);
        CommandRecords records = CommandRecords.Open(log, outbox);
        PollNumbers numbers = new(log, pairing.Agent, clock);

        AgentPolicy policy = ReadPolicy(folders.Root, rootNames);

        CheckPasswords(policy, secrets);
        LogOn(policy, secrets, workers);

        return new AgentRun(policy, pairing, secrets, log, outbox, records, numbers);
    }

    // The data folder admits nobody else: the folder, its two folders and every file the agent reads, since a file
    // moved into the folder keeps its own list. The log is read and cut before this, so the started of a failed check
    // goes after a cut torn line (§13 of docs/protocol.md).
    private static void CheckAccess(AgentFolders folders, IAccessLists access, AuditLog log, IReadOnlyList<string> rootNames)
    {
        List<string> paths = ["", "audit", "outcomes"];

        paths.AddRange(new[] { PolicyName, PairingName, SecretsName }.Where(rootNames.Contains));
        paths.AddRange(log.Files.Select(name => $@"audit\{name}"));
        paths.AddRange(List(folders.Outcomes, @"outcomes\").Select(name => $@"outcomes\{name}"));

        foreach (string path in paths)
        {
            string entry = path.Length == 0 ? "the data folder" : path;
            IReadOnlyList<string> others;

            try
            {
                others = access.Others(path);
            }
            catch (IOException)
            {
                throw new AgentStopException($"the access list of {entry} cannot be read");
            }

            if (others.Count > 0)
            {
                throw new AgentStopException(
                    $"the access list of {entry} admits {string.Join(", ", others)}; expected nobody but Administrators, SYSTEM and the agent's account, as the console's install sets it");
            }
        }
    }

    // An account that could replace the code that the agent's account runs, the agent's own program, the worker or the
    // executor, would get the agent's secrets; the first entry through which one could stops the start.
    private static void CheckPrograms(IAccessLists access, string programs)
    {
        IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> writers;

        try
        {
            writers = access.Writers(programs);
        }
        catch (IOException)
        {
            throw new AgentStopException("the access lists of the program folder cannot be read");
        }

        if (writers is [(string path, IReadOnlyList<string> accounts), ..])
        {
            throw new AgentStopException(
                $"the access list of {path} lets {string.Join(", ", accounts)} change it; expected nobody but Administrators, SYSTEM, TrustedInstaller and the agent's account, as the console's install sets it");
        }
    }

    private static AgentPairingSettings ReadPairing(IAgentFolder root, IReadOnlyList<string> rootNames)
    {
        if (!rootNames.Contains(PairingName))
        {
            throw new AgentStopException($"{PairingName} is absent: the agent is not paired; pair it with the console");
        }

        try
        {
            return AgentPairingFile.Read(ReadFile(root, PairingName));
        }
        catch (FormatException exception)
        {
            throw new AgentStopException($"{PairingName} cannot be read: {exception.Message}");
        }
    }

    private static AgentSecrets ReadSecrets(IAgentFolder root, IReadOnlyList<string> rootNames, IMachineProtection protection)
    {
        if (!rootNames.Contains(SecretsName))
        {
            throw new AgentStopException($"{SecretsName} is absent; write the secrets with the console");
        }

        try
        {
            return SecretsFile.Read(ReadFile(root, SecretsName), protection);
        }
        catch (FormatException exception)
        {
            throw new AgentStopException($"{SecretsName} cannot be read: {exception.Message}");
        }
        catch (CryptographicException)
        {
            // Such as a file copied from another machine.
            throw new AgentStopException($"{SecretsName} cannot be decrypted on this machine; write the secrets with the console");
        }
    }

    // The policy as AgentPolicyReader reads it from the text of the file in UTF-8, a byte order mark skipped and not
    // counted in the columns of an error. A file that cannot be read is as invalid as one that breaks its rules.
    private static AgentPolicy ReadPolicy(IAgentFolder root, IReadOnlyList<string> rootNames)
    {
        if (!rootNames.Contains(PolicyName))
        {
            throw new AgentPolicyException("", "the file is absent; expected the local policy of this machine");
        }

        byte[] bytes;

        try
        {
            bytes = root.ReadAll(PolicyName);
        }
        catch (IOException)
        {
            throw new AgentPolicyException("", "the file cannot be read; expected a file that the agent's account may read");
        }

        ReadOnlySpan<byte> text = bytes;

        if (text.StartsWith(ByteOrderMark))
        {
            text = text[ByteOrderMark.Length..];
        }

        string json;

        try
        {
            json = StrictUtf8.GetString(text);
        }
        catch (DecoderFallbackException)
        {
            // The fault is the file as a whole, so the path is empty, and the bytes are not repeated.
            throw new AgentPolicyException("", "the file holds bytes that are not UTF-8; expected JSON text in UTF-8");
        }

        return AgentPolicyReader.Read(json);
    }

    // A mode that the policy serves needs the password of its account: a start without one would only fail later, in
    // every command of that mode.
    private static void CheckPasswords(AgentPolicy policy, AgentSecrets secrets)
    {
        foreach (AgentBasePolicy basePolicy in policy.Bases)
        {
            if (!secrets.Passwords.ContainsKey((basePolicy.Key, OutpostModeEnum.Read)))
            {
                throw new AgentStopException(
                    $"{SecretsName} holds no password for the read mode of the base {basePolicy.Key}; write it with the console");
            }

            if (basePolicy.Write is not null && !secrets.Passwords.ContainsKey((basePolicy.Key, OutpostModeEnum.Write)))
            {
                throw new AgentStopException(
                    $"{SecretsName} holds no password for the write mode of the base {basePolicy.Key}; write it with the console");
            }

            if (basePolicy.Write?.WorkerAccount is not null && !secrets.WorkerPasswords.ContainsKey(basePolicy.Key))
            {
                throw new AgentStopException(
                    $"{SecretsName} holds no password for the worker account of the base {basePolicy.Key}; write it with the console");
            }
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

    // A file of the data folder itself, read whole; an IOException stops the start, naming the file.
    private static byte[] ReadFile(IAgentFolder root, string name)
    {
        try
        {
            return root.ReadAll(name);
        }
        catch (IOException)
        {
            throw new AgentStopException($"{name} cannot be read");
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
