namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of an `outcome` request (§6.3 of docs/protocol.md): the identity of the command, and the exact bytes of
// its outcome object (§8), as the agent forwards them. The size of the outcome is the length of those bytes.
internal sealed record OutpostOutcomeRequest(Guid Command, ReadOnlyMemory<byte> Outcome);
