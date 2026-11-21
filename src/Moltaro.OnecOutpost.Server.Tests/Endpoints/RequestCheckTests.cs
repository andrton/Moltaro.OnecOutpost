using System.Buffers.Text;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Endpoints;

// The checks of §9 of docs/protocol.md that the server makes before it reads the body of a request, over loopback HTTP
// written by hand (RawExchange), in their order: the version of the path, the headers of §4, the length against the
// limits of §10, and for poll and outcome whether the agent has a key and is not revoked. Each refusal of these checks
// is unsigned and comes before the body is read: the body is sent only when the server asks for it, and every body
// that a refusal leaves unread would otherwise pass. The outcome size limit is the least the option allows, so that
// the limit of an outcome body shows that it follows the option.
[Trait("Requires", "PostgreSQL")]
public sealed class RequestCheckTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    public static TheoryData<string> Requests => ["pair", "poll", "outcome"];

    // The faults of the headers that §4 requires, in full for poll. Every request runs the same check; pair and
    // outcome show that they run it for each header.
    public static TheoryData<string, string, HeaderFaultEnum> HeaderFaults
    {
        get
        {
            TheoryData<string, string, HeaderFaultEnum> faults = [];
            string[] headers = ["Content-Type", OutpostProtocol.AgentHeader, OutpostProtocol.SignatureHeader];

            foreach (string header in headers)
            {
                foreach (HeaderFaultEnum fault in Enum.GetValues<HeaderFaultEnum>())
                {
                    faults.Add("poll", header, fault);
                }

                faults.Add("pair", header, HeaderFaultEnum.Missing);
                faults.Add("outcome", header, HeaderFaultEnum.Missing);
            }

            return faults;
        }
    }

    // The request carries none of the headers of v1: the version is checked first, and the form of its answer is one
    // that every later version keeps.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("v2/poll")]
    [InlineData("v2/status")]
    public async Task A_version_other_than_v1_gets_the_unsigned_protocol_version_before_the_body_is_read(string request)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"version_{request.Replace('/', '_')}", cancellationToken);

        RawAnswer answer = await RawExchange.SendAsync(
            server.Address,
            request,
            [],
            "{}"u8.ToArray(),
            cancellationToken);

        Assert.Equal(400, answer.Status);
        Assert.Equal(OutpostCodec.WriteUnsupportedVersion(), answer.Body);
        Assert.Null(answer.Signature);
        Assert.False(answer.BodySent);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(HeaderFaults))]
    public async Task A_required_header_that_is_missing_repeated_or_in_another_form_gets_a_bare_400_before_the_body_is_read(
        string request,
        string header,
        HeaderFaultEnum fault)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string schemaName = $"header_{request}_{header.Replace('-', '_')}_{fault}";
        await using LoopbackServer server = await StartAsync(schemaName, cancellationToken);
        using TestAgent agent = await AgentForAsync(server, request, cancellationToken);
        byte[] body = GoodBody(server, agent, request);
        List<(string Name, string Value)> headers = agent.Headers(agent.Sign(Kind(request), body));

        RawAnswer answer = await RawExchange.SendAsync(
            server.Address,
            "v1/" + request,
            Break(headers, header, fault),
            body,
            cancellationToken);

        Assert.Equal(400, answer.Status);
        Assert.True(answer.IsBare);
        Assert.False(answer.BodySent);
    }

    // Even identity, which names no coding: §9 refuses a request that has a Content-Encoding at all.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Requests))]
    public async Task A_Content_Encoding_gets_a_bare_400_before_the_body_is_read(string request)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"content_encoding_{request}", cancellationToken);
        using TestAgent agent = await AgentForAsync(server, request, cancellationToken);
        byte[] body = GoodBody(server, agent, request);
        List<(string Name, string Value)> headers = agent.Headers(agent.Sign(Kind(request), body));

        RawAnswer answer = await RawExchange.SendAsync(
            server.Address,
            "v1/" + request,
            [.. headers, ("Content-Encoding", "identity")],
            body,
            cancellationToken);

        Assert.Equal(400, answer.Status);
        Assert.True(answer.IsBare);
        Assert.False(answer.BodySent);
    }

    // §4: the agent sends an outcome with Expect: 100-continue and its length, but a hop may drop either, so the server
    // requires neither. The body is good, so the answer that comes once it is read is neither a 400 nor a 413.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("Expect")]
    [InlineData("Content-Length")]
    public async Task An_outcome_without_Expect_or_without_Content_Length_is_not_answered_with_a_bare_400_or_413(
        string dropped)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string schemaName = $"outcome_without_{dropped.Replace('-', '_')}";
        await using LoopbackServer server = await StartAsync(schemaName, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        byte[] body = GoodBody(server, agent, "outcome");
        List<(string Name, string Value)> headers = agent.Headers(agent.Sign(OutpostRequestKindEnum.Outcome, body));
        int? status;

        if (dropped == "Expect")
        {
            RawAnswer answer = await RawExchange.SendWithoutExpectAsync(
                server.Address,
                "v1/outcome",
                headers,
                body,
                cancellationToken);

            status = answer.Status;
        }
        else
        {
            status = await RawExchange.SendChunkedAsync(server.Address, "v1/outcome", headers, body, cancellationToken);
        }

        Assert.NotNull(status);
        Assert.NotEqual(400, status);
        Assert.NotEqual(413, status);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("poll", "unknown", 401)]
    [InlineData("poll", "pending", 401)]
    [InlineData("poll", "revoked", 403)]
    [InlineData("outcome", "unknown", 401)]
    [InlineData("outcome", "pending", 401)]
    [InlineData("outcome", "revoked", 403)]
    public async Task An_unknown_or_pending_agent_gets_a_bare_401_and_a_revoked_one_a_bare_403_before_the_body_is_read(
        string request,
        string agentCase,
        int status)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"agent_{request}_{agentCase}", cancellationToken);
        using TestAgent agent = await server.CreateAgentAsync(cancellationToken);
        Guid agentId = agent.Id;

        if (agentCase == "unknown")
        {
            agentId = Guid.NewGuid();
        }

        if (agentCase == "revoked")
        {
            await agent.PairAsync(cancellationToken);
            Assert.True(await server.Server.RevokeAgentAsync(agent.Id, cancellationToken));
        }

        byte[] body = GoodBody(server, agent, request);
        byte[] signature = agent.SignAs(agentId, Kind(request), body);
        List<(string Name, string Value)> headers =
        [
            ("Content-Type", "application/json"),
            (OutpostProtocol.AgentHeader, OutpostHeaders.FormatAgent(agentId)),
            (OutpostProtocol.SignatureHeader, OutpostHeaders.FormatSignature(signature)),
        ];

        RawAnswer answer = await RawExchange.SendAsync(
            server.Address,
            "v1/" + request,
            headers,
            body,
            cancellationToken);

        Assert.Equal(status, answer.Status);
        Assert.True(answer.IsBare);
        Assert.False(answer.BodySent);
    }

    // The body is white space of exactly the limit, signed: whatever the server answers once it has read it, it is not
    // a refusal for the length.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Requests))]
    public async Task A_body_at_its_limit_is_not_answered_with_a_bare_413(string request)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"at_limit_{request}", cancellationToken);
        using TestAgent agent = await AgentForAsync(server, request, cancellationToken);
        byte[] body = Spaces(Limit(server, request));

        RawAnswer answer = await RawExchange.SendAsync(
            server.Address,
            "v1/" + request,
            agent.Headers(agent.Sign(Kind(request), body)),
            body,
            cancellationToken);

        Assert.True(answer.BodySent);
        Assert.NotEqual(413, answer.Status);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Requests))]
    public async Task A_body_one_byte_beyond_its_limit_with_a_declared_length_gets_a_bare_413_before_it_is_read(string request)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"beyond_limit_{request}", cancellationToken);
        using TestAgent agent = await AgentForAsync(server, request, cancellationToken);
        byte[] body = Spaces(Limit(server, request) + 1);

        RawAnswer answer = await RawExchange.SendAsync(
            server.Address,
            "v1/" + request,
            agent.Headers(agent.Sign(Kind(request), body)),
            body,
            cancellationToken);

        Assert.Equal(413, answer.Status);
        Assert.True(answer.IsBare);
        Assert.False(answer.BodySent);
    }

    // The body declares no length, as when a hop drops it, so the server finds it too long only while it reads it. It
    // is a good request padded with white space to one byte beyond the limit, which a server that read it whole would
    // act on. A 413 sent while the client still sends the body may come, or the connection may be reset first.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Requests))]
    public async Task A_chunked_body_one_byte_beyond_its_limit_ends_in_a_413_or_a_reset_and_changes_nothing(string request)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"chunked_beyond_limit_{request}", cancellationToken);
        using TestAgent agent = await AgentForAsync(server, request, cancellationToken);
        byte[] good = GoodBody(server, agent, request);
        byte[] body = [.. good, .. Spaces(Limit(server, request) + 1 - good.Length)];
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        int? status = await RawExchange.SendChunkedAsync(
            server.Address,
            "v1/" + request,
            agent.Headers(agent.Sign(Kind(request), body)),
            body,
            cancellationToken);

        Assert.True(status is null or 413, $"The request ended in {status}, neither a 413 nor a reset.");
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    private Task<LoopbackServer> StartAsync(string schemaName, CancellationToken cancellationToken)
    {
        return LoopbackServer.StartAsync(
            postgres,
            schemaName.ToLowerInvariant(),
            options => options.OutcomeSizeLimit = OutpostProtocol.MinOutcomeSizeLimit,
            cancellationToken);
    }

    // The agent of a request that passes every check but the one under test: for pair, one with a token of its own
    // that it has not used; for poll and outcome, a paired one.
    private static async Task<TestAgent> AgentForAsync(
        LoopbackServer server,
        string request,
        CancellationToken cancellationToken)
    {
        return request == "pair"
            ? await server.CreateAgentAsync(cancellationToken)
            : await server.PairAgentAsync(cancellationToken);
    }

    // A body of the request that a server reading it whole would act on: the agent's pairing, its next poll, or the
    // outcome that the console sends for a command it made up (§12).
    private static byte[] GoodBody(LoopbackServer server, TestAgent agent, string request)
    {
        if (request == "pair")
        {
            return agent.PairBody();
        }

        if (request == "poll")
        {
            return OutpostCodec.Write(agent.NextPoll(server.Options.PollTimeout, [ReadOnly("accounting")]));
        }

        OutpostOutcomeMessage outcome = new(
            OutpostAgentOutcomeKindEnum.Refused,
            OutpostOutcomeCodes.NotAddressed,
            null,
            null,
            [],
            false);

        return OutpostCodec.Write(new OutpostOutcomeRequest(Guid.NewGuid(), OutpostCodec.Write(outcome)));
    }

    // The body limits of §10, the outcome's following the option.
    private static int Limit(LoopbackServer server, string request)
    {
        return request switch
        {
            "pair" => OutpostProtocol.PairBodyLimit,
            "poll" => OutpostProtocol.PollBodyLimit,
            _ => server.Options.OutcomeSizeLimit + OutpostProtocol.OutcomeEnvelopeAllowance,
        };
    }

    private static OutpostRequestKindEnum Kind(string request)
    {
        return request switch
        {
            "pair" => OutpostRequestKindEnum.Pair,
            "poll" => OutpostRequestKindEnum.Poll,
            _ => OutpostRequestKindEnum.Outcome,
        };
    }

    private static List<(string Name, string Value)> Break(
        List<(string Name, string Value)> headers,
        string header,
        HeaderFaultEnum fault)
    {
        string value = headers.Single(line => line.Name == header).Value;
        List<(string Name, string Value)> others = [.. headers.Where(line => line.Name != header)];

        return fault switch
        {
            HeaderFaultEnum.Missing => others,
            HeaderFaultEnum.Repeated => [.. headers, (header, value)],
            HeaderFaultEnum.OtherForm => [.. others, (header, OtherForm(header, value))],
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
    }

    // The same value in another form: the media type with a parameter, the identity in uppercase, the signature in
    // base64 with padding.
    private static string OtherForm(string header, string value)
    {
        return header switch
        {
            "Content-Type" => "application/json; charset=utf-8",
            OutpostProtocol.AgentHeader => value.ToUpperInvariant(),
            _ => Convert.ToBase64String(Base64Url.DecodeFromChars(value)),
        };
    }

    private static byte[] Spaces(int count)
    {
        byte[] spaces = new byte[count];
        Array.Fill(spaces, (byte)' ');

        return spaces;
    }
}
