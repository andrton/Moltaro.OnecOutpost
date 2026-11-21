namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of the answer `200` to `poll` (§6.2 of docs/protocol.md): the server's time; `newest`, null in the
// ordinary answer and the newest poll number recorded for the agent in the answer to an older poll; the server's
// options PollTimeout and LeaseGrace; the keys of `bases.held`, which the agent holds, and of `bases.claimed`, which
// it declared and another agent holds; and the commands.
internal sealed record OutpostPollAnswer(
    DateTimeOffset Time,
    long? Newest,
    TimeSpan PollTimeout,
    TimeSpan LeaseGrace,
    IReadOnlyList<string> Held,
    IReadOnlyList<string> Claimed,
    IReadOnlyList<OutpostSignedCommand> Commands);
