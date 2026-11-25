using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Metadata;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The storage structure in the report of metadata: a storage structure that the read account may not read is recorded
// as unavailable with its reason, the
// objects and their fields kept. The probe's storageError alone makes it unavailable: the pages then run without it,
// common is not read, and no error of an object is set by it. A later page's storageError is written into the error of
// each object of that page that has none, as storage: {storageError}, which makes them failed. A common storage that
// fails is recorded in reads and leaves commonStorage null. Each of these runs ends with Success.
public sealed class MetadataStorageTests : IDisposable
{
    private const string StorageError = "Insufficient rights to read the storage structure";

    private readonly MetadataMachine _metadata = new();

    private FakeConfiguration Accounting => _metadata.Accounting;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    [Fact]
    public async Task A_storage_error_of_the_probe_makes_the_storage_unavailable_with_its_reason_and_the_pages_run_without_it()
    {
        Accounting.StorageFails("Catalog.Products", StorageError);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        Assert.Contains(
            $"The storage structure could not be read: {StorageError}; the report holds the objects and their fields without it.",
            run.Lines);
        MetadataReport report = run.Report;
        Assert.False(report.StorageAvailable);
        Assert.Equal(StorageError, report.StorageReason);
        Assert.Equal(6, report.Objects.Count);
        Assert.All(report.Objects, item =>
        {
            string fullName = item.GetProperty("fullName").GetString()!;
            JsonObject paged = Accounting.Paged(fullName, storage: false);
            paged["category"] = MetadataReport.TextOf(item, "category");

            MetadataRun.AssertJson(paged, item);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("storage").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("error").ValueKind);
        });
        Assert.Null(report.CommonStorage);
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
        Assert.DoesNotContain(report.Reads, read => read.Read == "common");
    }

    // An object whose own error came first keeps it; the probe found the storage available, and it stays so.
    [Fact]
    public async Task A_storage_error_of_a_later_page_is_the_error_of_each_of_its_objects_without_one_which_then_fail()
    {
        Accounting.Add("Documents", "Document.Broken", "Broken")["error"] = "The object cannot be read";
        Accounting.StorageFails("Document.GoodsReceipt", StorageError);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 5, found: 7, failed: 2);
        MetadataReport report = run.Report;
        Assert.True(report.StorageAvailable);
        Assert.Equal($"storage: {StorageError}", MetadataReport.TextOf(report.Object("Document.GoodsReceipt"), "error"));
        Assert.Equal("The object cannot be read", MetadataReport.TextOf(report.Object("Document.Broken"), "error"));
        Assert.Equal(new MetadataCategory("Documents", 2, 0, 2), report.Category("Documents"));
        Assert.Null(MetadataReport.TextOf(report.Object("Catalog.Products"), "error"));
        Assert.Equal(JsonValueKind.Array, report.Object("Catalog.Products").GetProperty("storage").ValueKind);
        Assert.NotNull(report.CommonStorage);
    }

    [Fact]
    public async Task The_common_read_recovers_unseen_tables_for_objects_that_have_storage_and_keeps_common_storage_separate()
    {
        const string Owner = "Документ.StorageSample";
        const string FailedOwner = "AccumulationRegister.Stock";
        const string RecoveredName = "_OutpostNestedStorage";
        Accounting.Add("Documents", Owner, "Storage sample");
        Accounting.StorageFails(FailedOwner, StorageError);

        JsonObject paged = (JsonObject)Accounting.Paged(Owner, storage: true)["storage"]![0]!;
        string pagedName = paged["storageName"]!.GetValue<string>();
        JsonObject recovered = (JsonObject)paged.DeepClone();
        recovered["storageName"] = RecoveredName;
        recovered["metadata"] = $"{Owner}.TabularSection.Items";
        Accounting.AddGlobalOnlyStorage(Owner, recovered);

        JsonObject unavailable = (JsonObject)recovered.DeepClone();
        unavailable["storageName"] = "_OutpostUnavailableStorage";
        unavailable["metadata"] = FailedOwner;
        Accounting.AddGlobalOnlyStorage(FailedOwner, unavailable);

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 7, failed: 1);
        MetadataReport report = run.Report;
        Assert.True(report.StorageAvailable);
        JsonElement storage = report.Object(Owner).GetProperty("storage");
        Assert.Equal(2, storage.GetArrayLength());
        MetadataRun.AssertJson(paged, Assert.Single(storage.EnumerateArray(), table => MetadataReport.TextOf(table, "storageName") == pagedName));
        MetadataRun.AssertJson(recovered, Assert.Single(storage.EnumerateArray(), table => MetadataReport.TextOf(table, "storageName") == RecoveredName));
        MetadataRun.AssertJson(Accounting.CommonStorage, report.CommonStorage!.Value);

        JsonElement failed = report.Object(FailedOwner);
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("storage").ValueKind);
        Assert.Equal($"storage: {StorageError}", MetadataReport.TextOf(failed, "error"));

        string commonParameters = Assert.Single(
            Assert.Single(_metadata.Machine.Workers.Processes).Commands,
            command => FakeConfiguration.Describe(command.ParametersJson) == "common").ParametersJson;
        using JsonDocument parameters = JsonDocument.Parse(commonParameters);
        string[] expectedOwners = ["Catalog.Products", "Catalog.Units", "Document.GoodsReceipt", Owner, "Enum.Statuses", "Constant.Currency"];
        IEnumerable<string> expectedNames = expectedOwners.Select(owner =>
            Accounting.Paged(owner, storage: true)["storage"]![0]!["storageName"]!.GetValue<string>());

        Assert.Equal(
            expectedOwners.Order(StringComparer.Ordinal),
            parameters.RootElement.GetProperty("owners").EnumerateArray().Select(owner => owner.GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedNames.Order(StringComparer.Ordinal),
            parameters.RootElement.GetProperty("seenStorageNames").EnumerateArray().Select(name => name.GetString()).Order(StringComparer.Ordinal));

        IEnumerable<string?> reportedNames = report.Objects
            .Select(item => item.GetProperty("storage"))
            .Where(tables => tables.ValueKind == JsonValueKind.Array)
            .SelectMany(tables => tables.EnumerateArray())
            .Select(table => MetadataReport.TextOf(table, "storageName"));
        Assert.Equal(expectedNames.Append(RecoveredName).Order(StringComparer.Ordinal), reportedNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_common_storage_that_fails_is_recorded_in_the_reads_and_leaves_the_common_storage_null()
    {
        Accounting.CommonFails("The fake common storage refused the call");

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        MetadataReport report = run.Report;
        Assert.True(report.StorageAvailable);
        Assert.Null(report.CommonStorage);
        MetadataRead common = report.Reads[^1];
        Assert.Equal(
            ("common", "failed", "script-error", "The fake common storage refused the call"),
            (common.Read, common.Kind, common.Code, common.ErrorText));
    }
}
