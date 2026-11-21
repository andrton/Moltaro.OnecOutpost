namespace Moltaro.OnecOutpost.Protocol.Messages;

// One entry of `commands` in a poll answer (§6.2 of docs/protocol.md): the exact bytes of a command (§7) and the
// server's signature over them (§3). Nothing in the bytes is trusted before the signature is verified.
internal sealed record OutpostSignedCommand(byte[] Bytes, byte[] Signature);
