using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.ScriptContract;

// What a script sees and what it cannot do: CallerKey holds the caller key or Undefined; each command gets a fresh
// executor, which runs the script with Execute, so a script cannot declare procedures; and the 1C session of the worker
// lives on between commands (concept §3, "The two commands").
public sealed class ScriptTests
{
    [Fact]
    public async Task CallerKey_holds_the_caller_key_of_a_write()
    {
        string callerKey = $"caller-key-{Guid.NewGuid():N}";
        await using WorkerProcess writer = await LabWorkers.TestBaseWriteAsync();

        OutcomeLine write = await writer.RunAsync("Result = CallerKey;", callerKey: callerKey);

        write.AssertIs("succeeded", null);
        Assert.Equal(callerKey, write.Result.GetString());
    }

    [Fact]
    public async Task CallerKey_is_Undefined_for_a_command_without_one()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Result = CallerKey = Undefined;", callerKey: null);

        read.AssertIs("succeeded", null);
        Assert.True(read.Result.GetBoolean());
    }

    [Fact]
    public async Task A_script_that_declares_a_procedure_is_refused()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Procedure Inner()\nEndProcedure\nResult = 1;");

        read.AssertIs("failed", "script-error");
    }

    // The number of the session in the base is the same for two commands of one worker, and another worker has another.
    [Fact]
    public async Task The_session_of_a_worker_lives_on_between_its_commands()
    {
        const string Script = "Result = InfoBaseSessionNumber();";

        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();
        OutcomeLine first = await reader.RunAsync(Script);
        OutcomeLine second = await reader.RunAsync(Script);

        await using WorkerProcess otherReader = await LabWorkers.TestBaseReadAsync();
        OutcomeLine other = await otherReader.RunAsync(Script);

        first.AssertIs("succeeded", null);
        second.AssertIs("succeeded", null);
        other.AssertIs("succeeded", null);
        Assert.Equal(first.Result.GetInt64(), second.Result.GetInt64());
        Assert.NotEqual(first.Result.GetInt64(), other.Result.GetInt64());
    }
}
