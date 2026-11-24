using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.OnecOutpost.TestSupport.Programs;
using Moltaro.OnecOutpost.TestSupport.RunningAgent;

namespace Moltaro.OnecOutpost.Shell.Tests.Sessions;

[Trait("Requires", "PostgreSQL")]
public sealed class ShellSessionTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string Read = "read accounting\nResult = 1;\n.\n";
    private const string Write = "write accounting an-order\nResult = 1;\n.\n";

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_write_passes_lf_joined_lines_and_a_literal_caller_key_to_the_worker_and_repeats_the_retained_outcome()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        LoopbackAgent agent = await PairLoopbackAgentAsync(shell);
        await using HostRun run = agent.Start(TimeProvider.System);
        await agent.WaitForConnectionAsync();

        const string Script = "Result = Parameters;\n.parameters\n.\n.field";
        const string Frame = """
            write accounting --help
            Result = Parameters;
            ..parameters
            ..
            .field
            .parameters
            {
              "city": "Łódź",
              "count": 3
            }
            .
            """;

        await shell.SendAsync("\uFEFF\r\n" + Frame.ReplaceLineEndings("\r\n") + "\r\n" + Frame);
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(0, end.ExitCode);
        Assert.Equal(2, Regex.Matches(end.Output, @"\bSucceeded\b", RegexOptions.IgnoreCase).Count);
        Guid command = Assert.Single(CommandIds(end.Output, agent.Id).Distinct());
        Assert.Equal(2, CommandIds(end.Output, agent.Id).Count(id => id == command));
        Assert.Matches(@"\r?\n +""city"":", end.Output);
        Assert.DoesNotContain("\u001b[", end.Output, StringComparison.Ordinal);

        (string script, string parameters, string? callerKey) = Assert.Single(agent.Commands(OutpostModeEnum.Write));
        Assert.Equal(Script, script);
        Assert.Equal("--help", callerKey);
        using JsonDocument json = JsonDocument.Parse(parameters);
        Assert.Equal("Łódź", json.RootElement.GetProperty("city").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("count").GetInt32());
        Assert.Empty(agent.Commands(OutpostModeEnum.Read));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_invalid_frame_is_consumed_through_its_terminator_and_a_later_command_still_runs()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        LoopbackAgent agent = await PairLoopbackAgentAsync(shell);
        await using HostRun run = agent.Start(TimeProvider.System);
        await agent.WaitForConnectionAsync();

        await shell.SendAsync("""
            write accounting
            token This is a script line
            .
            read accounting extra
            token This is also a script line
            .
            read accounting
            Result = 1;
            .parameters
            ["not an object"]
            .
            read accounting
            Result = 1;
            .parameters
            {"incomplete":
            .
            read accounting
            .
            token Still accepting commands
            """);
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(2, end.ExitCode);
        Assert.Contains("Usage", end.Output + end.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Regex.Matches(end.Output, @"\boutpost1\.").Count);
        Assert.DoesNotContain("Succeeded", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(agent.Commands(OutpostModeEnum.Read));
        Assert.Empty(agent.Commands(OutpostModeEnum.Write));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Eof_inside_a_read_frame_is_a_usage_error_and_dispatches_nothing()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        _ = await shell.AddressAsync();
        await shell.SendAsync("read accounting\nResult = 1;");

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(2, end.ExitCode);
        Assert.Contains("Usage", end.Output + end.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unknown-base", end.Output + end.Error, StringComparison.Ordinal);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Eof_returns_the_first_nonzero_status_after_later_errors_and_successes()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        _ = await shell.AddressAsync();
        await shell.SendAsync(Read + "not-a-command\ntoken This succeeds\n");

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(11, end.ExitCode);
        Assert.Contains("Refused", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown-base", end.Output, StringComparison.Ordinal);
        Assert.Contains("Usage", end.Output + end.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outpost1.", end.Output, StringComparison.Ordinal);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("c328")]
    [InlineData("fffe")]
    public async Task Malformed_utf8_ends_the_session_with_usage_even_after_an_earlier_refusal(string hexadecimal)
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        _ = await shell.AddressAsync();
        await shell.SendAsync(Read);
        await shell.ReadThroughAsync(line => line.Contains("unknown-base", StringComparison.Ordinal));
        await shell.SendAsync(Convert.FromHexString(hexadecimal));

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(2, end.ExitCode);
        Assert.Contains("UTF-8", end.Output + end.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_utf16_bom_at_the_start_of_input_is_an_encoding_error()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        _ = await shell.AddressAsync();
        byte[] wrongEncoding = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("token Wrong encoding\n")];
        await shell.SendAsync(wrongEncoding);

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(2, end.ExitCode);
        Assert.DoesNotContain("outpost1.", end.Output, StringComparison.Ordinal);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Session_help_names_the_commands_and_accepts_a_command_at_the_start_of_a_line()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        _ = await shell.AddressAsync();
        await shell.SendAsync("--help\n--help write\n");

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(0, end.ExitCode);
        Assert.Contains("token", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("write", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("caller", end.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Interrupting_a_shell_waiting_for_input_returns_the_cancellation_status()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "This test sends the terminal's interrupt signal on Unix.");
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        _ = await shell.AddressAsync();
        ProcessStartInfo signal = new("kill") { UseShellExecute = false };
        signal.ArgumentList.Add("-s");
        signal.ArgumentList.Add("INT");
        signal.ArgumentList.Add(shell.ProcessId.ToString(CultureInfo.InvariantCulture));
        using Process command = Process.Start(signal)!;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await command.WaitForExitAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        Assert.Equal(0, command.ExitCode);

        ProgramEnd end = await shell.EndAsync(closeInput: false);

        Assert.Equal(5, end.ExitCode);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_read_shows_the_json_null_result_and_its_messages_literally_in_order()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        LoopbackAgent agent = await PairLoopbackAgentAsync(shell);
        string first = "[red]Łódź[/] " + new string('x', 100);
        const string Second = "Second message";
        agent.ReplyWith(JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "succeeded",
            code = (string?)null,
            result = (object?)null,
            errorText = (string?)null,
            messages = new[] { first, Second },
            messagesTrimmed = true,
        }));
        await using HostRun run = agent.Start(TimeProvider.System);
        await agent.WaitForConnectionAsync();

        await shell.SendAsync(Read);
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(0, end.ExitCode);
        Assert.Contains("Succeeded", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Single(CommandIds(end.Output, agent.Id));
        Assert.Matches(@"\bnull\b", end.Output);
        Assert.Contains(first, end.Output, StringComparison.Ordinal);
        Assert.Contains(Second, end.Output, StringComparison.Ordinal);
        Assert.True(end.Output.IndexOf(first, StringComparison.Ordinal) < end.Output.IndexOf(Second, StringComparison.Ordinal));
        Assert.Matches(@"(?i)trimm|truncat", end.Output);
        Assert.DoesNotContain("\u001b[", end.Output + end.Error, StringComparison.Ordinal);
        (string script, string parameters, string? callerKey) = Assert.Single(agent.Commands(OutpostModeEnum.Read));
        Assert.Equal("Result = 1;", script);
        Assert.Equal("{}", parameters);
        Assert.Null(callerKey);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("Failed", "script-error", 10)]
    [InlineData("Unknown", "transaction-ended", 14)]
    public async Task A_worker_error_keeps_its_kind_code_and_command_id_and_sets_the_exit_status(
        string kind,
        string code,
        int status)
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        LoopbackAgent agent = await PairLoopbackAgentAsync(shell);
        agent.ReplyWith(kind == "Failed"
            ? """{"kind":"failed","code":"script-error","result":null,"errorText":"[red]Division by zero[/]","messages":[],"messagesTrimmed":false}"""u8.ToArray()
            : TestOutcomes.TransactionEnded());
        await using HostRun run = agent.Start(TimeProvider.System);
        await agent.WaitForConnectionAsync();

        await shell.SendAsync(Write);
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(status, end.ExitCode);
        Assert.Contains(kind, end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(code, end.Output, StringComparison.Ordinal);
        if (kind == "Failed")
        {
            Assert.Contains("[red]Division by zero[/]", end.Output, StringComparison.Ordinal);
        }

        Assert.Single(CommandIds(end.Output, agent.Id));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_read_to_the_base_of_a_disconnected_agent_is_unavailable()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        Uri address = await shell.AddressAsync();
        await shell.SendAsync("token Offline test agent\n");
        using HttpClient client = Client(address);
        FakeClock clock = new(TimeProvider.System.GetUtcNow() - TimeSpan.FromHours(1));
        using TestAgent agent = Agent(client, await shell.TokenAsync(), clock);
        _ = await TestTimes.EndOf(agent.PairAsync(TestContext.Current.CancellationToken));
        OutpostPollAnswer poll = await PollAsync(agent, PollRequests.ReadOnly(TestCalls.BaseKey));
        Assert.Equal([TestCalls.BaseKey], poll.Held);

        await shell.SendAsync(Read);
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(12, end.ExitCode);
        Assert.Contains("Unavailable", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(CommandIds(end.Output, agent.Id));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_command_that_the_next_signed_poll_shows_could_not_have_run_is_reported_as_expired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        Uri address = await shell.AddressAsync();
        await shell.SendAsync("token Expiry test agent\n");
        using HttpClient client = Client(address);
        using TestAgent agent = Agent(client, await shell.TokenAsync(), TimeProvider.System);
        OutpostPairAnswer pairing = await TestTimes.EndOf(agent.PairAsync(cancellationToken));
        OutpostPollBase @base = PollRequests.ReadOnly(TestCalls.BaseKey);
        _ = await PollAsync(agent, @base);

        OutpostPollRequest taking = agent.NextPoll(pairing.PollTimeout, [@base]);
        Task<AgentAnswer> answer = agent.PollAsync(taking, cancellationToken);
        await shell.SendAsync(Read);
        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(agent.ReadPollAnswer(taking, await TestTimes.EndOf(answer))));

        OutpostPollRequest later = agent.NextPoll(OutpostProtocol.MinPollTimeout, [@base]) with
        {
            Time = command.Deadline + command.Skew + TestTimes.Millisecond,
        };
        _ = await TestTimes.EndOf(agent.PollAsync(later, cancellationToken));
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(13, end.ExitCode);
        Assert.Contains("Expired", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(command.Id, Assert.Single(CommandIds(end.Output, agent.Id)));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Administration_lists_pending_and_paired_agents_and_shows_both_revocation_and_all_release_results()
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = await StartAsync(keys);
        Uri address = await shell.AddressAsync();
        const string Name = "Test  Łódź [sales]";
        await shell.SendAsync($"token {Name}\nagents\n");
        using HttpClient client = Client(address);
        using TestAgent agent = Agent(client, await shell.TokenAsync(), TimeProvider.System);
        string pending = await shell.ReadThroughAsync(line => line.Contains("Pending", StringComparison.OrdinalIgnoreCase)
            && line.Contains(Name, StringComparison.Ordinal));
        Assert.Contains(Name, pending, StringComparison.Ordinal);
        Assert.Contains(agent.Id.ToString("D"), pending, StringComparison.OrdinalIgnoreCase);

        _ = await TestTimes.EndOf(agent.PairAsync(TestContext.Current.CancellationToken));
        _ = await PollAsync(agent, PollRequests.ReadAndWrite(TestCalls.BaseKey), PollRequests.ReadOnly("warehouse"));
        await shell.SendAsync("agents\nrelease accounting\n");
        string paired = await shell.ReadThroughAsync(line => line.Contains("StillDeclared", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Paired", paired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connected", paired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("readOpen", paired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("writeOpen", paired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("held", paired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warehouse", paired, StringComparison.Ordinal);

        _ = await PollAsync(agent, PollRequests.ReadOnly("warehouse"));
        await shell.SendAsync($"release accounting\nrelease accounting\nrevoke {agent.Id:D}\nrevoke {agent.Id:D}\nagents");
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(0, end.ExitCode);
        Assert.Contains("expir", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Released", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotHeld", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"(?i)\bRevoked\b", end.Output);
        Assert.Contains("NotRevoked", end.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u001b[", end.Output + end.Error, StringComparison.Ordinal);
    }

    private async Task<ShellSession> StartAsync(TemporaryFolder keys)
    {
        string connection = await ShellDatabase.CreateAsync(postgres, TestContext.Current.CancellationToken);
        return ShellSession.Start(connection, "--key-folder", keys.Path, "--listen", ShellSession.LoopbackOrigin);
    }

    private static async Task<LoopbackAgent> PairLoopbackAgentAsync(ShellSession shell)
    {
        Uri address = await shell.AddressAsync();
        Assert.Equal("http", address.Scheme);
        Assert.Equal("127.0.0.1", address.Host);
        Assert.NotEqual(0, address.Port);

        await shell.SendAsync("token Test agent\n");
        return await LoopbackAgent.PairAsync(address, await shell.TokenAsync(), TestCalls.BaseKey, TestContext.Current.CancellationToken);
    }

    private static HttpClient Client(Uri address)
    {
        return new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new Uri(address.AbsoluteUri.TrimEnd('/') + "/"),
        };
    }

    private static TestAgent Agent(HttpClient client, string credential, TimeProvider clock)
    {
        Assert.True(OutpostPairingTokenText.TryRead(credential, out OutpostPairingTokenText? token));
        return new TestAgent(client, clock, new OutpostPairingToken(token.Agent, credential, DateTimeOffset.MaxValue));
    }

    private static async Task<OutpostPollAnswer> PollAsync(TestAgent agent, params OutpostPollBase[] bases)
    {
        OutpostPollRequest poll = agent.NextPoll(OutpostProtocol.MinPollTimeout, bases);
        AgentAnswer answer = await TestTimes.EndOf(agent.PollAsync(poll, TestContext.Current.CancellationToken));

        return agent.ReadPollAnswer(poll, answer);
    }

    private static IReadOnlyList<Guid> CommandIds(string output, Guid agent)
    {
        const string GuidPattern = @"\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b";
        return [.. Regex.Matches(output, GuidPattern, RegexOptions.IgnoreCase)
            .Select(match => Guid.Parse(match.Value))
            .Where(id => id != agent)];
    }
}
