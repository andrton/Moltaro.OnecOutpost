using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Files;

namespace Moltaro.OnecOutpost.Agent.Start;

// The folders of one data folder: the root, which holds policy.json, agent.json and secrets.dat, audit\ and outcomes\.
// The layout of the data folder is named here once, for the agent, the console and the tests.
internal sealed record AgentFolders(IAgentFolder Root, IAgentFolder Audit, IAgentFolder Outcomes)
{
    // The folder of the agent under %ProgramData%, and of its program under %ProgramFiles%.
    public const string Name = "Moltaro.OnecOutpost";

    public const string AuditName = "audit";

    public const string OutcomesName = "outcomes";

    // The files of the data folder that the agent reads, which the console writes.
    public const string PolicyName = "policy.json";

    public const string PairingName = "agent.json";

    public const string SecretsName = "secrets.dat";

    // The data folder of this machine: %ProgramData%\Moltaro.OnecOutpost.
    public static string OnMachine()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
    }

    // The folders on the disk of the data folder at its full path.
    [SupportedOSPlatform("windows")]
    public static AgentFolders OnDisk(string dataFolder)
    {
        return new AgentFolders(
            new DiskFolder(dataFolder),
            new DiskFolder(Path.Combine(dataFolder, AuditName)),
            new DiskFolder(Path.Combine(dataFolder, OutcomesName)));
    }
}
