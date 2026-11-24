using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Agent.Transport;

// The agent's requests to its server, poll and outcome (§6.2, §6.3 of docs/protocol.md), signed with the agent's key
// and verified with the server key it pinned. It owns its handler through HttpMessageInvoker and sends HTTP/1.1 only,
// the headers of §4, Accept-Encoding: identity, and on outcome Expect: 100-continue. Each wait is a
// CancellationTokenSource on the clock, and a body beyond its read limit (§10) is lost unread.
internal sealed class AgentConnection : IDisposable
{
    // The reason of §9 when no status came.
    private const string Unreachable = "unreachable";

    private readonly Uri _poll;

    private readonly Uri _outcome;

    private readonly Guid _agent;

    // The agent's key, which signs every request, and the server key it pinned, which verifies every answer. Their
    // owner disposes them after the connection.
    private readonly ECDsa _agentKey;

    private readonly ECDsa _serverKey;

    private readonly HttpMessageInvoker _invoker;

    private readonly TimeProvider _clock;

    public AgentConnection(
        Uri server,
        Guid agent,
        ECDsa agentKey,
        ECDsa serverKey,
        HttpMessageHandler handler,
        TimeProvider clock)
    {
        _poll = ServerRequests.PathOf(server, OutpostRequestKindEnum.Poll);
        _outcome = ServerRequests.PathOf(server, OutpostRequestKindEnum.Outcome);
        _agent = agent;
        _agentKey = agentKey;
        _serverKey = serverKey;
        _invoker = new HttpMessageInvoker(handler);
        _clock = clock;
    }

    // Sends the poll and waits for its answer to begin for its pollTimeout plus leaseGrace, then reads the body within
    // requestTimeout and 64 MiB. An answer verified by §9 and ReadPollAnswer, with the sum of the poll's free, is
    // Answer; any other gives the reason of §9. A tunnel the proxy refuses, which the handler throws as an
    // HttpRequestException with its status, is a status that came: status-407.
    public async Task<PollResult> PollAsync(
        OutpostPollRequest poll,
        TimeSpan leaseGrace,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        byte[] body = OutpostCodec.Write(poll);
        byte[] signature = OutpostSignatures.SignRequest(_agentKey, OutpostRequestKindEnum.Poll, _agent, body);

        using HttpRequestMessage request = ServerRequests.Create(_poll, OutpostRequestKindEnum.Poll, _agent, body, signature);

        HttpResponseMessage? response;
        int? status;

        // The wait for the answer to begin ends before the wait for its body starts, so that its timer is gone by then.
        using (CancellationTokenSource timeout = new(poll.PollTimeout + leaseGrace, _clock))
        using (CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            (response, status) = await ServerRequests.SendAsync(_invoker, request, wait.Token, cancellationToken);
        }

        if (response is null)
        {
            return new PollResult(null, status is int came ? StatusReason(came) : Unreachable);
        }

        using (response)
        using (CancellationTokenSource timeout = new(requestTimeout, _clock))
        using (CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            ServerAnswer answer = await ServerRequests.ReadAsync(response, OutpostProtocol.PollAnswerLimit, wait.Token, cancellationToken);

            return Judge(poll, body, answer);
        }
    }

    // Sends the outcome bytes of the command and waits requestTimeout over the answer and its body, read within 64 KiB.
    // Confirmed for a verified 200; TooLarge for a verified result-too-large and for a 413 without an Outpost-Signature
    // that verifies, whatever its body; Lost otherwise. The answer is judged once, in the order of §9: no status is
    // Unreachable, the status of a proxy that refused the tunnel an Answer, and an answer as Judge gives it, with the
    // time of a verified confirmation or refusal, its status, and for any answer but a confirmation its cause, with the
    // code of a verified refusal.
    public async Task<OutcomeAnswer> SendOutcomeAsync(
        Guid command,
        ReadOnlyMemory<byte> outcome,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        byte[] body = OutpostCodec.Write(new OutpostOutcomeRequest(command, outcome));
        byte[] signature = OutpostSignatures.SignRequest(_agentKey, OutpostRequestKindEnum.Outcome, _agent, body);

        using HttpRequestMessage request = ServerRequests.Create(_outcome, OutpostRequestKindEnum.Outcome, _agent, body, signature);
        using CancellationTokenSource timeout = new(requestTimeout, _clock);
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        (HttpResponseMessage? response, int? status) = await ServerRequests.SendAsync(_invoker, request, wait.Token, cancellationToken);

        if (response is null)
        {
            ServerAnswerCauseEnum cause = status is null ? ServerAnswerCauseEnum.Unreachable : ServerAnswerCauseEnum.Answer;

            return new OutcomeAnswer(OutcomeResultEnum.Lost, null, status, cause, null);
        }

        using (response)
        {
            ServerAnswer answer = await ServerRequests.ReadAsync(response, OutpostProtocol.AnswerLimit, wait.Token, cancellationToken);

            return Judge(command, body, answer);
        }
    }

    // Disposes the invoker, and with it the handler.
    public void Dispose()
    {
        _invoker.Dispose();
    }

    private static OutcomeAnswer Lost(int status, ServerAnswerCauseEnum cause)
    {
        return new OutcomeAnswer(OutcomeResultEnum.Lost, null, status, cause, null);
    }

    private static string StatusReason(int status)
    {
        return "status-" + status.ToString(CultureInfo.InvariantCulture);
    }

    private static int TotalFree(OutpostPollRequest poll)
    {
        return poll.Bases.Sum(declared => declared.Read.Free + (declared.Write?.Free ?? 0));
    }

