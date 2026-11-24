namespace Moltaro.OnecOutpost.Agent.Connector;

// What the finder found for one bitness: the connector registered for it, or null, and the copies of that bitness in
// the platform's folders that are not registered, by ordinal path.
internal sealed record ConnectorFinding(
    ConnectorBitnessEnum Bitness,
    ConnectorFile? Registered,
    IReadOnlyList<ConnectorFile> Unregistered)
{
    // The first finding with a registered connector, in the finder's order, 64 bits first, or null for none: the one
    // whose bitness the workers start with, for the agent's launcher and the console alike.
    public static ConnectorFinding? FirstRegistered(IReadOnlyList<ConnectorFinding> findings)
    {
        return findings.FirstOrDefault(finding => finding.Registered is not null);
    }
}
