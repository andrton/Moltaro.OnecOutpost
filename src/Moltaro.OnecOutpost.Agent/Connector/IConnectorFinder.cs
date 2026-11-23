namespace Moltaro.OnecOutpost.Agent.Connector;

// The finder of the COM connector: the boundary that the registry and the platform's folders implement and tests
// replace. The launcher of the COM workers and the console use it.
internal interface IConnectorFinder
{
    // A finding for 64 bits on a 64-bit Windows, then one for 32 bits. Throws nothing, and may run on several slots at
    // once: a key, folder or file that cannot be read is not Registered, or is left out of Unregistered.
    IReadOnlyList<ConnectorFinding> Find();
}
