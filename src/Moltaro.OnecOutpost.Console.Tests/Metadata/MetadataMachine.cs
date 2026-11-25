using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// A test's machine as install, pair and secret left it, on which metadata writes a report of each base: the data
// folder with the policy of TestMachine, agent.json and secrets.dat with the
// agent's key and every password the policy needs; the COM connector of one version registered for 64 bits; the worker
// account of accounting, a local user that logs on for a batch job with an ordinary token; and for the read account of
// each base a session whose configuration is FakeConfiguration.Standard, which answers the walk as its script would.
// The reports go to a temporary folder of the disk of the test's own, removed with the machine, which the run judges as
// it judges any folder: its final path, by the fake, is its own spelling, outside the data folder. The machine has no
// server, so a run that reached one would fail, and every handler it asks for is a route of the machine. A run fails
// when any password of secrets.dat reaches its output or its report.
internal sealed class MetadataMachine : IDisposable
{
    public const string ConnectorVersion = "8.3.27.1859";

    public const string WorkerAccount = "outpost-worker";

    // The passwords of secrets.dat, none of which may reach an output or a report.
    public const string ReadPassword = "read-S3cret";

    public const string WritePassword = "write-S3cret";

    public const string WarehousePassword = "warehouse-S3cret";

    public const string WorkerPassword = "worker-S3cret";

    // The runLimit of the read mode, which the policy of TestMachine leaves at its default.
    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private static readonly string[] Passwords = [ReadPassword, WritePassword, WarehousePassword, WorkerPassword];

    private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private readonly ECDsa _agentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public MetadataMachine()
    {
        Machine.Installed();
        Machine.Data.Root.Seed(AgentFolders.PairingName, AgentPairingFile.Write(new AgentPairingSettings(
            new Uri("https://outpost.example.com/agents/"),
            Guid.Parse("7a1c2e3f-4b5d-4e6f-8a90-b1c2d3e4f5a6"),
            OutpostKeys.ExportPublicKey(_serverKey),
            FakeServer.PollTimeout,
            FakeServer.LeaseGrace)));
        WriteSecrets(Secrets());
        Machine.Connectors.Registered(ConnectorBitnessEnum.X64, ConnectorVersion);
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("outpost-read", "Outpost read") { Configuration = Accounting };
        Machine.Workers.Sessions[("warehouse", "outpost-read")] = new FakeSession("outpost-read", "Outpost read") { Configuration = Warehouse };
    }

    public TestMachine Machine { get; } = new();

    // The metadata of each base as its read account sees it.
    public FakeConfiguration Accounting { get; } = FakeConfiguration.Standard();

    public FakeConfiguration Warehouse { get; } = FakeConfiguration.Standard();

    // The folder of the reports.
    public TemporaryFolder Reports { get; } = new();

    // The worker program and the executor of the program folder for the connector of 64 bits, with which every worker
    // starts, and the folder of that connector.
    public static string Program => ProgramFolderLayout.WorkerOf(TestMachine.ProgramFolder, ConnectorBitnessEnum.X64);

    public static string Executor => ProgramFolderLayout.ExecutorOf(TestMachine.ProgramFolder);

    public static string ConnectorFolder => Path.GetDirectoryName(FakeConnectors.PathOf(ConnectorBitnessEnum.X64, ConnectorVersion))!;

    // The session of the read account of the base.
    public FakeSession SessionOf(string baseKey)
    {
        return Machine.Workers.Sessions[(baseKey, "outpost-read")];
    }

    // The report of a run for the base, a new file of the folder of the reports.
    public string ReportOf(string baseKey)
    {
        return Reports.PathOf($"{baseKey}.json");
    }

    // The secrets of the machine: the agent's key, each password the policy needs, and the worker account's.
    public AgentSecrets Secrets()
    {
        return new AgentSecrets(
            _agentKey.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [("accounting", OutpostModeEnum.Read)] = ReadPassword,
                [("accounting", OutpostModeEnum.Write)] = WritePassword,
                [("warehouse", OutpostModeEnum.Read)] = WarehousePassword,
            },
            new Dictionary<string, string> { ["accounting"] = WorkerPassword });
    }

    // secrets.dat with these secrets, as secret writes it.
    public void WriteSecrets(AgentSecrets secrets)
    {
        Machine.Data.Root.Seed(AgentFolders.SecretsName, SecretsFile.Write(secrets, Machine.Protection));
    }

    // Runs metadata of the base into its report to its end.
    public async Task<MetadataRun> RunAsync(string baseKey = "accounting")
    {
        return Ended(await Machine.RunAsync("metadata", baseKey, ReportOf(baseKey)), baseKey);
    }

    // Starts metadata of the base, for a test that acts while it runs, such as one that moves the clock past a run
    // limit; the test ends the run by Ended.
    public Task<CommandAppResult> Start(string baseKey = "accounting")
    {
        return Machine.Start(TestContext.Current.CancellationToken, "metadata", baseKey, ReportOf(baseKey));
    }

    // The run of metadata of the base that ended with the result, whose output and report hold no password of
    // secrets.dat.
    public MetadataRun Ended(CommandAppResult result, string baseKey = "accounting")
    {
        string file = ReportOf(baseKey);

        foreach (string password in Passwords)
        {
            Assert.DoesNotContain(password, result.Output, StringComparison.Ordinal);

            if (File.Exists(file))
            {
                Assert.DoesNotContain(password, File.ReadAllText(file), StringComparison.Ordinal);
            }
        }

        return new MetadataRun(result, baseKey, file);
    }

    // The operations of the machine hold lines that start as given, in that order, among the other operations.
    public void AssertInOrder(params string[] starts)
    {
        IReadOnlyList<string> operations = Machine.Operations;
        int from = 0;

        foreach (string start in starts)
        {
            int index = -1;

            for (int position = from; position < operations.Count; position++)
            {
                if (operations[position].StartsWith(start, StringComparison.Ordinal))
                {
                    index = position;
                    break;
                }
            }

            Assert.True(index >= 0, $"No operation \"{start}…\" after the one before it:\n{string.Join("\n", operations)}");
            from = index + 1;
        }
    }

    public void Dispose()
    {
        Reports.Dispose();
        _agentKey.Dispose();
        _serverKey.Dispose();
    }
}
