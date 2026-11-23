using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Console.Connector;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The regsvr32 of a test's machine. Each run is first the line "regsvr32 {bitness} {path}" of the operation list of the
// machine's data folder, and ends with the exit code the test gives, 0 until it says otherwise. A run that ends with 0
// registers the copy in the machine's connector, as HKLM then shows it, unless the test says that HKLM shows nothing
// after it. The command an administrator would type is "regsvr32 {bitness} "{path}"", which changes nothing.
internal sealed class FakeRegistration : IConnectorRegistration
{
    private readonly MemoryDataFolder _notes;

    private readonly FakeConnectors _connectors;

    public FakeRegistration(MemoryDataFolder notes, FakeConnectors connectors)
    {
        _notes = notes;
        _connectors = connectors;
    }

    public int ExitCode { get; set; }

    // Whether a run that ends with 0 shows in HKLM.
    public bool Shows { get; set; } = true;

    public string CommandOf(ConnectorBitnessEnum bitness, string path)
    {
        return $"regsvr32 {bitness} \"{path}\"";
    }

    public Task<int> RegisterAsync(ConnectorBitnessEnum bitness, string path, CancellationToken cancellationToken)
    {
        _notes.Note($"regsvr32 {bitness} {path}");

        if (ExitCode == 0 && Shows)
        {
            _connectors.Register(bitness, path);
        }

        return Task.FromResult(ExitCode);
    }
}
