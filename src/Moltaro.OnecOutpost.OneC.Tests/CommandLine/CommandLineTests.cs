using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.CommandLine;

// The command line of the worker program is <base key> <read|write>; anything else ends it with exit code 1, before it
// reads its input and with nothing on its standard output. Only a process shows it, and the program
// has no test entry point, so the test lives here although it needs no 1C.
public sealed class CommandLineTests
{
    public static TheoryData<string[]> CommandLinesOtherThanABaseKeyAndAMode => new()
    {
        Array.Empty<string>(),
        new[] { "test" },
        new[] { "test", "delete" },
        new[] { "test", "read", "write" },
    };

    [Theory]
    [MemberData(nameof(CommandLinesOtherThanABaseKeyAndAMode))]
    public async Task A_command_line_other_than_a_base_key_and_a_mode_ends_the_worker_with_exit_code_1(string[] arguments)
    {
        await using WorkerProcess worker = WorkerProcess.Start(arguments);

        Assert.Null(await worker.ReadLineAsync());
        Assert.Equal(1, await worker.WaitForExitAsync());
    }
}
