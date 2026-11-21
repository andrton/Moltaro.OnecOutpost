namespace Moltaro.OnecOutpost.Protocol.Tests.Limits;

// The fixed values of protocol v1, written out as docs/protocol.md gives them, so that a constant that drifts from
// the document fails. Both sides check the same limits, and widening a range needs a new version of the protocol (§2).
public sealed class ProtocolConstantTests
{
    [Fact]
    public void The_version_is_1()
    {
        Assert.Equal(1, OutpostProtocol.Version);
    }

    [Fact]
    public void The_limits_are_those_of_section_10()
    {
        Assert.Equal(16 * 1024, OutpostProtocol.PairBodyLimit);
        Assert.Equal(1024 * 1024, OutpostProtocol.PollBodyLimit);
        Assert.Equal(4 * 1024, OutpostProtocol.OutcomeEnvelopeAllowance);
        Assert.Equal(64 * 1024 * 1024, OutpostProtocol.PollAnswerLimit);
        Assert.Equal(64 * 1024, OutpostProtocol.AnswerLimit);
        Assert.Equal(72, OutpostProtocol.MaxMessageDepth);
        Assert.Equal(64, OutpostProtocol.MaxValueDepth);
        Assert.Equal(256, OutpostProtocol.MaxBases);
        Assert.Equal(10_000, OutpostProtocol.MaxReceived);
        Assert.Equal(4_611_686_018_427_387_904, OutpostProtocol.MaxPollNumber);
    }

    [Fact]
    public void The_ranges_are_those_of_the_options_and_of_the_local_policy_that_they_mirror()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), OutpostProtocol.MinClockSkew);
        Assert.Equal(TimeSpan.FromHours(1), OutpostProtocol.MaxClockSkew);
        Assert.Equal(TimeSpan.FromSeconds(1), OutpostProtocol.MinTakeTimeout);
        Assert.Equal(TimeSpan.FromHours(1), OutpostProtocol.MaxTakeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), OutpostProtocol.MinPollTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), OutpostProtocol.MaxPollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), OutpostProtocol.MinLeaseGrace);
        Assert.Equal(TimeSpan.FromMinutes(5), OutpostProtocol.MaxLeaseGrace);
        Assert.Equal(TimeSpan.FromSeconds(1), OutpostProtocol.MinRunLimit);
        Assert.Equal(TimeSpan.FromDays(1), OutpostProtocol.MaxRunLimit);
        Assert.Equal(64 * 1024, OutpostProtocol.MinOutcomeSizeLimit);
        Assert.Equal(64 * 1024 * 1024, OutpostProtocol.MaxOutcomeSizeLimit);
        Assert.Equal(4 * 1024, OutpostProtocol.MinCommandSizeLimit);
        Assert.Equal(16 * 1024 * 1024, OutpostProtocol.MaxCommandSizeLimit);
        Assert.Equal(8, OutpostProtocol.MaxWorkers);
    }

    [Fact]
    public void The_headers_are_named_as_section_4_names_them()
    {
        Assert.Equal("Outpost-Agent", OutpostProtocol.AgentHeader);
        Assert.Equal("Outpost-Signature", OutpostProtocol.SignatureHeader);
    }

    [Fact]
    public void The_codes_of_a_refusal_are_those_of_section_9_and_the_one_code_that_is_never_sent()
    {
        Assert.Equal("form", OutpostProtocolCodes.Form);
        Assert.Equal("signature", OutpostProtocolCodes.Signature);
        Assert.Equal("result-too-large", OutpostProtocolCodes.ResultTooLarge);
        Assert.Equal("protocol-version", OutpostProtocolCodes.ProtocolVersion);
        Assert.Equal("server-key", OutpostProtocolCodes.ServerKey);
    }
}
