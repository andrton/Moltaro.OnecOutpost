using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The console's own account of an answer to outcome that it does not accept (§9 and §12 of docs/protocol.md), the
// table that the server's line of check reads, as pair reads it for
// pair: HTTP {status}: first when a status came, then the sentence of the cause, and for a verified refusal that of its
// code. Nothing in it is taken from a body. The answers to pair are PairRefusalTests'.
public sealed class ServerAnswerSentencesTests
{
    [Theory]
    [InlineData("Unreachable", null, null, "no answer came: the server, or the proxy of the policy, could not be reached, or did not answer within the requestTimeout of the policy")]
    [InlineData("ProtocolVersion", 400, null, "HTTP 400: an unsigned answer in the form the server uses for a protocol version it does not speak")]
    [InlineData("Signature", 200, null, "HTTP 200: the answer's signature does not verify")]
    [InlineData("Refusal", 401, "signature", "HTTP 401: the server does not verify the agent's signature: secrets.dat and agent.json come from different pairings")]
    [InlineData("Refusal", 400, "form", "HTTP 400: the server refused the form of the request")]
    [InlineData("Refusal", 413, "result-too-large", "HTTP 413: the server refused even this small outcome for its size")]
    [InlineData("Bare", 401, null, "HTTP 401: the server knows no such agent, or it is still pending")]
    [InlineData("Bare", 403, null, "HTTP 403: the agent is revoked, or something on the path refused it")]
    [InlineData("Bare", 413, null, "HTTP 413: something on the path refused even this small body")]
    [InlineData("Answer", 500, null, "HTTP 500: an answer that is not a signed answer of §6.3 or §9 of the protocol")]
    [InlineData("Answer", 407, null, "HTTP 407: an answer that is not a signed answer of §6.3 or §9 of the protocol")]
    public void Each_answer_to_an_outcome_that_the_console_does_not_accept_has_a_sentence_of_its_own(
        string cause,
        int? status,
        string? code,
        string sentence)
    {
        Assert.Equal(
            sentence,
            ServerAnswerSentences.Of(OutpostRequestKindEnum.Outcome, Enum.Parse<ServerAnswerCauseEnum>(cause), status, code));
    }
}
