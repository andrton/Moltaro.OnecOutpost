namespace Moltaro.OnecOutpost.Agent.Connector;

// A comcntr.dll of the platform: its full path, and its FileVersion, such as 8.3.17.1851, empty when it has none.
internal sealed record ConnectorFile(string Path, string Version);
