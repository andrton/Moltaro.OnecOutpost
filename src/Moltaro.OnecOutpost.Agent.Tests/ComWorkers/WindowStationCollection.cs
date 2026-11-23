namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The test classes that read the window station of the process of the tests, or swap it as WorkerDesktop.Make and
// TestDesktop.DesktopDacl do for a moment, run one at a time, so that none reads the station another swapped in.
[CollectionDefinition(Name)]
public sealed class WindowStationCollection
{
    public const string Name = "window station";
}
