using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Start;

// The steps of a start that judge the data folder and the program folder and read the files of the data folder, free of
// the audit log, so that AgentStart.Open and the console's check run the same steps with the same reasons. A step that
// judges many entries gives the reason of each; one that reads a file throws its reason.
internal static class StartChecks
{
    // The remedy of a secrets.dat that cannot be used.
    private const string PairAnew = $"to pair the agent anew, remove {AgentFolders.PairingName}, if any, and run the console's pair";

    // The paths, relative to the data folder, whose access lists a start judges: the folder itself, audit and outcomes,
    // those of the three files that rootNames holds, each audit file and each file of the outbox.
    public static IReadOnlyList<string> DataPaths(
        IReadOnlyList<string> rootNames,
        IEnumerable<string> auditFiles,
        IReadOnlyList<string> outboxFiles)
    {
        List<string> paths = ["", AgentFolders.AuditName, AgentFolders.OutcomesName];

        paths.AddRange(new[] { AgentFolders.PolicyName, AgentFolders.PairingName, AgentFolders.SecretsName }.Where(rootNames.Contains));
        paths.AddRange(auditFiles.Select(name => $@"{AgentFolders.AuditName}\{name}"));
        paths.AddRange(outboxFiles.Select(name => $@"{AgentFolders.OutcomesName}\{name}"));

        return paths;
    }

    // The reason why the entry at the path admits somebody else, or null when others IsEmpty, each with a remedy that
    // exists: an owner who is somebody else first, who can always change the access list (Q14), with the icacls command
    // that makes Administrators the owner, then the accounts of the entries, with what to do for the entry's kind.
    public static string? AccessReason(string path, OtherAccounts others)
    {
        if (others.IsEmpty)
        {
            return null;
        }

        string entry = EntryOf(path);
        List<string> reasons = [];

        if (others.Owner is string owner)
        {
            reasons.Add(
                $"{entry} is owned by {owner}, who can change its access list; make Administrators its owner with icacls \"{others.FullPath}\" /setowner *S-1-5-32-544, run as administrator");
        }

        if (others.Entries.Count > 0)
        {
            reasons.Add(
                $"the access list of {entry} admits {string.Join(", ", others.Entries)}; expected nobody but Administrators, SYSTEM and the agent's account: {RemedyOf(path, others.FullPath)}");
        }

        return string.Join("; ", reasons);
    }

    // The reason why the entry at the full path lets the accounts change the code that the agent's account runs.
    public static string WritersReason(string path, IReadOnlyList<string> accounts)
    {
        return $"the access list of {path} lets {string.Join(", ", accounts)} change it; expected nobody but Administrators, SYSTEM, TrustedInstaller and the agent's account: remove their entries, and make Administrators the owner of what they own";
    }

    // agent.json, absent or unreadable an AgentStopException.
    public static AgentPairingSettings ReadPairing(IAgentFolder root, IReadOnlyList<string> rootNames)
    {
        if (!rootNames.Contains(AgentFolders.PairingName))
        {
            throw new AgentStopException($"{AgentFolders.PairingName} is absent: the agent is not paired; pair it with the console's pair");
        }

        try
        {
            return AgentPairingFile.Read(ReadFile(root, AgentFolders.PairingName));
        }
        catch (FormatException exception)
        {
            throw new AgentStopException($"{AgentFolders.PairingName} cannot be read: {exception.Message}");
        }
    }

    // secrets.dat, absent, unreadable or not decrypted an AgentStopException. The console's pair writes it, and refuses
    // while agent.json exists, so the remedy of an absent file and of one that does not decrypt removes agent.json first.
    public static AgentSecrets ReadSecrets(IAgentFolder root, IReadOnlyList<string> rootNames, IMachineProtection protection)
    {
        if (!rootNames.Contains(AgentFolders.SecretsName))
        {
            throw new AgentStopException($"{AgentFolders.SecretsName} is absent; {PairAnew}");
        }

        try
        {
            return SecretsFile.Read(ReadFile(root, AgentFolders.SecretsName), protection);
        }
        catch (FormatException exception)
        {
            throw new AgentStopException($"{AgentFolders.SecretsName} cannot be read: {exception.Message}");
        }
        catch (CryptographicException)
        {
            // Such as a file copied from another machine.
            throw new AgentStopException($"{AgentFolders.SecretsName} cannot be decrypted on this machine; {PairAnew}");
        }
    }

