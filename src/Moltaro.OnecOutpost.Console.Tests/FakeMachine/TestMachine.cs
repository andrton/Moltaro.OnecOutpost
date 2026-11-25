using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeWindows;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The agent machine of a test, made of fakes of every boundary of ConsoleParts, and the console run on it through
// CommandAppTester with ConsoleApp.Configure, as Program runs it on a real machine. The data folder is in memory; it
// and every fake of the machine write their calls into the one operation
// list of the data folder, so that a test states the steps of a command in their order and that nothing came after a
// failed one. The paths are those of a real machine, rooted as the platform of the tests roots a path, so that the tests
// read alike on every platform; nothing is at them. The console is elevated, and its input is the console, until the
// test says otherwise. A fresh machine has neither the service nor the data folder; Installed gives it the data folder
// that install made.
internal sealed class TestMachine
{
    public const string ServiceName = "MoltaroOnecOutpost";

    public const string Version = "0.1.0-test";

    // A policy.json with a proxy and a requestTimeout of its own, a base that may be written without safe mode under a
    // worker account, and a base that may only be read.
    public const string Policy = """
        {
          "proxy": "http://proxy.example.com:3128",
          "requestTimeout": "00:00:20",
          "bases": [
            {
              "key": "accounting",
              "server": "srv-1c:1541",
              "database": "accounting",
              "read": { "account": "outpost-read" },
              "write": { "account": "outpost-write", "safeMode": false, "workerAccount": "outpost-worker" }
            },
            {
              "key": "warehouse",
              "file": "D:\\1C\\Warehouse",
              "read": { "account": "outpost-read" }
            }
          ]
        }
        """;

    // A pairing token in the form of §5 of docs/protocol.md, for a run that sends nothing.
    public static readonly string Token = new OutpostPairingTokenText(
        Guid.Parse("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b"),
        new byte[16],
        new string('0', 64)).Write();

    public static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    public static readonly string ProgramFolder = Path.Combine(Root, "Program Files", "Moltaro.OnecOutpost");

    public static readonly string DataFolder = Path.Combine(Root, "ProgramData", "Moltaro.OnecOutpost");

    // The folder that the administrator unpacked the archive into, from which the console runs.
    public static readonly string Archive = Path.Combine(Root, "Users", "admin", "Downloads", "Moltaro.OnecOutpost-0.1.0-win");

    public TestMachine()
    {
        Access = new FakeAccessLists(Data, DataFolder);
        Services = new FakeServiceControl(Data);
        Rights = new FakeAccountRights(Data);
        Folders = new FakeFolderSetup(Data);
        Connectors = new FakeConnectors(Data);
        Registration = new FakeRegistration(Data, Connectors);
        Workers = new FakeWorkerStarts(Data);
    }

    // The SID of NT SERVICE\MoltaroOnecOutpost, the agent's account.
    public static string AgentSid { get; } = ServiceAccount.Sid(ServiceName);

    public MemoryDataFolder Data { get; } = new();

    public FakeAccessLists Access { get; }

    public FakeProtection Protection { get; } = new();

    public FakeServiceControl Services { get; }

    public FakeAccountRights Rights { get; }

    public FakeFolderSetup Folders { get; }

    public FakeConnectors Connectors { get; }

    public FakeRegistration Registration { get; }

    public FakeWorkerStarts Workers { get; }

    public FakeClock Clock { get; } = new(TestTimes.Start);

    public bool Elevated { get; set; } = true;

    // The folder the console runs from.
    public string Source { get; set; } = Archive;

    // The redirected standard input, null for the console.
    public TextReader? Input { get; set; }

    // The handler to the server that ConsoleParts.Handlers gives, such as the stub of the pair endpoint.
    public HttpMessageHandler? Server { get; set; }

    // The server and the proxy of each handler made.
    public List<(Uri Server, Uri? Proxy)> Routes { get; } = [];

    // The SID of each account that the access lists of the data folder were made for.
    public List<string> AccessListAccounts { get; } = [];

    // The console of the runs: not interactive, and as wide as any line, until the test gives another.
    public TestConsole Console { get; set; } = new TestConsole().Width(int.MaxValue);

    // The lines of the operation list so far.
    public IReadOnlyList<string> Operations => [.. Data.Changes.Select(change => change.Line)];

    // The arguments of a run of the command, valid as its settings judge them: install, uninstall, pair, secret, check,
    // metadata, or connector register. The report of metadata is a new file of the temporary folder of the disk, which
    // exists, outside the data folder; a run that ends before its walk writes nothing there.
    public static string[] CommandLine(string command)
    {
        return command switch
        {
            "install" => ["install"],
            "uninstall" => ["uninstall"],
            "pair" => ["pair", "https://outpost.example.com/agents/", Token],
            "secret" => ["secret", "accounting", "read"],
            "check" => ["check"],
            "metadata" => ["metadata", "accounting", Path.Combine(Path.GetTempPath(), $"outpost-metadata-{Guid.NewGuid():N}.json")],
            "connector register" => ["connector", "register"],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "No such command."),
        };
    }

    // The lines of a run's output.
    public static IReadOnlyList<string> LinesOf(CommandAppResult result)
    {
        return result.Output.Split('\n');
    }

    // The data folder as install made it, the folder, audit\ and outcomes\, with the policy given in it.
    public void Installed(string policy = Policy)
    {
        Folders.Kinds[DataFolder] = FolderKindEnum.Folder;
        Folders.Kinds[Path.Combine(DataFolder, "audit")] = FolderKindEnum.Folder;
        Folders.Kinds[Path.Combine(DataFolder, "outcomes")] = FolderKindEnum.Folder;
        Data.Root.Seed("policy.json", policy);
    }

    // The service as install registered it, stopped.
    public void HasService()
    {
        Services.Exists = true;
        Services.StopResult = ServiceStopEnum.Stopped;
    }

    // Runs the console with the arguments to its end, which comes within the deadline of the tests.
    public Task<CommandAppResult> RunAsync(params string[] arguments)
    {
        return TestTimes.EndOf(Start(TestContext.Current.CancellationToken, arguments));
    }

    // Starts a run of the console with the arguments and the token of the run, for a test that acts while it runs.
    public Task<CommandAppResult> Start(CancellationToken cancellationToken, params string[] arguments)
    {
        ConsoleParts parts = new(
            Elevated,
            ServiceName,
            AgentSid,
            Source,
            ProgramFolder,
            DataFolder,
            Data.Folders,
            Services,
            Rights,
            Folders,
            sid =>
            {
                AccessListAccounts.Add(sid);
                return Access;
            },
            Protection,
            Connectors,
            Registration,
            Workers,
            (server, proxy) =>
            {
                Routes.Add((server, proxy));
                return Server ?? throw new InvalidOperationException("The test gave its machine no server.");
            },
            Input,
            Clock,
            Version);

        CommandAppTester tester = new(console: Console);
        tester.Configure(config => ConsoleApp.Configure(config, parts));

        return tester.RunAsync(arguments, cancellationToken);
    }
}
