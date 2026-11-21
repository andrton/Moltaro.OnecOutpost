namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of a `poll` request (§6.2 of docs/protocol.md): the poll number, the agent's time and version, the poll
// timeout that the agent last learnt, the bases that its local policy serves, and every command it received whose
// outcome the server has not confirmed.
internal sealed record OutpostPollRequest(
    long Number,
    DateTimeOffset Time,
    string AgentVersion,
    TimeSpan PollTimeout,
    IReadOnlyList<OutpostPollBase> Bases,
    IReadOnlyList<Guid> Received);
