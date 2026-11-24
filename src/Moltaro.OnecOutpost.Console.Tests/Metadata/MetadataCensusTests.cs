using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The census and the probe of metadata (the S10 brief, "metadata <BASE> <FILE>", step 6): the census, then a page of
// one object with the storage, the first object of the first category that has one. A census or a probe that does not
// succeed within the run limit ends metadata with ReportFailed, saying how it ended; so does a probe object with an
// error of its own, judged before a storageError, and a configuration without an object. No page runs after them and
// nothing is written.
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

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_census_that_fails_in_1C_ends_metadata_with_ReportFailed_and_its_kind_its_code_and_the_text_of_1C()
    {
        Accounting.CensusFails(ErrorText);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(ConsoleExitCodes.ReportFailed, $"ReportFailed (20): the census ended failed script-error: {ErrorText}");
        Assert.Equal(["census"], Accounting.Answers.Select(answer => answer.Read));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
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

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_probe_that_fails_in_1C_ends_metadata_with_ReportFailed_naming_the_category_of_its_object()
    {
        Accounting.FailsPage("Catalog.Products", ErrorText);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(
            ConsoleExitCodes.ReportFailed,
            $"ReportFailed (20): the probe, the first object of Catalogs, ended failed script-error: {ErrorText}");
        Assert.Equal(["census", "page Catalogs 0 1 storage"], Accounting.Answers.Select(answer => answer.Read));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
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

    // The error of the object is judged before the storageError of its page, which alone would leave the walk without
    // the storage.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_probe_object_with_an_error_ends_metadata_with_ReportFailed_and_its_error_before_a_storage_error()
    {
        Accounting.ObjectsOf("Catalogs")[0]["error"] = "Field access denied";
        Accounting.StorageFails("Catalog.Products", "Insufficient rights to read the storage structure");

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(ConsoleExitCodes.ReportFailed, "ReportFailed (20): the probe of Catalog.Products failed: Field access denied");
        Assert.Equal(["census", "page Catalogs 0 1 storage"], Accounting.Answers.Select(answer => answer.Read));
    }

    // A configuration whose catalogs are empty: the probe reads the first document.
    [Fact(Skip = SkipReason.NotImplemented)]
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

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_configuration_without_an_object_ends_metadata_with_ReportFailed()
    {
        FakeConfiguration configuration = new();
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("outpost-read", "Outpost read") { Configuration = configuration };

        MetadataRun run = await _metadata.RunAsync();

        run.AssertEnded(ConsoleExitCodes.ReportFailed, "ReportFailed (20): the configuration has no object to read");
        Assert.Equal(["census"], configuration.Answers.Select(answer => answer.Read));
    }
}
