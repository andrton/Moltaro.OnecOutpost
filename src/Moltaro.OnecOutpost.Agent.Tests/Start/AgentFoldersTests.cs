using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// The layout of the data folder, named once, with Requires=Windows:
// AgentFolders.OnDisk gives the folders on the disk of a data folder at its full path, the root, which holds
// policy.json, agent.json and secrets.dat, and audit\ and outcomes\ inside it.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class AgentFoldersTests : IDisposable
{
    private readonly TemporaryFolder _temporary = new();

    public void Dispose()
    {
        _temporary.Dispose();
    }

    [Fact]
    public void The_folders_of_a_data_folder_on_the_disk_are_the_folder_itself_its_audit_and_its_outcomes()
    {
        string data = _temporary.PathOf("data");
        Directory.CreateDirectory(Path.Combine(data, "audit"));
        Directory.CreateDirectory(Path.Combine(data, "outcomes"));

        AgentFolders folders = AgentFolders.OnDisk(data);
        folders.Root.Replace(AgentFolders.PolicyName, "{}"u8);
        folders.Audit.Append("2026-11-22.jsonl", "{}\n"u8);
        folders.Outcomes.Replace("9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6.json", "{}"u8);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(data, "policy.json")));
        Assert.Equal("{}\n", File.ReadAllText(Path.Combine(data, "audit", "2026-11-22.jsonl")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(data, "outcomes", "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6.json")));
        Assert.Equal([AgentFolders.PolicyName], folders.Root.Names());
    }
}