    // The policy as AgentPolicyReader reads it from the file. A file that cannot be read is as invalid as one that breaks
    // its rules: an AgentPolicyException.
    public static AgentPolicy ReadPolicy(IAgentFolder root, IReadOnlyList<string> rootNames)
    {
        if (!rootNames.Contains(AgentFolders.PolicyName))
        {
            throw new AgentPolicyException("", "the file is absent; expected the local policy of this machine");
        }

        byte[] bytes;

        try
        {
            bytes = root.ReadAll(AgentFolders.PolicyName);
        }
        catch (IOException)
        {
            throw new AgentPolicyException("", "the file cannot be read; expected a file that the agent's account may read");
        }

        return AgentPolicyReader.ReadFile(bytes);
    }

    // The reason of a policy.json that is not valid, as the start, pair, secret, check and metadata give it.
    public static string PolicyReason(AgentPolicyException exception)
    {
        return $"{AgentFolders.PolicyName} is not valid: {exception.Message}";
    }

    // A mode that the policy serves needs the password of its account, and a base with a worker account the password of
    // that account: every one missing, in the policy's order, each with the console's secret that writes it.
    public static IReadOnlyList<string> MissingPasswords(AgentPolicy policy, AgentSecrets secrets)
    {
        List<string> missing = [];

        foreach (AgentBasePolicy basePolicy in policy.Bases)
        {
            if (!secrets.Passwords.ContainsKey((basePolicy.Key, OutpostModeEnum.Read)))
            {
                missing.Add($"{AgentFolders.SecretsName} holds no password for the read mode of the base {basePolicy.Key}; write it with secret {basePolicy.Key} read");
            }

            if (basePolicy.Write is not null && !secrets.Passwords.ContainsKey((basePolicy.Key, OutpostModeEnum.Write)))
            {
                missing.Add($"{AgentFolders.SecretsName} holds no password for the write mode of the base {basePolicy.Key}; write it with secret {basePolicy.Key} write");
            }

            if (basePolicy.Write?.WorkerAccount is not null && !secrets.WorkerPasswords.ContainsKey(basePolicy.Key))
            {
                missing.Add($"{AgentFolders.SecretsName} holds no password for the worker account of the base {basePolicy.Key}; write it with secret {basePolicy.Key} worker");
            }
        }

        return missing;
    }

    // The entry at the path, relative to the data folder, as the reasons name it: "" is the data folder.
    public static string EntryOf(string path)
    {
        return path.Length == 0 ? "the data folder" : path;
    }

    // What to do about an entry that admits somebody else: the data folder and its two folders hold what may not be lost,
    // so their lists are corrected; a file gets its folder's list again, and whoever secrets.dat admitted could have
    // decrypted it, since DPAPI for the machine lets every local process decrypt.
    private static string RemedyOf(string path, string fullPath)
    {
        const string Correct = "inspect and correct its access list as install sets it; never remove it, which holds";

        return path switch
        {
            "" => $"{Correct} the audit log and the outcomes not yet sent",
            AgentFolders.AuditName => $"{Correct} the audit log",
            AgentFolders.OutcomesName => $"{Correct} the outcomes not yet sent",
            AgentFolders.SecretsName => $"{ResetFile(fullPath)}; whoever it admitted could decrypt it: pair the agent anew and change the passwords it held",
            _ => ResetFile(fullPath),
        };
    }

    private static string ResetFile(string fullPath)
    {
        return $"inspect it, then reset it to its folder's with icacls \"{fullPath}\" /reset, run as administrator";
    }

    // A file of the data folder itself, read whole; an IOException is an AgentStopException naming the file.
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
}
