using Moltaro.OnecOutpost.Agent.Checks;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using static Moltaro.OnecOutpost.Agent.Tests.Checks.TestChecks;
using static Moltaro.OnecOutpost.TestSupport.TestTimes;

namespace Moltaro.OnecOutpost.Agent.Tests.Checks;

// The checks of one command (concept rule 1, §7 of docs/protocol.md), in the order of §7: the signature
// over the exact bytes with the pinned key; then `protocol` and `id`; then, for an identity the audit log knows,
// nothing more; then the version and the rest of §7; then not-addressed, not-served, clock-skew and expired, judged by
// the agent's clock and the skew the command carries. Bytes that do not verify, and verified bytes that yield no
// `protocol` and `id`, name no command, so their refusal has no identity. A refusal of the version or of the form of
// §7 has the identity alone; one of step 4 the identity, the base and the mode.
public sealed class CommandCheckTests : IDisposable
{
    private readonly TestChecks _test = new();

    public void Dispose()
    {
        _test.Dispose();
    }

    // Rule 1: a command runs only when every check holds; here a Write of a base that the policy writes, and a Read of
    // a base it only reads.
    [Theory]
    [InlineData("accounting", "write")]
    [InlineData("warehouse", "read")]
    public void A_command_that_passes_every_check_runs_with_the_command_read_from_its_bytes(string baseKey, string mode)
    {
        OutpostSignedCommand signed = _test.Signed(Command(baseKey, TestCommands.ModeOf(mode)));

        CommandCheck check = _test.Checks.Check(signed, _test.Log);

        Assert.Equal(CommandVerdictEnum.Runs, check.Verdict);
        Assert.Null(check.Code);
        Assert.NotNull(check.Command);
        Assert.Equal(signed.Bytes, OutpostCodec.Write(check.Command));
    }

    // Rule 1 and step 1 of §7: nothing in bytes that do not verify with the pinned key can be trusted, their identity
    // included, so the refusal names no command, and only the audit log records it. It comes first, before the audit
    // log is asked about the identity the bytes claim.
    [Theory]
    [InlineData("signed with another key")]
    [InlineData("changed by one byte after it was signed")]
    [InlineData("of an identity the audit log knows, signed with another key")]
    public void A_command_whose_signature_does_not_verify_is_refused_with_signature_and_no_identity(string form)
    {
        OutpostSignedCommand signed = form switch
        {
            "signed with another key" => _test.Forged(Command("accounting", OutpostModeEnum.Write)),
            "changed by one byte after it was signed" => ChangedByOneByte(_test.Signed(Command("accounting", OutpostModeEnum.Write))),
            _ => _test.Forged(Command("accounting", OutpostModeEnum.Write) with { Id = KnownId }),
        };

        CommandCheck check = _test.Checks.Check(signed, _test.Log);

        Assert.Equal(new CommandCheck(CommandVerdictEnum.Refused, null, null, null, "signature", null), check);
    }

    // Step 2 of §7: verified bytes that do not yield both a `protocol` and an `id` name no command that a refusal could
    // answer, so the refusal has no identity. An `id` without a `protocol` is no identity, even one the log knows.
    [Theory]
    [InlineData("Result = 1;")]
    [InlineData("""{"protocol":1}""")]
    [InlineData("""{"id":"0b6f2c1d-3e4a-4b5c-8d6e-7f8091a2b3c4","mode":"write"}""")]
    public void Verified_bytes_that_yield_no_protocol_and_identity_are_refused_with_form_and_no_identity(string text)
    {
        CommandCheck check = _test.Checks.Check(_test.Signed(text), _test.Log);

        Assert.Equal(new CommandCheck(CommandVerdictEnum.Refused, null, null, null, "form", null), check);
    }

    // Rule 3 and step 2 of §7: a command whose identity the audit log holds ran, or was refused, before. Nothing more
    // is done with its bytes, whatever they hold, so its recorded outcome, or its refusal, stays as it is.
    [Theory]
    [InlineData("the same identity again")]
    [InlineData("another version of the protocol")]
    [InlineData("a Write without a caller key")]
    [InlineData("a deadline that passed")]
    public void A_command_whose_identity_the_audit_log_knows_is_known_whatever_the_rest_of_its_bytes(string form)
    {
        OutpostCommandMessage write = Command("accounting", OutpostModeEnum.Write) with { Id = KnownId };
        OutpostSignedCommand signed = form switch
        {
            "the same identity again" => _test.Signed(write),
            "another version of the protocol" => _test.Signed($$"""{"protocol":2,"id":"{{KnownId}}"}"""),
            "a Write without a caller key" => _test.Signed(write with { CallerKey = null }),
            _ => _test.Signed(DeadlineAt(write, Now - Skew - Millisecond)),
        };

        CommandCheck check = _test.Checks.Check(signed, _test.Log);

        Assert.Equal(CommandVerdictEnum.Known, check.Verdict);
        Assert.Equal(KnownId, check.Id);
    }

