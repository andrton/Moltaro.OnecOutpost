using System.Text;
using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Commands;

// Each command line gets a fresh executor, whose Run receives the command as the worker read it, and whose text the
// worker writes to its output unchanged, with its LF, in one write, before it takes the next line (concept §3).
[Trait("Requires", "Windows")]
public sealed class CommandTests
{
    [Theory]
    [InlineData("Read", "read")]
    [InlineData("Write", "write")]
    public void A_command_gives_the_executor_the_mode_of_the_worker_its_script_and_its_outcome_limit(
        string mode,
        string modeOfTheExecutor)
    {
        const string Script = "Message(\"Привет, \"\"мир\"\"\\\t😀\");\nResult = 1;";
        FakeSession session = new();

        WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command(Script, outcomeLimit: 1048576).ToLine());

        FakeRun run = Assert.Single(Assert.Single(session.Executors).Runs);
        Assert.Equal(modeOfTheExecutor, run.Mode);
        Assert.Equal(Script, run.Script);
        Assert.Equal(1048576, run.OutcomeLimit);
    }

    // The worker passes the text of the parameters as it received it: its white space, its escapes and the notation of
    // its numbers stay as they were.
    [Fact]
    public void The_parameters_reach_the_executor_as_the_text_the_worker_received()
    {
        const string Parameters = """{ "b" : [1, 2.50, 1E2, {"c":null}], "a":"é\"\/", "d":"жёлтый 😀" }""";
        FakeSession session = new();

        WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command(parameters: Parameters).ToLine());

        FakeRun run = Assert.Single(Assert.Single(session.Executors).Runs);
        Assert.Equal(Parameters, run.ParametersJson);
    }

    [Theory]
    [InlineData("\"order-42\"", "order-42")]
    [InlineData("\"\\u0437\\u0430\\u043a\\u0430\\u0437\"", "заказ")]
    [InlineData("null", null)]
    public void The_caller_key_of_a_command_reaches_the_executor_and_a_null_one_as_null(
        string callerKey,
        string? callerKeyOfTheExecutor)
    {
        FakeSession session = new();

        WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command(callerKey: callerKey).ToLine());

        FakeRun run = Assert.Single(Assert.Single(session.Executors).Runs);
        Assert.Equal(callerKeyOfTheExecutor, run.CallerKey);
    }

    [Fact]
    public void Each_command_gets_a_fresh_executor_that_is_disposed_once_after_its_run_and_before_the_next_is_opened()
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal(["open 1", "run 1", "dispose 1", "open 2", "run 2", "dispose 2"], session.Events);
        Assert.All(session.Executors, executor => Assert.Equal(1, executor.DisposeCount));
    }

    // Non-ASCII text, a character outside the Basic Multilingual Plane, escapes that a JSON writer would write otherwise,
    // and white space that it would leave out: a worker that read and wrote the text again would change it.
    [Fact]
    public void An_outcome_is_written_as_the_text_the_executor_returned_byte_for_byte_with_its_LF()
    {
        string first = ExecutorLine.Of(
            """{"kind":"succeeded","code":null,"result":{"name":"Товар 😀","escaped":"ж\/é"}, "errorText":null,"messages":["Проведён"],"messagesTrimmed":false}""");
        string second = ExecutorLine.Of(
            """{ "kind" : "failed", "code" : "script-error", "result" : null, "errorText" : "Ошибка 𝄞", "messages" : [], "messagesTrimmed" : true }""");
        FakeSession session = new(() => first, () => second);

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Read,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"ready\":true}\n" + first + "\n" + second + "\n"), run.Output.Bytes);
    }

    [Fact]
    public void An_outcome_reaches_the_output_in_one_write_with_its_LF()
    {
        string text = ExecutorLine.Of(
            """{"kind":"succeeded","code":null,"result":"Склад","errorText":null,"messages":[],"messagesTrimmed":false}""");
        FakeSession session = new(() => text);

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine());

        byte[] line = Encoding.UTF8.GetBytes(text + "\n");
        Assert.Contains(run.Output.Writes, write => write.AsSpan().SequenceEqual(line));
    }

    // AgentInput sends a line only once the worker answered the line before it, as the agent does, and records a line
    // the worker took earlier.
    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void The_worker_takes_the_next_line_only_after_it_wrote_the_answer_to_the_line_before_it(string mode)
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal(4, run.Output.LineCount);
        Assert.False(run.Input.ReadAhead);
    }
}
