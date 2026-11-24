using System.ComponentModel;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Secret;

// How secret ends without writing a password (the S10 brief, "secret"), each failure at its step and nothing after it:
// secrets.dat absent ends it with NotPaired, one that does not decrypt with Failure; a base, a mode or a worker account
// that the policy does not name with NotInPolicy; a worker account that is not a local user with UnknownAccount, before
// any right is granted; a console whose output is redirected and whose input is the console with NotInteractive,
// reading nothing; an empty password of a worker account, which Windows refuses outside its console, and the end of the
// input before a line, with Usage. secrets.dat keeps what it held, and no password reaches the output.
public sealed class SecretRefusalTests
{
    private readonly TestMachine _machine = PairedMachine.Create(out _);

    public SecretRefusalTests()
    {
        _machine.Input = new FakeInput(_machine.Data, "new-password\n");
    }

    // secret reads secrets.dat through the agent's StartChecks, with the start's reason and its own code (the S10b brief,
    // "One wording, with a remedy that exists").
    [Fact]
    public async Task A_secrets_dat_that_is_absent_ends_secret_with_NotPaired()
    {
        _machine.Data.Root.Unseed("secrets.dat");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "read");

        Assert.Equal(ConsoleExitCodes.NotPaired, result.ExitCode);
        Assert.Contains(
            "NotPaired (13): secrets.dat is absent; to pair the agent anew, remove agent.json, if any, and run the console's pair",
            result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
    }

    // As a secrets.dat copied from another machine.
    [Fact]
    public async Task A_secrets_dat_that_does_not_decrypt_ends_secret_with_Failure()
    {
        _machine.Protection.Refuses = true;

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "read");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): ", result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
    }

    [Theory]
    [InlineData("archive", "read", "names no base archive")]
    [InlineData("warehouse", "write", "does not let it be written")]
    [InlineData("warehouse", "worker", "names no worker account for it")]
    public async Task A_base_mode_or_worker_account_that_the_policy_does_not_name_ends_secret_with_NotInPolicy(
        string baseKey,
        string kind,
        string reason)
    {
        CommandAppResult result = await _machine.RunAsync("secret", baseKey, kind);

        Assert.Equal(ConsoleExitCodes.NotInPolicy, result.ExitCode);
        Assert.Contains("NotInPolicy (14): ", result.Output);
        Assert.Contains(reason, result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
    }

    [Fact]
    public async Task A_worker_account_that_is_not_a_local_user_ends_secret_with_UnknownAccount_before_any_right_is_granted()
    {
        _machine.Rights.Users.Clear();

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "worker");

        Assert.Equal(ConsoleExitCodes.UnknownAccount, result.ExitCode);
        Assert.Contains("UnknownAccount (15): ", result.Output);
        Assert.Contains("outpost-worker of the base accounting is not a local user of this machine", result.Output);
        Assert.Equal(["others \"\"", $"look up {PairedMachine.WorkerAccount}"], _machine.Operations);
    }

    [Fact]
    public async Task A_batch_right_that_LSA_refuses_ends_secret_with_Failure_before_the_password_is_read()
    {
        _machine.Rights.GrantFailure = new Win32Exception(1332, "the fake LSA refused");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "worker");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): ", result.Output);
        Assert.Contains("the fake LSA refused (Windows error 1332)", result.Output);
        Assert.Equal(
            ["others \"\"", $"look up {PairedMachine.WorkerAccount}", $"grant {PairedMachine.WorkerSid} SeBatchLogonRight"],
            _machine.Operations);
    }

    // The input is the console, and the console of the test is not interactive, as one whose output is redirected.
    [Fact]
    public async Task A_console_that_cannot_prompt_and_has_no_redirected_input_ends_secret_with_NotInteractive_reading_nothing()
    {
        _machine.Input = null;

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "read");

        Assert.Equal(ConsoleExitCodes.NotInteractive, result.ExitCode);
        Assert.Contains(
            "NotInteractive (16): run secret in a terminal, or give the password through standard input",
            TestMachine.LinesOf(result));
        Assert.Equal(["others \"\""], _machine.Operations);
    }

    // Windows refuses a blank password outside its console (the coordinator, 23 November 2026).
    [Fact]
    public async Task An_empty_password_of_a_worker_account_ends_secret_with_Usage()
    {
        _machine.Input = new FakeInput(_machine.Data, "\n");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "worker");

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains("Usage (2): ", result.Output);
        Assert.DoesNotContain("replace secrets.dat", _machine.Operations);
        Assert.Equal(PairedMachine.Earlier, PairedMachine.PasswordsOf(PairedMachine.SecretsOf(_machine)));
    }

    [Fact]
    public async Task The_end_of_the_input_before_a_line_ends_secret_with_Usage()
    {
        _machine.Input = new FakeInput(_machine.Data, "");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "read");

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains("Usage (2): ", result.Output);
        Assert.Equal(["others \"\"", "read the input"], _machine.Operations);
        Assert.Equal(PairedMachine.Earlier, PairedMachine.PasswordsOf(PairedMachine.SecretsOf(_machine)));
    }

    [Fact]
    public async Task A_secrets_dat_that_cannot_be_written_ends_secret_with_Failure_and_keeps_what_it_held()
    {
        _machine.Data.Root.FailReplace("secrets.dat");

        CommandAppResult result = await _machine.RunAsync("secret", "accounting", "read");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): ", result.Output);
        Assert.DoesNotContain("new-password", result.Output);
        Assert.Equal(["others \"\"", "read the input"], _machine.Operations);
        Assert.Equal(PairedMachine.Earlier, PairedMachine.PasswordsOf(PairedMachine.SecretsOf(_machine)));
    }
}
