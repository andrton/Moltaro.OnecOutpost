using System.Runtime.Versioning;
using System.Text;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The lines of S9 as ComWorkerProcess reads them (the S9b brief, "ComWorkerProcess"). The ready line is one line of at
// most 1 KiB, true only for an object whose one member is "ready":true. An outcome line is one line of at most
// OutcomeLimit plus 64 bytes: an object with exactly size, an integer from 0, then outcome, any JSON value, which gives
// size and the exact bytes of outcome, whatever their length. Any other line, and the end of the output before a whole
// line, kills the process and gives false or null. The test program writes the line that its location or its script
// names and then waits for its input, so that only a kill ends it; the test holds its process and sees it end.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerLineTests
{
    // Ready lines that are not the one object whose one member is "ready":true.
    public static TheoryData<string> ReadyLinesRefused =>
    [
        """{"ready":false}""",
        """{"ready":true,"ready":true}""",
        """{"ready":true,"session":1}""",
        """{"ready":"true"}""",
        """{"ready":1}""",
        """{}""",
        """[true]""",
        """ready""",
    ];

    // Outcome lines that are not an object with exactly size, then outcome, or whose size is not an integer from 0.
    public static TheoryData<string> OutcomeLinesRefused =>
    [
        """{"outcome":{},"size":2}""",
        """{"size":2,"outcome":{},"more":1}""",
        """{"size":2,"size":2,"outcome":{}}""",
        """{"size":2}""",
        """{"outcome":{}}""",
        """[2,{}]""",
        """size 2""",
        """{"size":2.5,"outcome":{}}""",
        """{"size":2.0,"outcome":{}}""",
        """{"size":2e0,"outcome":{}}""",
        """{"size":-2,"outcome":{}}""",
        """{"size":"2","outcome":{}}""",
        """{"size":null,"outcome":{}}""",
    ];

    [Theory]
    [MemberData(nameof(ReadyLinesRefused))]
    public async Task A_ready_line_other_than_ready_true_gives_false_and_the_process_ends(string line)
    {
        using StartedWorker started = StartedWorker.Start();

        Assert.False(await OpenAsync(started, "line " + line));
        await started.EndedAsync();
    }

    // {"ready":true} with white space up to 1 KiB and one byte more, its LF not counted.
    [Fact]
    public async Task A_ready_line_over_1_KiB_gives_false_and_the_process_ends()
    {
        string line = """{"ready":true}""".PadRight(1025);
        using StartedWorker started = StartedWorker.Start();

        Assert.False(await OpenAsync(started, "line " + line));
        await started.EndedAsync();
    }

    // The program refuses a command line of one argument and ends at once, before any line.
    [Fact]
    public async Task The_end_of_the_output_before_a_ready_line_gives_false()
    {
        using StartedWorker started = StartedWorker.Start("accounting");

        Assert.False(await OpenAsync(started, TestLines.Connection().Location));
        await started.EndedAsync();
    }

    [Theory]
    [MemberData(nameof(OutcomeLinesRefused))]
    public async Task An_outcome_line_that_is_not_size_then_outcome_or_whose_size_is_not_an_integer_from_0_gives_null_and_the_process_ends(
        string line)
    {
        using StartedWorker started = await StartedWorker.OpenAsync();

        Assert.Null(await RunAsync(started, "line " + line));
        await started.EndedAsync();
    }

    // The limit is OutcomeLimit plus 64 bytes; the line is one byte longer, its LF not counted, and would be read
    // otherwise.
    [Fact]
    public async Task An_outcome_line_over_OutcomeLimit_plus_64_bytes_gives_null_and_the_process_ends()
    {
        string line = OutcomeLineOfLength((int)TestLines.OutcomeLimit + 64 + 1);
        using StartedWorker started = await StartedWorker.OpenAsync();

        Assert.Null(await RunAsync(started, "line " + line));
        await started.EndedAsync();
    }

    // The size is given as the line has it, a size of 0 too: the pool compares it with the length of the bytes.
    [Theory]
    [InlineData("""{"size":3,"outcome": { "kind" : "x" } }""", 3, """{ "kind" : "x" }""")]
    [InlineData("""{"size":0,"outcome":null}""", 0, "null")]
    public async Task An_outcome_line_of_size_then_outcome_gives_the_size_and_the_exact_bytes_of_the_outcome(
        string line,
        long size,
        string outcome)
    {
        using StartedWorker started = await StartedWorker.OpenAsync();

        (long Size, byte[] Outcome)? given = await RunAsync(started, "line " + line);

        Assert.NotNull(given);
        Assert.Equal(size, given.Value.Size);
        Assert.Equal(Encoding.UTF8.GetBytes(outcome), given.Value.Outcome);
        Assert.False(started.Process.HasExited);
    }

    // The largest outcome the executor may give, OutcomeLimit bytes, fits its line within the limit.
    [Fact]
    public async Task An_outcome_of_OutcomeLimit_bytes_is_read_whole()
    {
        string outcome = "\"" + new string('x', (int)TestLines.OutcomeLimit - 2) + "\"";
        using StartedWorker started = await StartedWorker.OpenAsync();

        (long Size, byte[] Outcome)? given = await RunAsync(started, $$"""line {"size":{{outcome.Length}},"outcome":{{outcome}}}""");

        Assert.NotNull(given);
        Assert.Equal(TestLines.OutcomeLimit, given.Value.Size);
        Assert.Equal(Encoding.UTF8.GetBytes(outcome), given.Value.Outcome);
    }

    private static Task<bool> OpenAsync(StartedWorker started, string location)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return started.Worker.OpenAsync(TestLines.Connection(location), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    private static Task<(long Size, byte[] Outcome)?> RunAsync(StartedWorker started, string script)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return started.Worker.RunAsync(TestLines.Command(script), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    // An outcome line of exactly this many bytes, its LF not counted, whose outcome is a string and whose size is the
    // length of that string as the line writes it.
    private static string OutcomeLineOfLength(int length)
    {
        for (int fill = length; fill >= 0; fill--)
        {
            string outcome = "\"" + new string('x', fill) + "\"";
            string line = $$"""{"size":{{outcome.Length}},"outcome":{{outcome}}}""";

            if (line.Length == length)
            {
                return line;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(length), length, "No outcome line is that short.");
    }
}
