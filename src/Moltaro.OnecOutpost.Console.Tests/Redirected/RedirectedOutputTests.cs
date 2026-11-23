using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Programs;

namespace Moltaro.OnecOutpost.Console.Tests.Redirected;

// The console with its output redirected, as a script runs it (the S10 brief, "Tests with Requires=Windows", and
// AGENTS.md, "Console programs"): the built console as a child process with redirected streams, as the agent's tests run
// the agent. Spectre's own detection then writes plain text, without colour or animation, so neither stream holds an
// escape sequence. Each command is run with --help, which ends with Success, and with one argument that it refuses,
// which ends with Usage; neither reaches a step, so the data folder of the machine, %ProgramData%\Moltaro.OnecOutpost,
// which the test only reads, is not changed.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class RedirectedOutputTests
{
    private const string ConsoleFile = "Moltaro.OnecOutpost.Console.exe";

    private const string Escape = "\u001b[";

    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Moltaro.OnecOutpost");

    // The help of install and uninstall describes --stop-timeout with [d.]hh:mm:ss, which Spectre reads as markup and
    // refuses; their cases wait for the description to be escaped.
    [Theory]
    [InlineData("install", Skip = SkipReason.NotImplemented)]
    [InlineData("uninstall", Skip = SkipReason.NotImplemented)]
    [InlineData("pair")]
    [InlineData("secret")]
    [InlineData("connector", "register")]
    public async Task The_help_of_each_command_is_plain_text_that_ends_with_Success_and_changes_nothing(params string[] command)
    {
        string before = BuiltProgram.Describe(DataFolder);

        ProgramEnd end = await BuiltProgram.RunAsync(ConsoleFile, [.. command, "--help"]);

        Assert.Equal(ConsoleExitCodes.Success, end.ExitCode);
        Assert.Contains("USAGE:", end.Output);
        Assert.DoesNotContain(Escape, end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Escape, end.Error, StringComparison.Ordinal);
        Assert.Equal(before, BuiltProgram.Describe(DataFolder));
    }

    // The token of pair is valid, so that its server is the argument refused, and it never reaches the output.
    [Theory]
    [InlineData("install", "--stop-timeout", "00:00:00")]
    [InlineData("uninstall", "--stop-timeout", "01:00:01")]
    [InlineData("pair", "outpost.example.com", "{token}")]
    [InlineData("secret", "Accounting", "read")]
    [InlineData("connector", "register", "now")]
    public async Task A_refused_argument_of_each_command_is_plain_text_that_ends_with_Usage_and_changes_nothing(params string[] arguments)
    {
        string before = BuiltProgram.Describe(DataFolder);

        ProgramEnd end = await BuiltProgram.RunAsync(
            ConsoleFile,
            [.. arguments.Select(argument => argument == "{token}" ? TestMachine.Token : argument)]);

        Assert.Equal(ConsoleExitCodes.Usage, end.ExitCode);
        Assert.Contains("Usage (2): ", end.Output);
        Assert.DoesNotContain(TestMachine.Token, end.Output);
        Assert.DoesNotContain(Escape, end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Escape, end.Error, StringComparison.Ordinal);
        Assert.Equal(before, BuiltProgram.Describe(DataFolder));
    }
}
