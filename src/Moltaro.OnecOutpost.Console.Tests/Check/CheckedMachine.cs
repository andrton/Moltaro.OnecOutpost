using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// A test's machine as install, pair and secret left it, on which every line of check passes (the S10b brief, "Tests on
// every platform"): the data folder with the policy of TestMachine, agent.json pinning the key of the stub of the
// outcome endpoint, and secrets.dat with the agent's key and every password the policy needs; the COM connector of one
// version registered for 64 bits; the agent's account holding its two privileges; the worker account of accounting, a
// local user that logs on for a batch job, with an ordinary token and a probe that ends with 0; and a session for each
// account of each base, opened under the account's own name. The stub is the fake server, signing with a test key of its
// own and reading the agent's requests with the agent's key, each request a line "request {name}" of the operation
// list. A run of check answers the one outcome request as the test gives, or not at all for a run that ends before its
// server line, and fails when any password of secrets.dat reaches its output.
internal sealed class CheckedMachine : IDisposable
{
    public const string ConnectorVersion = "8.3.27.1859";

    public const string WorkerAccount = "outpost-worker";

    // The passwords of secrets.dat, none of which may reach an output.
    public const string ReadPassword = "read-S3cret";

    public const string WritePassword = "write-S3cret";

    public const string WarehousePassword = "warehouse-S3cret";

    public const string WorkerPassword = "worker-S3cret";

    public static readonly Uri Server = new("https://outpost.example.com/agents/");

    public static readonly Guid Agent = Guid.Parse("7a1c2e3f-4b5d-4e6f-8a90-b1c2d3e4f5a6");

    private static readonly string[] Passwords = [ReadPassword, WritePassword, WarehousePassword, WorkerPassword];

    private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public CheckedMachine()
    {
        Machine.Installed();
        Machine.Data.Root.Seed("agent.json", AgentPairingFile.Write(new AgentPairingSettings(
            Server,
            Agent,
            OutpostKeys.ExportPublicKey(_serverKey),
            FakeServer.PollTimeout,
            FakeServer.LeaseGrace)));
        Machine.Data.Root.Seed("secrets.dat", SecretsFile.Write(Secrets(), Machine.Protection));
        Machine.Connectors.Registered(ConnectorBitnessEnum.X64, ConnectorVersion);
        Machine.Rights.Rights[TestMachine.AgentSid] = AccountRightNames.AgentPrivileges;
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("outpost-read", "Outpost read");
        Machine.Workers.Sessions[("accounting", "outpost-write")] = new FakeSession("outpost-write", "Outpost write");
        Machine.Workers.Sessions[("warehouse", "outpost-read")] = new FakeSession("outpost-read", "Outpost read");

        Stub = new FakeServer(_serverKey, Machine.Clock, Machine.Data) { AgentKey = AgentKey };
        Machine.Server = Stub;
    }

    public TestMachine Machine { get; } = new();

    // The agent's key, which secrets.dat holds and with which check signs its outcome.
    public ECDsa AgentKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FakeServer Stub { get; }

    // The worker program and the executor of the program folder for the connector of 64 bits, with which every worker
    // starts.
    public static string Program => ProgramFolderLayout.WorkerOf(TestMachine.ProgramFolder, ConnectorBitnessEnum.X64);

    public static string Executor => ProgramFolderLayout.ExecutorOf(TestMachine.ProgramFolder);

    // The folder of the registered connector, whose access lists check judges.
    public static string ConnectorFolder =>
        Path.GetDirectoryName(FakeConnectors.PathOf(ConnectorBitnessEnum.X64, ConnectorVersion))!;

    // The secrets of the machine: the agent's key, each password the policy needs, and the worker account's.
    public AgentSecrets Secrets()
    {
        return new AgentSecrets(
            AgentKey.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [("accounting", OutpostModeEnum.Read)] = ReadPassword,
                [("accounting", OutpostModeEnum.Write)] = WritePassword,
                [("warehouse", OutpostModeEnum.Read)] = WarehousePassword,
            },
            new Dictionary<string, string> { ["accounting"] = WorkerPassword });
    }

    // Runs check with the options to its end, the stub confirming its outcome at once.
    public Task<CheckRun> RunAsync(params string[] options)
    {
        return RunAsync(exchange => exchange.Confirm(), options);
    }

    // Runs check with the options to its end, the stub answering its outcome request as the test gives.
    public Task<CheckRun> RunAsync(Action<ServerExchange> answer, params string[] options)
    {
        return RunAsync(
            exchange =>
            {
                answer(exchange);

                return Task.CompletedTask;
            },
            options);
    }

    // The same with an answer that waits, such as one that moves the clock of the machine before it answers.
    public async Task<CheckRun> RunAsync(Func<ServerExchange, Task> answer, params string[] options)
    {
        Task<CommandAppResult> run = Machine.Start(TestContext.Current.CancellationToken, ["check", .. options]);

        await answer(await Stub.NextOutcomeAsync());

        return Ended(await TestTimes.EndOf(run));
    }

    // Runs check with the options to its end, the stub confirming its outcome at once if one comes, for a test that
    // leaves open whether its run reaches the server line.
    public async Task<CheckRun> RunConfirmingAnyAsync(params string[] options)
    {
        Task<CommandAppResult> run = Machine.Start(TestContext.Current.CancellationToken, ["check", .. options]);
        Task<ServerExchange> request = Stub.NextOutcomeAsync();

        if (await Task.WhenAny(run, request) == request)
        {
            (await request).Confirm();
        }

        return Ended(await TestTimes.EndOf(run));
    }

    // Runs check with the options to its end, which comes before any request to the server.
    public async Task<CheckRun> RunUnansweredAsync(params string[] options)
    {
        CommandAppResult result = await Machine.RunAsync(["check", .. options]);

        Assert.Equal(0, Stub.Count(OutpostRequestKindEnum.Outcome));

        return Ended(result);
    }

    // Starts check with the options, for a test that acts while it runs, such as one that moves the clock past a run
    // limit; the test answers the stub itself and ends the run by Ended.
    public Task<CommandAppResult> Start(params string[] options)
    {
        return Machine.Start(TestContext.Current.CancellationToken, ["check", .. options]);
    }

    // The run of check that ended with the result, whose output holds no password of secrets.dat.
    public static CheckRun Ended(CommandAppResult result)
    {
        foreach (string password in Passwords)
        {
            Assert.DoesNotContain(password, result.Output, StringComparison.Ordinal);
        }

        return new CheckRun(result);
    }

    public void Dispose()
    {
        Stub.Dispose();
        AgentKey.Dispose();
        _serverKey.Dispose();
    }
}
