namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of a signed refusal (§9 of docs/protocol.md): the server's time and the code, `form`, `signature` or
// `result-too-large`, each under the status that the table of §9 gives it.
internal sealed record OutpostRefusal(DateTimeOffset Time, string Code);
