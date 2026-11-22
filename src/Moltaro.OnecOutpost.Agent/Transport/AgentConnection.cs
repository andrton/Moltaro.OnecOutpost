using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Messages;

namespace Moltaro.OnecOutpost.Agent.Transport;

// The agent's requests to its server, poll and outcome (§6.2, §6.3 of docs/protocol.md), signed with the agent's key
// and verified with the server key it pinned. It owns its handler through HttpMessageInvoker and sends HTTP/1.1 only,
// the headers of §4, Accept-Encoding: identity, and on outcome Expect: 100-continue. Each wait is a
// CancellationTokenSource on the clock, and a body beyond its read limit (§10) is lost unread.
internal sealed class AgentConnection : IDisposable
{
    public AgentConnection(
        Uri server,
        Guid agent,
        ECDsa agentKey,
        ECDsa serverKey,
        HttpMessageHandler handler,
        TimeProvider clock)
    {
        throw new NotSupportedException();
    }

    // Sends the poll and waits for its answer to begin for its pollTimeout plus leaseGrace, then reads the body within
    // requestTimeout and 64 MiB. An answer verified by §9 and ReadPollAnswer, with the sum of the poll's free, is
    // Answer; any other gives the reason of §9. A tunnel the proxy refuses, which the handler throws as an
    // HttpRequestException with its status, is a status that came: status-407.
    public Task<PollResult> PollAsync(
        OutpostPollRequest poll,
        TimeSpan leaseGrace,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // Sends the outcome bytes of the command and waits requestTimeout over the answer and its body, read within 64 KiB.
    // Confirmed for a verified 200; TooLarge for a verified result-too-large and for a 413 without an Outpost-Signature
    // that verifies, whatever its body; Lost otherwise.
    public Task<OutcomeResultEnum> SendOutcomeAsync(
        Guid command,
        ReadOnlyMemory<byte> outcome,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // Disposes the invoker, and with it the handler.
    public void Dispose()
    {
        throw new NotSupportedException();
    }
}
