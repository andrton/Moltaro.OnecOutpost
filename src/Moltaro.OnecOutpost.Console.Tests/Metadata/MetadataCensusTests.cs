using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Metadata;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The census and the probe of metadata (the S10 brief, "metadata <BASE> <FILE>", step 6): the census, then a page of
// one object with the storage, the first object of the first category that has one. A census or a probe Read that does
// not succeed within the run limit ends metadata with ReportFailed, saying how it ended, and so does a configuration
// without an object; no page runs after them and nothing is written. A probe object with an error of its own is a
// failed object like any other, recorded and counted by its page, and the walk goes on (the coordinator, after the lab
// run of deeb370: one unreadable property must not stop a report).
public sealed class MetadataCensusTests : IDisposable
{
    private const string ErrorText = "The fake configuration refused the call";

    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    private FakeConfiguration Accounting => _metadata.Accounting;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    [Fact]
    public async Task A_census_that_fails_in_1C_ends_metadata_with_ReportFailed_and_its_kind_its_code_and_the_text_of_1C()
    {
        Accounting.CensusFails(ErrorText);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(ConsoleExitCodes.ReportFailed, $"ReportFailed (20): the census ended failed script-error: {ErrorText}");
        Assert.Equal(["census"], Accounting.Answers.Select(answer => answer.Read));
    }

    [Fact]
    public async Task A_census_that_does_not_end_within_the_run_limit_ends_metadata_with_ReportFailed_and_its_worker_is_killed()
    {
        Accounting.CensusOverruns();

        Task<CommandAppResult> started = _metadata.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "run accounting outpost-read census");
        await TestTimes.PassExactlyAsync(Machine.Clock, MetadataMachine.RunLimit, "census");
        MetadataRun run = _metadata.Ended(await TestTimes.EndOf(started));

