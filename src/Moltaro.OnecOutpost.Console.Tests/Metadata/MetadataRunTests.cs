using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Metadata;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// metadata on a machine where everything holds (the S10 brief, "metadata <BASE> <FILE>", "The report", and Acceptance):
// after check's rule, one worker of the base's read account, in read mode and in safe mode, under the base's worker
// account or under the console's own token, reads the census, the probe, each category in pages of 50 with the storage,
// and the common storage, by the one script of the walk; the report is written whole, format 1, and the run shows the
// table of the categories and ends with Success. It only reads: it changes no file of the data folder and nothing of
// the machine, never reaches the server, logs the worker account on once and runs no probe.
public sealed class MetadataRunTests : IDisposable
{
    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    private FakeConfiguration Accounting => _metadata.Accounting;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task On_a_machine_where_everything_holds_metadata_writes_the_report_and_ends_with_Success_showing_each_category_of_the_census()
    {
        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        Assert.Equal(run.Report.Categories, run.Rows);
        Assert.Equal(FakeConfiguration.Categories, run.Rows.Select(row => row.Category));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_report_holds_its_format_the_base_the_time_the_platform_the_configuration_the_storage_and_each_category_in_the_order_of_the_census()
    {
        MetadataRun run = await _metadata.RunAsync();

        JsonElement root = run.Report.Root;
        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal("accounting", root.GetProperty("base").GetString());
        Assert.Equal("2026-11-22T10:15:00.123Z", root.GetProperty("written").GetString());
        Assert.Equal(FakeConfiguration.Platform, root.GetProperty("platform").GetString());
        MetadataRun.AssertJson(
            new JsonObject { ["name"] = "OutpostTest", ["version"] = "1.0.0.1", ["compatibilityMode"] = "Version8_2_13" },
            root.GetProperty("configuration"));
        MetadataRun.AssertJson(new JsonObject { ["available"] = true, ["reason"] = null }, root.GetProperty("storage"));

        Assert.Equal(
            [.. FakeConfiguration.Categories.Select(category => new MetadataCategory(
                category,
                Accounting.ObjectsOf(category).Count,
                Accounting.ObjectsOf(category).Count,
                0))],
            run.Report.Categories);
        Assert.Equal(new MetadataCategory("Catalogs", 2, 2, 0), run.Report.Category("Catalogs"));
    }

    // Each object as its page gave it, with the category of the page and its tables, in the census's order: by
    // category, then by its index in its collection.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_report_holds_each_object_as_its_page_gave_it_with_its_category_and_its_tables_in_the_order_of_the_census()
    {
        MetadataRun run = await _metadata.RunAsync();

        (string Category, string FullName)[] expected =
        [
            ("Catalogs", "Catalog.Products"),
            ("Catalogs", "Catalog.Units"),
            ("Documents", "Document.GoodsReceipt"),
            ("Enums", "Enum.Statuses"),
            ("Constants", "Constant.Currency"),
            ("AccumulationRegisters", "AccumulationRegister.Stock"),
        ];
        IReadOnlyList<JsonElement> objects = run.Report.Objects;
        Assert.Equal(expected.Length, objects.Count);

        for (int index = 0; index < expected.Length; index++)
        {
            JsonObject paged = Accounting.Paged(expected[index].FullName, storage: true);
            paged["category"] = expected[index].Category;

            MetadataRun.AssertJson(paged, objects[index]);
        }
    }

    // Every Read in its order: the census, the probe, a page of the first object of the first category with an object,
    // each category in a page from its first object, and the common storage, each with the bytes of its outcome.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_report_holds_the_common_storage_and_every_Read_in_its_order_with_the_bytes_of_its_outcome()
    {
        MetadataRun run = await _metadata.RunAsync();

        MetadataRun.AssertJson(Accounting.CommonStorage, run.Report.CommonStorage!.Value);
        run.AssertReads(
            Accounting,
            ("census", null, null, null, "succeeded", null),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 0, 2, "succeeded", null),
            ("page", "Documents", 0, 1, "succeeded", null),
            ("page", "Enums", 0, 1, "succeeded", null),
            ("page", "Constants", 0, 1, "succeeded", null),
            ("page", "AccumulationRegisters", 0, 1, "succeeded", null),
            ("common", null, null, null, "succeeded", null));
        Assert.All(run.Report.Reads, read => Assert.Null(read.ErrorText));
    }

