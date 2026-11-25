using System.Runtime.Versioning;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Tests.FakeWindows;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// The acceptance of S6, with Requires=Windows: a data folder, or a file in it, whose access list admits anybody else
// makes the agent stop, recorded. The data folder is on the disk, in the test's temporary folder, with the access list
// that the console's install gives it and the account that runs the tests in the place of the agent's; its secrets are
// encrypted with DPAPI for the machine. The data folder's lists are those of Windows; the program folder lets nobody
// else change it, as the fake answers, so that the start reaches the data folder's files.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class DiskStartTests : IDisposable
{
    private readonly DiskDataFolder _data = new();

    private readonly FakeClock _clock = new(TestTimes.Start);

    public void Dispose()
    {
        _data.Dispose();
    }

    [Fact]
    public void A_file_on_the_disk_whose_access_list_admits_anybody_else_stops_the_start_recorded()
    {
        _data.WriteFiles();
        Start().Log.Dispose();

        AccessListEdits.Allow(_data.PathOf("policy.json"), AccessListEdits.Everyone);

        AgentStopException exception = Assert.Throws<AgentStopException>(() => Start());

        JsonElement started = _data.LastEntry(TestTimes.Start);
        Assert.Equal("started", started.GetProperty("entry").GetString());
        Assert.Equal(exception.Message, started.GetProperty("error").GetString());
        Assert.Contains("policy.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains(AccessListEdits.NameOf(AccessListEdits.Everyone), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_data_folder_on_the_disk_whose_access_list_admits_anybody_else_stops_the_start_recorded()
    {
        _data.WriteFiles();
        AccessListEdits.Allow(_data.Path, AccessListEdits.Everyone);

        AgentStopException exception = Assert.Throws<AgentStopException>(() => Start());

        JsonElement started = _data.LastEntry(TestTimes.Start);
        Assert.Equal("started", started.GetProperty("entry").GetString());
        Assert.Equal(exception.Message, started.GetProperty("error").GetString());
        Assert.Contains(AccessListEdits.NameOf(AccessListEdits.Everyone), exception.Message, StringComparison.Ordinal);
    }

    private AgentRun Start()
    {
        return AgentStart.Open(
            _data.Folders,
            new DataFolderAccessLists(_data.Path),
            new MachineDpapi(),
            new FakeWorkerLauncher(new MemoryDataFolder()),
            PairedFolder.ProgramFolder,
            _clock,
            PairedFolder.Version);
    }
}
