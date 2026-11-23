namespace Moltaro.OnecOutpost.Agent.Connector;

// What the finder found for one bitness: the connector registered for it, or null, and the copies of that bitness in
// the platform's folders that are not registered, by ordinal path.
internal sealed record ConnectorFinding(
    ConnectorBitnessEnum Bitness,
    ConnectorFile? Registered,
    IReadOnlyList<ConnectorFile> Unregistered);
