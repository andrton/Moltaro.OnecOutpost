namespace Moltaro.OnecOutpost.Agent.Pairing;

// The form of agent.json: {"server":…,"agent":…,"serverKey":…,"pollTimeoutMs":…,"leaseGraceMs":…} in the forms of §2
// of docs/protocol.md: an absolute http or https URL, an identity, the 91-byte key in base64url, and the two durations
// in the ranges of PollTimeout and LeaseGrace. The rule of §11 for the address is the transport's.
internal static class AgentPairingFile
{
    // Reads exactly these members, and throws FormatException, quoting nothing, for anything else.
    public static AgentPairingSettings Read(byte[] file)
    {
        throw new NotSupportedException();
    }

    // Writes the settings with the writer of §2.
    public static byte[] Write(AgentPairingSettings settings)
    {
        throw new NotSupportedException();
    }
}
