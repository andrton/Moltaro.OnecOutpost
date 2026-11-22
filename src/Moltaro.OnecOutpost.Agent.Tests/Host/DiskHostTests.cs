using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Tests.Start;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// The acceptance of S6, with Requires=Windows: a data folder, or a file in it, whose access list admits anybody else
// makes the agent stop, recorded. The host runs on the disk with the machine's access lists and DPAPI, as the service
// does; it gives Error, records started with the error, and polls nothing.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class DiskHostTests : IDisposable
{
    private readonly DiskDataFolder _data = new();

    public void Dispose()
    {
        _data.Dispose();
    }

    // "" is the data folder itself.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("")]
    [InlineData("policy.json")]
    [InlineData("secrets.dat")]
    public async Task A_data_folder_or_a_file_in_it_whose_access_list_admits_anybody_else_makes_the_agent_stop_recorded(string entry)
    {
        _data.WriteFiles();
        AccessListEdits.Allow(entry.Length == 0 ? _data.Path : _data.PathOf(entry), AccessListEdits.Everyone);
        FakeClock clock = new(TestTimes.Start);
        using ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        FakeServer server = new(serverKey, clock);
        AgentParts parts = new(
            _data.Folders,
            new WindowsAccessLists(_data.Path),
            new MachineDpapi(),
            new FakeWorkerLauncher(new MemoryDataFolder()),
            TestHost.Executor,
            (_, _) => server,
            clock,
            PairedFolder.Version);

        await using HostRun run = HostRun.Start(parts);

        Assert.Equal(AgentExitCodes.Error, await run.ExitAsync());
        JsonElement started = _data.LastEntry(TestTimes.Start);
        Assert.Equal("started", started.GetProperty("entry").GetString());
        Assert.Contains(AccessListEdits.NameOf(AccessListEdits.Everyone), started.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, server.Count(OutpostRequestKindEnum.Poll));
    }
}
