namespace Moltaro.OnecOutpost.Protocol;

// What the codec and the signing types throw for bytes that they refuse: a code of OutpostProtocolCodes, or
// `malformed` of the outcome codes for a command (§7 of docs/protocol.md), with a reason as the message.
internal sealed class OutpostProtocolException : Exception
{
    public OutpostProtocolException(string code, string reason, Guid? commandId = null)
        : base(reason)
    {
        Code = code;
        CommandId = commandId;
    }

    public string Code { get; }

    // Set for the two refusals of a verified command that name it, `protocol-version` and `malformed` (§7): the agent
    // refuses the command under this identity. Null for every other refusal.
    public Guid? CommandId { get; }
}
