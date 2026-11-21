using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Secrets;

// The password stays on its machine (principle 9): it appears in no output of the worker. 1C or .NET may quote a value
// it refused, so the worker replaces the password with <password> in every text it writes to its diagnostics, and an
// outcome it writes itself carries no text of a failure (the brief of S9, "The lines").
[Trait("Requires", "Windows")]
public sealed class PasswordTests
{
    private const string FailureText = $"The fake refused the password {LineText.Password} of the account.";

    private const string ReplacedFailureText = "The fake refused the password <password> of the account.";

    // Where the failure occurs: the session, the executor or the call of Run.
    public static TheoryData<string> PlacesOfAFailure => ["connect", "open executor", "run"];

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(PlacesOfAFailure))]
    public void The_password_appears_in_no_output_when_the_text_of_a_failure_quotes_it(string place)
    {
        WorkerRun run = RunWithAFailure(place);

        Assert.DoesNotContain(LineText.Password, run.Output.Text);
        Assert.DoesNotContain(LineText.Password, run.Diagnostics);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(PlacesOfAFailure))]
    public void The_text_of_a_failure_goes_to_the_diagnostics_with_the_password_replaced_by_a_placeholder(string place)
    {
        WorkerRun run = RunWithAFailure(place);

        Assert.Contains(ReplacedFailureText, run.Diagnostics);
    }

    // A Write worker, so that a Run that throws gives Unknown, and a command after the failure that the worker never
    // reaches.
    private static WorkerRun RunWithAFailure(string place)
    {
        InvalidOperationException failure = new(FailureText);

        FakeSession session = place switch
        {
            "open executor" => new FakeSession { OpenFailure = failure },
            "run" => new FakeSession(() => throw failure),
            _ => new FakeSession(),
        };

        FakeConnector connector = new(session) { Failure = place == "connect" ? failure : null };

        return WorkerRun.Of(
            WorkerModeEnum.Write,
            connector,
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());
    }
}
