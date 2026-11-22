using Moltaro.OnecOutpost.Protocol.Messages;

namespace Moltaro.OnecOutpost.Agent.Transport;

// What a poll gives the loop: the answer verified by §9 of docs/protocol.md and ReadPollAnswer, or, for a lost answer,
// null and the reason that connection lost records (§9).
internal sealed record PollResult(OutpostPollAnswer? Answer, string? Reason);
