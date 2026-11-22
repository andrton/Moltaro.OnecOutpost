namespace Moltaro.OnecOutpost.Agent.Pairing;

// What agent.json holds after pairing: the server's address, the agent's identity, the server key the agent pinned, in
// its form of 91 bytes, and the poll timeout and the lease grace of the server.
internal sealed record AgentPairingSettings(
    Uri Server,
    Guid Agent,
    byte[] ServerKey,
    TimeSpan PollTimeout,
    TimeSpan LeaseGrace);
