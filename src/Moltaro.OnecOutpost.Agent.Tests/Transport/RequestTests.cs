using System.Net;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// The requests of the agent's connection (§1, §2, §4 and §11 of docs/protocol.md, the S6 brief, "AgentConnection"): a
// POST over HTTP/1.1 to {prefix}/v1/{request} of the server's address, with the exact bytes it signed by the writer of
// §2, Content-Type application/json, Outpost-Agent, Outpost-Signature over the body with the agent's key,
// Accept-Encoding identity, no Content-Encoding and no credentials; on outcome, Expect: 100-continue. The connection
// owns its handler.
public sealed class RequestTests
{
    // The server's address is the prefix that the application gave MapOutpostAgents, with or without its last slash.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("https://outpost.example.com/agents")]
    [InlineData("https://outpost.example.com/agents/")]
    public async Task A_poll_is_a_POST_over_HTTP_1_1_of_the_exact_bytes_it_signed_with_the_headers_of_section_4(
        string address)
    {
        using TestConnection test = new(new Uri(address));
        OutpostPollRequest poll = test.Poll();

        Task<PollResult> result = test.PollAsync(poll);
        ServerExchange exchange = await test.Server.NextPollAsync();

        Assert.Equal(new Uri("https://outpost.example.com/agents/v1/poll"), exchange.Uri);
        AssertSentAsSection4Says(exchange);
        Assert.Equal(OutpostCodec.Write(poll), exchange.Body);
        exchange.ReadPoll();
        Assert.NotEqual(true, exchange.ExpectContinue);
        Assert.Empty(exchange.Values("Expect"));

        exchange.AnswerPoll(test.Server.Ordinary());
        Assert.NotNull((await result).Answer);
    }

    // The outcome object goes byte for byte as the executor wrote it, white space included (§8).
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_outcome_is_a_POST_with_Expect_100_continue_of_the_exact_bytes_of_its_outcome_object()
    {
        using TestConnection test = new();
        Guid command = Guid.NewGuid();
        byte[] outcome = TestOutcomes.Succeeded();

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(command, outcome);
        ServerExchange exchange = await test.Server.NextOutcomeAsync();

        Assert.Equal(new Uri("https://outpost.example.com/agents/v1/outcome"), exchange.Uri);
        AssertSentAsSection4Says(exchange);
        Assert.True(exchange.ExpectContinue);
        Assert.Equal(OutpostCodec.Write(new OutpostOutcomeRequest(command, outcome)), exchange.Body);
        Assert.Equal(command, exchange.ReadOutcome().Command);
        Assert.Equal(outcome, exchange.OutcomeBytes());

        exchange.Confirm();
        Assert.Equal(OutcomeResultEnum.Confirmed, await result);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Disposing_the_connection_disposes_its_handler()
    {
        TestConnection test = new();

        test.Dispose();

        Assert.True(test.Server.IsDisposed);
    }

    // The signature is verified by ReadPoll and ReadOutcome, as the server verifies it.
    private static void AssertSentAsSection4Says(ServerExchange exchange)
    {
        Assert.Equal(HttpMethod.Post, exchange.Method);
        Assert.Equal(HttpVersion.Version11, exchange.Version);
        Assert.NotEqual(HttpVersionPolicy.RequestVersionOrHigher, exchange.VersionPolicy);
        Assert.Equal("application/json", exchange.ContentType);
        Assert.Equal(exchange.Body.Length, exchange.ContentLength);
        Assert.Equal(PairedFolder.Agent.ToString("D"), exchange.Single(OutpostProtocol.AgentHeader));
        Assert.Equal(PairedFolder.Agent, exchange.Agent);
        Assert.Equal(64, exchange.Signature.Length);
        Assert.Equal<string>(["identity"], exchange.Values("Accept-Encoding"));
        Assert.Empty(exchange.Values("Content-Encoding"));
        Assert.Empty(exchange.Values("Authorization"));
        Assert.Empty(exchange.Values("Proxy-Authorization"));
    }
}
