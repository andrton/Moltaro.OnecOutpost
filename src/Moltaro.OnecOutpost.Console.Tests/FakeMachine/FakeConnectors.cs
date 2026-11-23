using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The COM connector of a test's machine, a 64-bit Windows, as the finder reads HKLM and the folders of the platform:
// for each bitness the copy registered, if any, and the copies installed and not registered, in the order the test
// installs them. Each Find is first the line "find" of the operation list of the machine's data folder, and gives a
// finding for 64 bits, then one for 32 bits, as they are at that moment. Not thread-safe.
internal sealed class FakeConnectors : IConnectorFinder
{
    private readonly MemoryDataFolder _notes;

    private readonly Dictionary<ConnectorBitnessEnum, ConnectorFile?> _registered = new()
    {
        [ConnectorBitnessEnum.X64] = null,
        [ConnectorBitnessEnum.X86] = null,
    };

    private readonly Dictionary<ConnectorBitnessEnum, List<ConnectorFile>> _unregistered = new()
    {
        [ConnectorBitnessEnum.X64] = [],
        [ConnectorBitnessEnum.X86] = [],
    };

    public FakeConnectors(MemoryDataFolder notes)
    {
        _notes = notes;
    }

    // The platform's comcntr.dll of the version in its folder under Program Files, or Program Files (x86) for 32 bits,
    // such as C:\Program Files\1cv8\8.3.27.1859\bin\comcntr.dll.
    public static string PathOf(ConnectorBitnessEnum bitness, string version)
    {
        string programFiles = bitness == ConnectorBitnessEnum.X64 ? "Program Files" : "Program Files (x86)";

        return Path.Combine(TestMachine.Root, programFiles, "1cv8", version, "bin", "comcntr.dll");
    }

    // A copy of the connector of the version, installed and registered for its bitness.
    public ConnectorFile Registered(ConnectorBitnessEnum bitness, string version)
    {
        ConnectorFile file = new(PathOf(bitness, version), version);
        _registered[bitness] = file;

        return file;
    }

    // A copy of the connector of the version, installed and not registered.
    public ConnectorFile Installed(ConnectorBitnessEnum bitness, string version)
    {
        ConnectorFile file = new(PathOf(bitness, version), version);
        _unregistered[bitness].Add(file);

        return file;
    }

    public IReadOnlyList<ConnectorFinding> Find()
    {
        _notes.Note("find");

        return
        [
            new ConnectorFinding(ConnectorBitnessEnum.X64, _registered[ConnectorBitnessEnum.X64], [.. _unregistered[ConnectorBitnessEnum.X64]]),
            new ConnectorFinding(ConnectorBitnessEnum.X86, _registered[ConnectorBitnessEnum.X86], [.. _unregistered[ConnectorBitnessEnum.X86]]),
        ];
    }

    // The installed copy at the path becomes the registered one of its bitness, as a run of regsvr32 that HKLM shows.
    public void Register(ConnectorBitnessEnum bitness, string path)
    {
        ConnectorFile file = _unregistered[bitness].Single(copy => copy.Path == path);

        _unregistered[bitness].Remove(file);
        _registered[bitness] = file;
    }
}
