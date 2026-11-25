using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Secrets;

// The password stays on its machine (principle 9): it appears in no output of the worker. A text that is not the
// worker's own, such as the failure message of 1C or of .NET, reaches the diagnostics only when it holds the password
// neither as it is nor with its quotes doubled; otherwise the worker writes a sentence of its own that the text was
// withheld. Replacing the password inside a text would give a short one away, so nothing is replaced, in such a text
// or in a sentence of the worker.
[Trait("Requires", "Windows")]
public sealed class PasswordTests
{
    // Every failure text of the fake begins with it. It holds none of the passwords of these tests, so a worker that
    // replaced the password would leave it whole, and any part of a failure text that reached an output is found.
    private const string Mark = "FAKE REFUSAL";

    private const string TextWithTheLongPassword = $"{Mark}: the password {LineText.Password} of the account.";

    // Where the failure occurs: the session, the executor or the call of Run.
    public static TheoryData<string> PlacesOfAFailure => [.. Places];

    // A place, a password, and a failure text that holds it: a long one as it is, a short one inside another word, 1 in
    // 1541 and e in nearly every word, and one with quotes that the text holds only with its quotes doubled, as a 1C
    // connection string writes it.
    public static TheoryData<string, string, string> FailuresThatQuoteThePassword
    {
        get
        {
            (string Password, string Text)[] failures =
            [
                (LineText.Password, TextWithTheLongPassword),
                ("1", $"{Mark}: no answer from srv:1541."),
                ("e", $"{Mark}: the account was refused."),
                ("Pw\"4f9\"Kq2", $"{Mark}: Pwd=\"Pw\"\"4f9\"\"Kq2\";"),
            ];

            TheoryData<string, string, string> data = [];

            foreach (string place in Places)
            {
                foreach ((string password, string text) in failures)
                {
                    data.Add(place, password, text);
                }
            }

            return data;
        }
    }

    private static string[] Places => ["connect", "open executor", "run"];

    [Theory]
    [MemberData(nameof(PlacesOfAFailure))]
    public void The_password_appears_in_no_output_when_the_text_of_a_failure_quotes_it(string place)
    {
        WorkerRun run = RunWithAFailure(place, LineText.Password, TextWithTheLongPassword);

        Assert.DoesNotContain(LineText.Password, run.Output.Text);
        Assert.DoesNotContain(LineText.Password, run.Diagnostics);
    }

    // The worker's own sentences hold no password, so its diagnostics are the same whichever password its failure text
    // quoted: had it replaced a short password in them, they would differ from those of the long one.
    [Theory]
    [MemberData(nameof(FailuresThatQuoteThePassword))]
    public void A_failure_text_that_quotes_the_password_is_withheld_and_the_sentences_of_the_worker_are_not_altered(
        string place,
        string password,
        string text)
    {
        WorkerRun run = RunWithAFailure(place, password, text);
        WorkerRun withTheLongPassword = RunWithAFailure(place, LineText.Password, TextWithTheLongPassword);

        Assert.DoesNotContain(Mark, run.Diagnostics);
        Assert.DoesNotContain(Mark, run.Output.Text);
        Assert.False(string.IsNullOrWhiteSpace(run.Diagnostics));
        Assert.Equal(withTheLongPassword.Diagnostics, run.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(PlacesOfAFailure))]
    public void A_failure_text_without_the_password_reaches_the_diagnostics(string place)
    {
        const string Text = $"{Mark}: the account is locked.";

        WorkerRun run = RunWithAFailure(place, LineText.Password, Text);

        Assert.Contains(Text, run.Diagnostics);
    }

    // A Write worker, so that a Run that throws gives Unknown, and a command after the failure that the worker never
    // reaches.
    private static WorkerRun RunWithAFailure(string place, string password, string text)
    {
        InvalidOperationException failure = new(text);

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
            LineText.Connection().With("password", LineText.Quote(password)).ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());
    }
}
