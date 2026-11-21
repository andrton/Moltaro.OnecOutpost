using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// The fit of a code to a kind, stated in full: the kinds column of the table of codes of the contract. A code that
// belongs to two kinds fits both, since only the mode of the command decides between them. The codes are written out
// as the contract gives them, so a constant that drifts fails too.
public sealed class OutcomeCodeFitTests
{
    // The code, and whether it belongs to Failed, to Refused and to Unknown.
    public static TheoryData<string, bool, bool, bool> CodeTable => new()
    {
        { "script-error", true, false, false },
        { "result-type", true, false, true },
        { "result-too-large", true, false, true },
        { "run-limit", true, false, true },
        { "lost", true, false, false },
        { "caller-key-conflict", false, true, false },
        { "unknown-base", false, true, false },
        { "mode-closed", false, true, false },
        { "not-served", false, true, false },
        { "not-addressed", false, true, false },
        { "protocol-version", false, true, false },
        { "malformed", false, true, false },
        { "expired", false, true, false },
        { "clock-skew", false, true, false },
        { "revoked", false, true, true },
        { "disconnected", false, false, true },
        { "agent-stopped", false, false, true },
        { "transaction-ended", false, false, true },

        // A text that is no code, and codes of the codec that no outcome carries.
        { "timeout", false, false, false },
        { "form", false, false, false },
        { "signature", false, false, false },
        { "", false, false, false },
    };

    // The codes of a failure of the connection to 1C, which the COM worker reports (S9): the same columns.
    public static TheoryData<string, bool, bool, bool> CodeTableOfTheConnectionTo1C => new()
    {
        { "base-unreachable", true, false, false },
        { "connection-lost", true, false, true },
    };

    [Theory]
    [MemberData(nameof(CodeTable))]
    public void A_code_fits_exactly_its_kinds(string code, bool failed, bool refused, bool unknown)
    {
        Assert.Equal(failed, OutpostOutcomeCodes.FitsFailed(code));
        Assert.Equal(refused, OutpostOutcomeCodes.FitsRefused(code));
        Assert.Equal(unknown, OutpostOutcomeCodes.FitsUnknown(code));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(CodeTableOfTheConnectionTo1C))]
    public void A_code_of_the_connection_to_1C_fits_exactly_its_kinds(string code, bool failed, bool refused, bool unknown)
    {
        Assert.Equal(failed, OutpostOutcomeCodes.FitsFailed(code));
        Assert.Equal(refused, OutpostOutcomeCodes.FitsRefused(code));
        Assert.Equal(unknown, OutpostOutcomeCodes.FitsUnknown(code));
    }

    [Fact]
    public void Every_code_has_the_text_that_the_contract_gives_it()
    {
        Assert.Equal("script-error", OutpostOutcomeCodes.ScriptError);
        Assert.Equal("result-type", OutpostOutcomeCodes.ResultType);
        Assert.Equal("result-too-large", OutpostOutcomeCodes.ResultTooLarge);
        Assert.Equal("run-limit", OutpostOutcomeCodes.RunLimit);
        Assert.Equal("lost", OutpostOutcomeCodes.Lost);
        Assert.Equal("caller-key-conflict", OutpostOutcomeCodes.CallerKeyConflict);
        Assert.Equal("unknown-base", OutpostOutcomeCodes.UnknownBase);
        Assert.Equal("mode-closed", OutpostOutcomeCodes.ModeClosed);
        Assert.Equal("not-served", OutpostOutcomeCodes.NotServed);
        Assert.Equal("not-addressed", OutpostOutcomeCodes.NotAddressed);
        Assert.Equal("protocol-version", OutpostOutcomeCodes.ProtocolVersion);
        Assert.Equal("malformed", OutpostOutcomeCodes.Malformed);
        Assert.Equal("expired", OutpostOutcomeCodes.Expired);
        Assert.Equal("clock-skew", OutpostOutcomeCodes.ClockSkew);
        Assert.Equal("revoked", OutpostOutcomeCodes.Revoked);
        Assert.Equal("disconnected", OutpostOutcomeCodes.Disconnected);
        Assert.Equal("agent-stopped", OutpostOutcomeCodes.AgentStopped);
        Assert.Equal("transaction-ended", OutpostOutcomeCodes.TransactionEnded);
        Assert.Equal("base-unreachable", OutpostOutcomeCodes.BaseUnreachable);
        Assert.Equal("connection-lost", OutpostOutcomeCodes.ConnectionLost);
    }
}