        run.AssertEnded(ConsoleExitCodes.ReportFailed, "ReportFailed (20): the census did not end within 00:10:00");
        Assert.Contains("kill accounting outpost-read", Machine.Operations);
        Assert.Equal(["census"], Accounting.Answers.Select(answer => answer.Read));
    }

    [Fact]
    public async Task A_probe_that_fails_in_1C_ends_metadata_with_ReportFailed_naming_the_category_of_its_object()
    {
        Accounting.FailsPage("Catalog.Products", ErrorText);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(
            ConsoleExitCodes.ReportFailed,
            $"ReportFailed (20): the probe, the first object of Catalogs, ended failed script-error: {ErrorText}");
        Assert.Equal(["census", "page Catalogs 0 1 storage"], Accounting.Answers.Select(answer => answer.Read));
    }

    [Fact]
    public async Task A_probe_that_does_not_end_within_the_run_limit_ends_metadata_with_ReportFailed_and_its_worker_is_killed()
    {
        Accounting.Overruns("Catalog.Products");

        Task<CommandAppResult> started = _metadata.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "run accounting outpost-read page Catalogs 0 1 storage");
        await TestTimes.PassExactlyAsync(Machine.Clock, MetadataMachine.RunLimit, "probe");
        MetadataRun run = _metadata.Ended(await TestTimes.EndOf(started));

        run.AssertEnded(
            ConsoleExitCodes.ReportFailed,
            "ReportFailed (20): the probe, the first object of Catalogs, did not end within 00:10:00");
        Assert.Contains("kill accounting outpost-read", Machine.Operations);
        Assert.Equal(["census", "page Catalogs 0 1 storage"], Accounting.Answers.Select(answer => answer.Read));
    }

    // The probe's object fails with an error of its own, and the probe's page raises on the storage: the object is
    // recorded and counted as failed by its page, the storage structure is unavailable with its reason, and every page
    // is read without it.
    [Fact]
    public async Task A_probe_object_with_an_error_is_a_failed_object_like_any_other_and_the_walk_goes_on_to_write_the_report()
    {
        const string StorageError = "Insufficient rights to read the storage structure";
        Accounting.ObjectsOf("Catalogs")[0]["error"] = "Field access denied";
        Accounting.StorageFails("Catalog.Products", StorageError);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 5, found: 6, failed: 1);
        Assert.Equal("Field access denied", MetadataReport.TextOf(run.Report.Object("Catalog.Products"), "error"));
        Assert.Equal(new MetadataCategory("Catalogs", 2, 1, 1), run.Report.Category("Catalogs"));
        Assert.False(run.Report.StorageAvailable);
        Assert.Equal(StorageError, run.Report.StorageReason);
        Assert.Equal(
            [
                "census",
                "page Catalogs 0 1 storage",
                "page Catalogs 0 2",
                "page Documents 0 1",
                "page Enums 0 1",
                "page Constants 0 1",
                "page AccumulationRegisters 0 1",
            ],
            Accounting.Answers.Select(answer => answer.Read));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_error_seen_only_by_the_probe_is_reported_with_the_values_read_by_the_objects_page()
    {
        const string ProbeError = "synonym: The synonym could not be read";
        JsonObject product = Accounting.ObjectsOf("Catalogs")[0];
        Accounting.Reading = read =>
        {
            bool probing = read == "page Catalogs 0 1 storage";
            product["error"] = probing ? ProbeError : null;
            product["synonym"] = probing ? null : "Products";
        };

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 5, found: 6, failed: 1);
        MetadataReport report = run.Report;
        JsonElement products = report.Object("Catalog.Products");
        Assert.Equal(ProbeError, MetadataReport.TextOf(products, "error"));
        Assert.Equal("Products", MetadataReport.TextOf(products, "synonym"));
        Assert.True(products.GetProperty("properties").GetProperty("hierarchical").GetBoolean());
        Assert.Equal(new MetadataCategory("Catalogs", 2, 1, 1), report.Category("Catalogs"));
        Assert.Single(report.Objects, item => MetadataReport.TextOf(item, "error") is not null);
    }

    // The S10 brief, "After the lab run of deeb370", 1: a value of the census without an XML form, which XMLString
    // raises on, as the lab found on a typical copy, is {"error": text} in its place, the walk goes on, and the caption
    // of the table names it. The caption may wrap within the width of the table, so its words are read across the
    // lines.
    [Fact]
    public async Task Census_values_that_1C_cannot_give_are_errors_in_their_place_named_in_the_caption_and_the_report_is_written()
    {
        const string PlatformError = "SystemInfo is not available";
        const string ModeError = "XMLString cannot convert the value";
        Accounting.CensusValueFails("platform", PlatformError);
        Accounting.CensusValueFails("compatibilityMode", ModeError);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        JsonElement root = run.Report.Root;
        MetadataRun.AssertJson(new JsonObject { ["error"] = PlatformError }, root.GetProperty("platform"));
        MetadataRun.AssertJson(
            new JsonObject { ["name"] = "OutpostTest", ["version"] = "1.0.0.1", ["compatibilityMode"] = new JsonObject { ["error"] = ModeError } },
            root.GetProperty("configuration"));
        Assert.Contains(
            $"Not read: platform ({PlatformError}), compatibilityMode ({ModeError})",
            string.Join(" ", run.Output.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)),
            StringComparison.Ordinal);
    }

    // A configuration whose catalogs are empty: the probe reads the first document.
    [Fact]
    public async Task The_probe_reads_the_first_object_of_the_first_category_that_has_one()
    {
        FakeConfiguration configuration = new();
        configuration.Add("Documents", "Document.GoodsReceipt", "Goods receipt");
        configuration.Add("Documents", "Document.GoodsIssue", "Goods issue");
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("outpost-read", "Outpost read") { Configuration = configuration };

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 2, found: 2, failed: 0);
        Assert.Equal(
            ["census", "page Documents 0 1 storage", "page Documents 0 2 storage", "common"],
            configuration.Answers.Select(answer => answer.Read));
    }

    [Fact]
    public async Task A_configuration_without_an_object_ends_metadata_with_ReportFailed()
    {
        FakeConfiguration configuration = new();
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("outpost-read", "Outpost read") { Configuration = configuration };

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(ConsoleExitCodes.ReportFailed, "ReportFailed (20): the configuration has no object to read");
        Assert.Equal(["census"], configuration.Answers.Select(answer => answer.Read));
    }
}