    // Step 2 of §7: a command of another version is refused under its identity, and the rest of its bytes is not read,
    // so the refusal names no base and no mode, and no check of step 4 is made: here it names another agent.
    [Fact]
    public void A_command_of_another_version_of_the_protocol_is_refused_with_its_identity_alone()
    {
        Guid id = Guid.NewGuid();
        OutpostSignedCommand signed = _test.Signed(
            $$"""{"protocol":2,"id":"{{id}}","agent":"{{Guid.NewGuid()}}","base":"accounting","mode":"write"}""");

        CommandCheck check = _test.Checks.Check(signed, _test.Log);

        Assert.Equal(new CommandCheck(CommandVerdictEnum.Refused, id, null, null, "protocol-version", null), check);
    }

    // Step 3 of §7: a verified command of this version that breaks §7 does not run, and is refused with malformed under
    // its identity alone. It comes before the checks of step 4: here it also names another agent.
    [Fact]
    public void A_verified_command_that_breaks_section_7_is_refused_with_malformed_and_its_identity_alone()
    {
        OutpostCommandMessage command = Command("accounting", OutpostModeEnum.Write) with
        {
            CallerKey = null,
            Agent = Guid.NewGuid(),
        };

        CommandCheck check = _test.Checks.Check(_test.Signed(command), _test.Log);

        Assert.Equal(new CommandCheck(CommandVerdictEnum.Refused, command.Id, null, null, "malformed", null), check);
    }

    // Step 4 of §7 and rule 1: each check of the agent's identity, its local policy and its clock refuses with its own
    // code, naming the identity, the base and the mode of the command. The skew is the one the command carries.
    [Theory]
    [InlineData("addressed to another agent", "accounting", "write", "not-addressed")]
    [InlineData("for a base the policy does not hold", "payroll", "read", "not-served")]
    [InlineData("a Write of a base the policy does not write", "warehouse", "write", "not-served")]
    [InlineData("issued a millisecond more than the skew ahead", "accounting", "read", "clock-skew")]
    [InlineData("whose deadline passed a millisecond more than the skew ago", "accounting", "write", "expired")]
    public void Each_check_of_step_4_refuses_with_its_code_and_the_identity_the_base_and_the_mode(
        string form,
        string baseKey,
        string modeWord,
        string code)
    {
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        OutpostCommandMessage command = Command(baseKey, mode);
        command = form switch
        {
            "addressed to another agent" => command with { Agent = Guid.NewGuid() },
            "issued a millisecond more than the skew ahead" => IssuedAt(command, Now + Skew + Millisecond),
            "whose deadline passed a millisecond more than the skew ago" => DeadlineAt(command, Now - Skew - Millisecond),
            _ => command,
        };

        CommandCheck check = _test.Checks.Check(_test.Signed(command), _test.Log);

        Assert.Equal(new CommandCheck(CommandVerdictEnum.Refused, command.Id, baseKey, mode, code, null), check);
    }

    // Rule 1: by the agent's clock, a command issued no later than the skew ahead, and whose deadline passed no longer
    // than the skew ago, runs.
    [Theory]
    [InlineData("issued exactly the skew ahead")]
    [InlineData("whose deadline passed exactly the skew ago")]
    public void A_command_at_either_bound_of_its_skew_runs(string form)
    {
        OutpostCommandMessage command = form == "issued exactly the skew ahead"
            ? IssuedAt(Command("accounting", OutpostModeEnum.Read), Now + Skew)
            : DeadlineAt(Command("accounting", OutpostModeEnum.Read), Now - Skew);

        CommandCheck check = _test.Checks.Check(_test.Signed(command), _test.Log);

        Assert.Equal(CommandVerdictEnum.Runs, check.Verdict);
    }

    // Step 4 of §7: the checks run in their order, and the first that fails gives the code.
    [Theory]
    [InlineData("addressed to another agent, for a base the policy does not hold, expired", "not-addressed")]
    [InlineData("a Write of a base the policy does not write, issued beyond the skew ahead", "not-served")]
    public void When_several_checks_of_step_4_fail_the_first_in_the_order_of_section_7_gives_the_code(
        string form,
        string code)
    {
        OutpostCommandMessage command = form.StartsWith("addressed", StringComparison.Ordinal)
            ? DeadlineAt(
                Command("payroll", OutpostModeEnum.Write) with { Agent = Guid.NewGuid() },
                Now - Skew - Millisecond)
            : IssuedAt(Command("warehouse", OutpostModeEnum.Write), Now + Skew + Millisecond);

        CommandCheck check = _test.Checks.Check(_test.Signed(command), _test.Log);

        Assert.Equal(CommandVerdictEnum.Refused, check.Verdict);
        Assert.Equal(code, check.Code);
    }

    // The bytes of a signed command with one digit of its outcome limit changed: still a command, signed no more.
    private static OutpostSignedCommand ChangedByOneByte(OutpostSignedCommand signed)
    {
        byte[] bytes = [.. signed.Bytes];
        bytes[^2] ^= 1;

        return new OutpostSignedCommand(bytes, signed.Signature);
    }
}
