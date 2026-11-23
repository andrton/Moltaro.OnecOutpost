using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Secret;

// secret <BASE> <KIND> (the S10 brief, "secret"), after the steps it shares with pair: the password of the base's 1C
// account in the mode, or of its worker account once "Log on as a batch job" is granted to that local user, is written
// to secrets.dat, encrypted for the machine, with every other secret kept, by Data.Replace and then owned by
// Administrators (Q14). The password comes from a prompt that does not echo it when the console is interactive,
// otherwise from one line of the redirected input; an empty line is the password of a 1C account. The password never
// reaches the output.
public sealed class SecretTests
{
    private readonly TestMachine _machine;

    private readonly byte[] _agentKey;

    public SecretTests()
    {
        _machine = PairedMachine.Create(out _agentKey);
    }

    private static string SecretsPath => Path.Combine(TestMachine.DataFolder, "secrets.dat");

    // The batch right comes first, before the password is read.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("worker")]
    public async Task A_password_of_the_redirected_input_is_written_to_secrets_dat_with_every_other_secret_kept(string kind)
    {
        _machine.Input = new FakeInput(_machine.Data, "new-password\nanother line\n");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", kind);

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        List<string> operations = ["others \"\""];

        if (kind == "worker")
        {
            operations.Add($"look up {PairedMachine.WorkerAccount}");
            operations.Add($"grant {PairedMachine.WorkerSid} SeBatchLogonRight");
        }

        operations.AddRange(["read the input", "replace secrets.dat", $"own {SecretsPath}"]);
        Assert.Equal(operations, _machine.Operations);

        AgentSecrets secrets = PairedMachine.SecretsOf(_machine);
        Assert.Equal(_agentKey, secrets.PrivateKey);
        Assert.Equal(Replaced($"accounting {kind}", "new-password"), PairedMachine.PasswordsOf(secrets));
        Assert.Equal(-1, _machine.Data.Root.Bytes("secrets.dat").AsSpan().IndexOf("new-password"u8));

        Assert.DoesNotContain("new-password", result.Output);
        Assert.Contains(TestMachine.LinesOf(result), line =>
            line.StartsWith("Wrote the password", StringComparison.Ordinal)
            && line.EndsWith("; the service uses it from its next start.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("read", "outpost-read for the read mode")]
    [InlineData("write", "outpost-write for the write mode")]
    [InlineData("worker", "outpost-worker for the workers")]
    public async Task An_interactive_console_asks_for_the_password_with_a_prompt_that_does_not_echo_it(string kind, string asked)
    {
        _machine.Console = new TestConsole().Interactive().Width(int.MaxValue);
        _machine.Console.Input.PushTextWithEnter("typed-password");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", kind);

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        IReadOnlyList<string> lines = TestMachine.LinesOf(result);
        int prompt = IndexOf(lines, line =>
            line.StartsWith("Password of ", StringComparison.Ordinal)
            && line.Contains($"{asked} of the base accounting:", StringComparison.Ordinal));
        Assert.True(prompt >= 0, "The console asked for no password.");
        Assert.DoesNotContain("typed-password", result.Output);
        Assert.Equal(Replaced($"accounting {kind}", "typed-password"), PairedMachine.PasswordsOf(PairedMachine.SecretsOf(_machine)));

        if (kind == "worker")
        {
            int granted = IndexOf(lines, line =>
                line.StartsWith("Granted ", StringComparison.Ordinal)
                && line.EndsWith("\"Log on as a batch job\".", StringComparison.Ordinal));
            Assert.InRange(granted, 0, prompt - 1);
        }
    }

    // A real case of 1C: an account without a password.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task An_empty_line_is_the_password_of_a_1C_account(string kind)
    {
        _machine.Input = new FakeInput(_machine.Data, "\n");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", kind);

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal(Replaced($"accounting {kind}", ""), PairedMachine.PasswordsOf(PairedMachine.SecretsOf(_machine)));
    }

    // The earlier secrets with the password of one base and kind replaced.
    private static IReadOnlyList<string> Replaced(string baseAndKind, string password)
    {
        return [.. PairedMachine.Earlier.Select(entry => entry.StartsWith(baseAndKind + ":", StringComparison.Ordinal) ? $"{baseAndKind}: {password}" : entry)];
    }

    private static int IndexOf(IReadOnlyList<string> lines, Func<string, bool> match)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (match(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
