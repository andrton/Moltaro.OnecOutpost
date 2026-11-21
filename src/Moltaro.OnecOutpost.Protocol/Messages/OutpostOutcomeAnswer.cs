namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of the answer `200` to `outcome` (§6.3 of docs/protocol.md): the server's time, and the identity of the
// command whose outcome it confirms, which is the request's `command`.
internal sealed record OutpostOutcomeAnswer(DateTimeOffset Time, Guid Confirmed);