    // The table of §9 for a poll: a verified 200 is the answer; every other answer is lost with the first reason that
    // applies: the code of a verified refusal, protocol-version for the unsigned body of §9, or status- and the status.
    private PollResult Judge(OutpostPollRequest poll, byte[] requestBody, ServerAnswer answer)
    {
        if (answer.Status == (int)HttpStatusCode.OK && answer.IsJson && answer.Body is byte[] body)
        {
            try
            {
                OutpostPollAnswer read = OutpostCodec.ReadPollAnswer(
                    _serverKey,
                    _agent,
                    TotalFree(poll),
                    requestBody,
                    body,
                    answer.Signature);

                return new PollResult(read, null);
            }
            catch (OutpostProtocolException)
            {
                // Not verified: lost, with its status.
            }
        }

        if (VerifiedRefusal(OutpostRequestKindEnum.Poll, requestBody, answer) is OutpostRefusal refusal)
        {
            return new PollResult(null, refusal.Code);
        }

        if (answer.Status == (int)HttpStatusCode.BadRequest
            && answer.Body is byte[] unsigned
            && OutpostCodec.IsUnsupportedVersion(unsigned))
        {
            return new PollResult(null, OutpostProtocolCodes.ProtocolVersion);
        }

        return new PollResult(null, StatusReason(answer.Status));
    }

    // The table of §9 for an outcome, judged once in its order. A 200 is judged by JudgeConfirmation. A verified refusal
    // is a Refusal with its time and its code, TooLarge for result-too-large, the one code of a signed 413; the unsigned
    // body of §9 is a ProtocolVersion; a 401, 403 or 413 without an Outpost-Signature that verifies, whatever its body, is
    // Bare, the 413 TooLarge, as anything on the path sends one; anything else, a 413 whose signature verifies with
    // another body included, is an Answer and lost.
    private OutcomeAnswer Judge(Guid command, byte[] requestBody, ServerAnswer answer)
    {
        int status = answer.Status;

        if (status == (int)HttpStatusCode.OK)
        {
            return JudgeConfirmation(command, requestBody, answer);
        }

        if (VerifiedRefusal(OutpostRequestKindEnum.Outcome, requestBody, answer) is OutpostRefusal refusal)
        {
            OutcomeResultEnum result = refusal.Code == OutpostProtocolCodes.ResultTooLarge
                ? OutcomeResultEnum.TooLarge
                : OutcomeResultEnum.Lost;

            return new OutcomeAnswer(result, refusal.Time, status, ServerAnswerCauseEnum.Refusal, refusal.Code);
        }

        if (status == (int)HttpStatusCode.BadRequest
            && answer.Body is byte[] unsigned
            && OutpostCodec.IsUnsupportedVersion(unsigned))
        {
            return Lost(status, ServerAnswerCauseEnum.ProtocolVersion);
        }

        bool mayBeBare = status is (int)HttpStatusCode.Unauthorized
            or (int)HttpStatusCode.Forbidden
            or (int)HttpStatusCode.RequestEntityTooLarge;

        if (!mayBeBare || Verifies(status, requestBody, answer))
        {
            return Lost(status, ServerAnswerCauseEnum.Answer);
        }

        OutcomeResultEnum bare = status == (int)HttpStatusCode.RequestEntityTooLarge
            ? OutcomeResultEnum.TooLarge
            : OutcomeResultEnum.Lost;

        return new OutcomeAnswer(bare, null, status, ServerAnswerCauseEnum.Bare, null);
    }

    // A 200: Confirmed with the server's time when it verifies and confirms the command. Its signature is judged before
    // its form, so that an answer signed with another key, such as that of a hop that opens TLS, is a Signature; any
    // other 200 that is not a confirmation of this command, one without its whole body included, is an Answer.
    private OutcomeAnswer JudgeConfirmation(Guid command, byte[] requestBody, ServerAnswer answer)
    {
        int status = answer.Status;

        if (answer.Body is not byte[] body)
        {
            // The status came, and no complete body within the limit and the wait: an answer all the same (§9).
            return Lost(status, ServerAnswerCauseEnum.Answer);
        }

        if (!Verifies(status, requestBody, answer))
        {
            return Lost(status, ServerAnswerCauseEnum.Signature);
        }

        if (!answer.IsJson)
        {
            return Lost(status, ServerAnswerCauseEnum.Answer);
        }

        try
        {
            OutpostOutcomeAnswer confirmed = OutpostCodec.ReadOutcomeAnswer(_serverKey, _agent, command, requestBody, body, answer.Signature);

            return new OutcomeAnswer(OutcomeResultEnum.Confirmed, confirmed.Time, status, null, null);
        }
        catch (OutpostProtocolException)
        {
            return Lost(status, ServerAnswerCauseEnum.Answer);
        }
    }

    // Whether the answer carries an Outpost-Signature of the server key over this request, its status and its whole body.
    private bool Verifies(int status, byte[] requestBody, ServerAnswer answer)
    {
        return answer.Body is byte[] body
            && OutpostSignatures.VerifyAnswer(
                _serverKey,
                OutpostRequestKindEnum.Outcome,
                _agent,
                status,
                requestBody,
                body,
                answer.Signature);
    }

    // A verified signed refusal of §9, under the status that the table gives its code, or null for any other answer.
    private OutpostRefusal? VerifiedRefusal(OutpostRequestKindEnum kind, byte[] requestBody, ServerAnswer answer)
    {
        if (!answer.IsJson || answer.Body is not byte[] body)
        {
            return null;
        }

        try
        {
            return OutpostCodec.ReadRefusal(_serverKey, kind, _agent, answer.Status, requestBody, body, answer.Signature);
        }
        catch (OutpostProtocolException)
        {
            return null;
        }
    }
}
