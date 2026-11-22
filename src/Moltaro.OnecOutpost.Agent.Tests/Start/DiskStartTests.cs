using System.Buffers.Text;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// The acceptance of S6, with Requires=Windows: a data folder, or a file in it, whose access list admits anybody else
// makes the agent stop, recorded. The data folder is on the disk, in the test's temporary folder, with the access list
// that the console's install gives it and the account that runs the tests in the place of the agent's; its secrets are
// encrypted with DPAPI for the machine.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class DiskStartTests : IDisposable
{
    private readonly TemporaryFolder _temporary = new();

    private readonly string _data;

    private readonly FakeClock _clock = new(TestTimes.Start);

    public DiskStartTests()
    {
        _data = _temporary.PathOf("data");
        Directory.CreateDirectory(_data);
        AccessListEdits.AdmitNobodyElse(_data);
        Directory.CreateDirectory(Path.Combine(_data, "audit"));
        Directory.CreateDirectory(Path.Combine(_data, "outcomes"));
    }

    public void Dispose()
    {
        _temporary.Dispose();
    }

    [Fact]
    public void A_file_on_the_disk_whose_access_list_admits_anybody_else_stops_the_start_recorded()
    {
        WriteDataFolder();
        Start().Log.Dispose();

        AccessListEdits.Allow(Path.Combine(_data, "policy.json"), AccessListEdits.Everyone);

        AgentStopException exception = Assert.Throws<AgentStopException>(() => Start());

        JsonElement started = LastEntry();
        Assert.Equal("started", started.GetProperty("entry").GetString());
        Assert.Equal(exception.Message, started.GetProperty("error").GetString());
        Assert.Contains("policy.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains(AccessListEdits.NameOf(AccessListEdits.Everyone), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_data_folder_on_the_disk_whose_access_list_admits_anybody_else_stops_the_start_recorded()
    {
        WriteDataFolder();
        AccessListEdits.Allow(_data, AccessListEdits.Everyone);

        AgentStopException exception = Assert.Throws<AgentStopException>(() => Start());

        JsonElement started = LastEntry();
        Assert.Equal("started", started.GetProperty("entry").GetString());
        Assert.Equal(exception.Message, started.GetProperty("error").GetString());
        Assert.Contains(AccessListEdits.NameOf(AccessListEdits.Everyone), exception.Message, StringComparison.Ordinal);
    }

    private AgentRun Start()
    {
        AgentFolders folders = new(
            new DiskFolder(_data),
            new DiskFolder(Path.Combine(_data, "audit")),
            new DiskFolder(Path.Combine(_data, "outcomes")));

        return AgentStart.Open(folders, new WindowsAccessLists(_data), new MachineDpapi(), _clock, PairedFolder.Version);
    }

    // policy.json, agent.json and secrets.dat as an administrator and the console write them.
    private void WriteDataFolder()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa agent = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        File.WriteAllText(Path.Combine(_data, "policy.json"), PairedFolder.Policy);
        File.WriteAllText(
            Path.Combine(_data, "agent.json"),
            $$"""{"server":"https://outpost.example.com/agents","agent":"{{PairedFolder.Agent}}","serverKey":"{{Base64Url.EncodeToString(server.ExportSubjectPublicKeyInfo())}}","pollTimeoutMs":25000,"leaseGraceMs":10000}""");

        string secrets =
            $$"""{"key":"{{Base64Url.EncodeToString(agent.ExportPkcs8PrivateKey())}}","passwords":[{"base":"accounting","mode":"read","password":"read-pw"},{"base":"accounting","mode":"write","password":"write-pw"},{"base":"warehouse","mode":"read","password":""}]}""";

        File.WriteAllBytes(Path.Combine(_data, "secrets.dat"), new MachineDpapi().Protect(Encoding.UTF8.GetBytes(secrets)));
    }

    private JsonElement LastEntry()
    {
        string line = File.ReadAllLines(Path.Combine(_data, "audit", AuditLines.FileOf(TestTimes.Start)))[^1];

        using JsonDocument document = JsonDocument.Parse(line);

        return document.RootElement.Clone();
    }
}
