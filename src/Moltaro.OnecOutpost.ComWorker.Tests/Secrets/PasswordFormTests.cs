using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Secrets;

// The connector writes the password into a 1C connection string, quoted with its quotes doubled, and 1C may quote that
// string in a failure, so the worker withholds a failure text that holds the password in that form too. An empty
// password withholds nothing, and the text of the failure still reaches the diagnostics (the brief of S9, "The lines";
// principle 9).
[Trait("Requires", "Windows")]
public sealed class PasswordFormTests
{
    private const string QuotedPassword = "Pw\"4f9\"Kq2";

    // The failure text holds the password only with its quotes doubled, not as it is.
    [Fact]
    public void A_password_with_quotes_appears_in_no_output_also_with_its_quotes_doubled()
    {
        string doubled = QuotedPassword.Replace("\"", "\"\"", StringComparison.Ordinal);
        FakeConnector connector = new(new FakeSession())
        {
            Failure = new InvalidOperationException($"The fake refused Pwd=\"{doubled}\"."),
        };

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            connector,
            LineText.Connection().With("password", LineText.Quote(QuotedPassword)).ToLine());

        Assert.Equal(WorkerExitCodes.NoSession, run.ExitCode);
        Assert.DoesNotContain("4f9", run.Diagnostics);
        Assert.DoesNotContain("The fake refused", run.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(run.Diagnostics));
    }

    [Fact]
    public void An_empty_password_leaves_the_text_of_a_failure_as_it_is()
    {
        FakeConnector connector = new(new FakeSession())
        {
            Failure = new InvalidOperationException("The fake refused the account."),
        };

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            connector,
            LineText.Connection().With("password", "\"\"").ToLine());

        Assert.Equal(WorkerExitCodes.NoSession, run.ExitCode);
        Assert.Equal("{\"ready\":false}\n", run.Output.Text);
        Assert.Contains("The fake refused the account.", run.Diagnostics);
    }
}