    // The probe and every page ask for the storage, which the probe found available.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_probe_and_every_page_ask_for_the_storage_and_the_common_storage_is_read_last()
    {
        await _metadata.RunAsync();

        Assert.Equal(
            [
                "census",
                "page Catalogs 0 1 storage",
                "page Catalogs 0 2 storage",
                "page Documents 0 1 storage",
                "page Enums 0 1 storage",
                "page Constants 0 1 storage",
                "page AccumulationRegisters 0 1 storage",
                "common",
            ],
            Accounting.Answers.Select(answer => answer.Read));
    }

    // A category of 120 objects is read in pages of 50, the last one of what is left, each from the index after the
    // last one before it.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_category_is_read_from_its_first_object_in_pages_of_50()
    {
        for (int index = 3; index <= 120; index++)
        {
            Accounting.Add("Catalogs", $"Catalog.Extra{index}", $"Extra {index}");
        }

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 124, found: 124, failed: 0);
        Assert.Equal(
            ["page Catalogs 0 50 storage", "page Catalogs 50 50 storage", "page Catalogs 100 20 storage"],
            Accounting.Answers.Select(answer => answer.Read).Skip(2).Take(3));
        Assert.Equal(new MetadataCategory("Catalogs", 120, 120, 0), run.Report.Category("Catalogs"));
    }

    // One UTF-8 JSON file without a byte order mark, indented, every character written as it is, such as the Cyrillic
    // of a synonym and the &, <, > and ' that an encoder for HTML would escape.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_report_is_indented_UTF_8_without_a_byte_order_mark_and_writes_every_character_as_it_is()
    {
        MetadataRun run = await _metadata.RunAsync();

        byte[] bytes = run.Report.Bytes;
        Assert.False(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), "The report begins with a byte order mark.");
        string text = run.Report.Text;
        Assert.Matches(new Regex("\n +\"format\": 1"), text);
        Assert.Contains("\"Товары\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Goods & services <sold> 'here'\"", text, StringComparison.Ordinal);
    }

    // Nothing of the report is data of the base, a password or an account.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_report_holds_no_password_and_no_account()
    {
        MetadataRun run = await _metadata.RunAsync();

        string text = run.Report.Text;
        Assert.DoesNotContain("S3cret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("outpost-read", text, StringComparison.Ordinal);
        Assert.DoesNotContain("outpost-write", text, StringComparison.Ordinal);
        Assert.DoesNotContain(MetadataMachine.WorkerAccount, text, StringComparison.Ordinal);
    }

    // check's rule first, the files, the connector and the access lists, then the one logon of the worker account, then
    // one session and its Reads; no probe.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Metadata_takes_its_steps_in_their_order_and_runs_no_probe()
    {
        await _metadata.RunAsync();

        _metadata.AssertInOrder(
            "find",
            "others \"\"",
            $"writers {TestMachine.ProgramFolder}",
            $"writers {MetadataMachine.ConnectorFolder}",
            "logon accounting outpost-worker",
            "token accounting outpost-worker",
            $"start accounting {MetadataMachine.Program} under outpost-worker",
            "open accounting outpost-read",
            "run accounting outpost-read census",
            "run accounting outpost-read page Catalogs 0 1 storage",
            "run accounting outpost-read page Catalogs 0 2 storage",
            "run accounting outpost-read page AccumulationRegisters 0 1 storage",
            "run accounting outpost-read common");
        Assert.DoesNotContain(Machine.Operations, line => line.StartsWith("probe ", StringComparison.Ordinal));
        Assert.Single(Machine.Operations, line => line.StartsWith("start ", StringComparison.Ordinal));
    }

    // metadata works while the service runs, which holds the audit log and writes the outbox: it changes no file of the
    // data folder, takes no lock there, changes nothing else of the machine, and never reaches the server.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Metadata_only_reads_the_data_folder_changes_nothing_of_the_machine_and_reaches_no_server()
    {
        string outcome = $"{Guid.NewGuid()}.json";
        Machine.Data.Audit.Seed("2026-11-21.jsonl", "{}\n");
        Machine.Data.Outcomes.Seed(outcome, "{}");
        string[] files = [AgentFolders.PolicyName, AgentFolders.PairingName, AgentFolders.SecretsName];
        byte[][] before = [.. files.Select(Machine.Data.Root.Bytes)];

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        string[] changes = ["append ", "cut ", "replace ", "delete ", "own ", "apply ", "grant ", "revoke ", "install ", "stop ", "regsvr32 "];
        Assert.DoesNotContain(Machine.Operations, line => changes.Any(change => line.StartsWith(change, StringComparison.Ordinal)));
        Assert.Equal(before, [.. files.Select(Machine.Data.Root.Bytes)]);
        Assert.Equal(files.Order(StringComparer.Ordinal), Machine.Data.Root.Names().Order(StringComparer.Ordinal));
        Assert.Equal(["2026-11-21.jsonl"], Machine.Data.Audit.Names());
        Assert.Equal([outcome], Machine.Data.Outcomes.Names());
        Assert.Empty(Machine.Routes);
    }

    // metadata runs Reads of its one script alone, in read mode and in safe mode whatever the policy says of the Writes
    // of the base, such as accounting's without safe mode, under the read account with its password.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Every_worker_runs_the_walk_in_read_mode_and_in_safe_mode_with_the_read_account_and_its_password()
    {
        await _metadata.RunAsync();

        FakeCheckProcess process = Assert.Single(Machine.Workers.Processes);
        Assert.Equal(MetadataMachine.Program, process.Program);
        Assert.NotNull(process.Connection);
        Assert.Equal("Srvr=\"srv-1c:1541\";Ref=\"accounting\";", process.Connection.Location);
        Assert.Equal("outpost-read", process.Connection.User);
        Assert.Equal(MetadataMachine.ReadPassword, process.Connection.Password);
        Assert.True(process.Connection.SafeMode);
        Assert.Equal(MetadataMachine.Executor, process.Connection.Executor);
        Assert.Equal(8, process.Commands.Count);
        Assert.All(process.Commands, command =>
        {
            Assert.Equal(MetadataScripts.Walk, command.Script);
            Assert.Null(command.CallerKey);
            Assert.Equal(OutpostProtocol.MaxOutcomeSizeLimit, command.OutcomeLimit);

            using JsonDocument parameters = JsonDocument.Parse(command.ParametersJson);
            Assert.Contains(parameters.RootElement.GetProperty("read").GetString(), new[] { "census", "page", "common" });
        });
    }

    // The service runs every worker of a base with a worker account under it (D12), so metadata does as well, through
    // the Secondary Logon service; a base without one under the console's own token, which logs nothing on.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("accounting", "outpost-worker")]
    [InlineData("warehouse", null)]
    public async Task The_worker_of_a_base_with_a_worker_account_runs_under_it_and_that_of_a_base_without_one_under_the_console(
        string baseKey,
        string? account)
    {
        MetadataRun run = await _metadata.RunAsync(baseKey);

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        FakeCheckProcess process = Assert.Single(Machine.Workers.Processes);
        Assert.Equal((baseKey, account), (process.Base, process.Account));
        Assert.True(process.Connection!.SafeMode);
        Assert.Equal(account is null ? 0 : 1, Machine.Workers.LogOns.Count);
    }

    // Each failed logon counts toward a lockout of the account, so metadata logs the worker account on once per run,
    // with the password of secrets.dat, and closes it with every worker by its end.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_worker_account_logs_on_once_and_every_worker_and_account_is_closed_by_the_end()
    {
        await _metadata.RunAsync();

        Assert.Equal([("accounting", MetadataMachine.WorkerAccount, MetadataMachine.WorkerPassword)], Machine.Workers.LogOns);
        Assert.All(Machine.Workers.Processes, process => Assert.True(process.IsDisposed, $"The worker of {process.Base} was not disposed."));
        Assert.All(Machine.Workers.Accounts, account => Assert.True(account.IsDisposed, $"{account.Account} was not disposed."));
    }
}
