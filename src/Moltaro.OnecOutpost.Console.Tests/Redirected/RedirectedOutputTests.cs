using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
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

    // The help of install, uninstall and check describes --stop-timeout and --clock-skew with [d.]hh:mm:ss, which
    // Spectre reads as markup, so the description escapes it.
    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    [InlineData("pair")]
    [InlineData("secret")]
    [InlineData("check")]
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
    [InlineData("check", "--clock-skew", "00:00:00")]
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

    // Plain text for a script keeps each line whole, though it is longer than the 80 columns that Spectre assumes
    // without a terminal, and is UTF-8 whatever the code page of the console, which would lose the § of this line.
    [Fact]
    public async Task A_line_is_written_whole_and_in_UTF_8()
    {
        ProgramEnd end = await BuiltProgram.RunAsync(ConsoleFile, "pair", "outpost.example.com", TestMachine.Token);

        Assert.Equal(ConsoleExitCodes.Usage, end.ExitCode);
        Assert.Equal(
            ["Usage (2): <SERVER>: expected an https address, or an http one on loopback (§11 of the protocol)"],
            end.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    // The variables by which GitHub Actions and Azure Pipelines name themselves, with which Spectre's default enrichers
    // would turn colour on whatever the output is: the output is not an interactive terminal, so it stays plain text.
    [Theory]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("TF_BUILD", "True")]
    public async Task Under_the_variables_of_a_CI_system_a_redirected_output_is_plain_text(string variable, string value)
    {
        ProgramEnd end = await BuiltProgram.RunWithVariablesAsync(
            ConsoleFile,
            new Dictionary<string, string> { [variable] = value },
            "secret",
            "Accounting",
            "read");

        Assert.Equal(ConsoleExitCodes.Usage, end.ExitCode);
        Assert.StartsWith("Usage (2): ", end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Escape, end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Escape, end.Error, StringComparison.Ordinal);
    }
}
