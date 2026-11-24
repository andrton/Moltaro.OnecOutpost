using System.Text.Json;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Metadata;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The pages of metadata (the S10 brief, "metadata <BASE> <FILE>", steps 7 and 9, and "The report"): a page that gives
// result-too-large, or no outcome within the run limit, is read again as two halves, the first ⌈count/2⌉, until one
// object, which is then recorded as failed with that code; any other end records the page's objects as failed with its
// kind, code and error text, unhalved. A page that ended its worker, by the run limit or as the judgment of its outcome
// says, such as connection-lost, makes the next Read open a new session with the same account and token; a session that
// does not reopen records every page left, and common, as failed, with the open's reason. An object with an error of
// its own is recorded and counted as failed. The run ends with Success while a page was read, and with ReportFailed,
// writing nothing, when none was.
public sealed class MetadataPageTests : IDisposable
{
    private const string ErrorText = "The fake page refused the call";

    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    private FakeConfiguration Accounting => _metadata.Accounting;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    // Catalogs holds Products, Units and Warehouses, of which Units is too large: the page of three is read as two and
    // one, the two as one and one.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_page_that_gives_result_too_large_is_halved_until_its_one_object_too_large_is_recorded_as_failed()
    {
        Accounting.Add("Catalogs", "Catalog.Warehouses", "Warehouses");
        Accounting.TooLarge("Catalog.Units");

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 7, failed: 1);
        run.AssertReads(
            Accounting,
            ("census", null, null, null, "succeeded", null),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 0, 3, "failed", "result-too-large"),
            ("page", "Catalogs", 0, 2, "failed", "result-too-large"),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 1, 1, "failed", "result-too-large"),
            ("page", "Catalogs", 2, 1, "succeeded", null),
            ("page", "Documents", 0, 1, "succeeded", null),
            ("page", "Enums", 0, 1, "succeeded", null),
            ("page", "Constants", 0, 1, "succeeded", null),
            ("page", "AccumulationRegisters", 0, 1, "succeeded", null),
            ("common", null, null, null, "succeeded", null));
        Assert.Equal(new MetadataCategory("Catalogs", 3, 2, 1), run.Report.Category("Catalogs"));
        Assert.Equal(
            ["Catalog.Products", "Catalog.Warehouses"],
            CatalogsOf(run.Report));
        Assert.Single(Machine.Workers.Processes);
    }

    // Each Read that reaches the run limit ends its worker, so each half is read in a new session, of the same account
    // with the token of its one logon.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_page_that_does_not_end_within_the_run_limit_is_halved_each_half_in_a_new_session_of_the_same_account()
    {
        Accounting.Add("Catalogs", "Catalog.Warehouses", "Warehouses");
        Accounting.Overruns("Catalog.Units");

        Task<CommandAppResult> started = _metadata.Start();
        await PassRunLimitAsync("page Catalogs 0 3 storage");
        await PassRunLimitAsync("page Catalogs 0 2 storage");
        await PassRunLimitAsync("page Catalogs 1 1 storage");
        MetadataRun run = _metadata.Ended(await TestTimes.EndOf(started));

        run.AssertSucceeded(read: 6, found: 7, failed: 1);
        run.AssertReads(
            Accounting,
            ("census", null, null, null, "succeeded", null),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 0, 3, "failed", "run-limit"),
            ("page", "Catalogs", 0, 2, "failed", "run-limit"),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 1, 1, "failed", "run-limit"),
            ("page", "Catalogs", 2, 1, "succeeded", null),
            ("page", "Documents", 0, 1, "succeeded", null),
            ("page", "Enums", 0, 1, "succeeded", null),
            ("page", "Constants", 0, 1, "succeeded", null),
            ("page", "AccumulationRegisters", 0, 1, "succeeded", null),
            ("common", null, null, null, "succeeded", null));
        Assert.Equal(new MetadataCategory("Catalogs", 3, 2, 1), run.Report.Category("Catalogs"));
        Assert.Equal(4, Machine.Workers.Processes.Count);
        Assert.All(Machine.Workers.Processes, process => Assert.Equal(MetadataMachine.WorkerAccount, process.Account));
        Assert.Single(Machine.Workers.LogOns);
        Assert.Single(Machine.Workers.Accounts);
        Assert.Equal(3, Machine.Operations.Count(line => line == "kill accounting outpost-read"));
    }

    // A script error is no matter of size, so the page is not halved and both of its objects fail; the worker goes on.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_page_that_fails_in_1C_records_its_objects_as_failed_with_its_kind_its_code_and_the_text_of_1C()
    {
        Accounting.FailsPage("Catalog.Units", ErrorText);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 4, found: 6, failed: 2);
        MetadataRead page = run.Report.Reads[2];
        Assert.Equal(
            ("page", "Catalogs", 0, 2, "failed", "script-error", ErrorText),
            (page.Read, page.Category, page.From, page.Count, page.Kind, page.Code, page.ErrorText));
        Assert.Equal(new MetadataCategory("Catalogs", 2, 0, 2), run.Report.Category("Catalogs"));
        Assert.Empty(CatalogsOf(run.Report));
        Assert.Equal(8, run.Report.Reads.Count);
        Assert.Single(Machine.Workers.Processes);
    }

    // connection-lost ends the COM worker, so the next page, Documents, is read in a new session; the lost page is not
    // halved.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_page_that_loses_the_connection_records_its_objects_as_failed_and_the_next_page_opens_a_new_session()
    {
        Accounting.LosesConnection("Catalog.Units");

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 4, found: 6, failed: 2);
        run.AssertReads(
            Accounting,
            ("census", null, null, null, "succeeded", null),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 0, 2, "failed", "connection-lost"),
            ("page", "Documents", 0, 1, "succeeded", null),
            ("page", "Enums", 0, 1, "succeeded", null),
            ("page", "Constants", 0, 1, "succeeded", null),
            ("page", "AccumulationRegisters", 0, 1, "succeeded", null),
            ("common", null, null, null, "succeeded", null));
        Assert.Equal(2, Machine.Workers.Processes.Count);
        Assert.All(Machine.Workers.Processes, process => Assert.Equal(MetadataMachine.WorkerAccount, process.Account));
        Assert.Single(Machine.Workers.LogOns);
        _metadata.AssertInOrder(
            "run accounting outpost-read page Catalogs 0 2 storage",
            "start accounting ",
            "open accounting outpost-read",
            "run accounting outpost-read page Documents 0 1 storage");
    }

    // The page of Documents ends the worker and the session that would replace it does not open: Enums, Constants,
    // AccumulationRegisters and common are recorded as failed without a code, with the open's reason, and no further
    // session is tried. Catalogs was read, so the report is written.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_session_that_does_not_reopen_records_every_page_left_and_common_as_failed_with_the_reason_of_its_open()
    {
        const string Diagnostics = "No session opened: the cluster refused the connection";
        Accounting.LosesConnection("Document.GoodsReceipt");
        _metadata.SessionOf("accounting").RefuseOpenFrom(2, Diagnostics);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 2, found: 6, failed: 4);
        run.AssertReads(
            Accounting,
            ("census", null, null, null, "succeeded", null),
            ("page", "Catalogs", 0, 1, "succeeded", null),
            ("page", "Catalogs", 0, 2, "succeeded", null),
            ("page", "Documents", 0, 1, "failed", "connection-lost"),
            ("page", "Enums", 0, 1, "failed", null),
            ("page", "Constants", 0, 1, "failed", null),
            ("page", "AccumulationRegisters", 0, 1, "failed", null),
            ("common", null, null, null, "failed", null));
        Assert.All(
            run.Report.Reads.Skip(4),
            read => Assert.Contains(Diagnostics, read.ErrorText ?? "", StringComparison.Ordinal));
        Assert.Null(run.Report.CommonStorage);
        Assert.Equal(2, Machine.Workers.Processes.Count);
        Assert.Equal(new MetadataCategory("Enums", 1, 0, 1), run.Report.Category("Enums"));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_object_with_an_error_of_its_own_is_recorded_with_it_and_counted_as_failed()
    {
        Accounting.ObjectsOf("Catalogs")[1]["error"] = "The object cannot be read";

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 5, found: 6, failed: 1);
        Assert.Equal("The object cannot be read", MetadataReport.TextOf(run.Report.Object("Catalog.Units"), "error"));
        Assert.Equal(new MetadataCategory("Catalogs", 2, 1, 1), run.Report.Category("Catalogs"));
        Assert.Contains(new MetadataCategory("Catalogs", 2, 1, 1), run.Rows);
    }

    // The probe reads Products alone, and the one page of the configuration, Products and Units, fails.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_walk_in_which_no_page_was_read_ends_metadata_with_ReportFailed_and_its_first_end()
    {
        FakeConfiguration configuration = new();
        configuration.Add("Catalogs", "Catalog.Products", "Products");
        configuration.Add("Catalogs", "Catalog.Units", "Units");
        configuration.FailsPage("Catalog.Units", ErrorText);
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("outpost-read", "Outpost read") { Configuration = configuration };

        MetadataRun run = await _metadata.RunAsync();

        Assert.True(run.ExitCode == ConsoleExitCodes.ReportFailed, $"The run ended with {run.ExitCode}; its output:\n{run.Output}");
        string end = Assert.Single(run.Lines, line => line.StartsWith("ReportFailed (20): no page of the walk was read: ", StringComparison.Ordinal));
        Assert.Contains(ErrorText, end, StringComparison.Ordinal);
        run.AssertNoFile();
    }

    // The full names of the catalogs of the report, in its order.
    private static IReadOnlyList<string> CatalogsOf(MetadataReport report)
    {
        return [.. report.Objects.Where(item => MetadataReport.TextOf(item, "category") == "Catalogs").Select(item => item.GetProperty("fullName").GetString()!)];
    }

    // Waits until the Read of the walk runs and moves the clock by the run limit, at which its worker is killed.
    private async Task PassRunLimitAsync(string read)
    {
        await Machine.Data.WaitForAsync(change => change.Line == $"run accounting outpost-read {read}");
        await TestTimes.PassExactlyAsync(Machine.Clock, MetadataMachine.RunLimit, read);
    }
}
