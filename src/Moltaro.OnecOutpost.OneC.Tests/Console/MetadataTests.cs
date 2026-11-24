using System.Runtime.Versioning;
using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Agent;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Metadata;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// The acceptance of metadata on the lab (the S10 brief, "Lab tests" of "Internal of S10c", and Acceptance): metadata
// writes a report of the test base and of each copy of a typical base of lab.local.json, through the console's own
// command on the machine's parts, into a temporary folder, with no poll. Each ends with Success, every object of every
// category read and none failed, each error named otherwise. The test base in safe mode holds the objects of the test
// configuration with their fields: Document.GoodsReceipt, whose Product is a Catalog.Products, whose Quantity has 15
// digits and 3 of them fractional and whose Comment has a length of 200; AccumulationRegister.Stock, with the dimension
// Product and the resource Quantity; and Catalog.Products, hierarchical. Each copy of a typical base, in safe mode or
// not as its writeSafeMode says, holds the object of its documentType with its fields; nothing of a copy is a literal
// here. With the storage available to the read account, an object holds its tables, and the storage names of the
// report, of its objects and its common storage, are as a set those of one GetDBStorageStructureInfo(Undefined, True)
// by the lab's own worker; without it, the report says why, which the lab run records, and this test then states the
// one that the lab found. Each run records its coverage, its reads, its largest outcome, its halvings, its storage
// and the language of the columns of the storage, for the brief, in the output that a passing test shows. A copy's
// census reads values without an XML form too (the S10 brief, "After the lab run of deeb370", 1). The assertions
// locate the copy's document type by name. Messages identify the copy by its index and give counts and outcome codes.
[SupportedOSPlatform("windows")]
public sealed class MetadataTests
{
    [Fact]
    public async Task On_the_test_base_in_safe_mode_the_report_holds_every_object_of_the_test_configuration_with_its_fields_and_its_storage()
    {
        using LabAgent agent = LabAgent.Create();
        using TemporaryFolder folder = new();

        LabMetadata metadata = await LabConsole.MetadataAsync(agent, folder.PathOf("test.json"));

        const string About = "the test base";
        metadata.AssertEveryObjectRead(About);
        MetadataReport report = metadata.Report;

        JsonElement receipt = report.Object("Document.GoodsReceipt");
        Assert.Equal(["Catalog.Products"], LabMetadata.TypesOf(MetadataReport.FieldOf(receipt, "Attributes", "Product")));
        JsonElement quantity = MetadataReport.FieldOf(receipt, "Attributes", "Quantity").GetProperty("number");
        Assert.Equal((15, 3), (quantity.GetProperty("digits").GetInt32(), quantity.GetProperty("fractionDigits").GetInt32()));
        Assert.Equal(200, MetadataReport.FieldOf(receipt, "Attributes", "Comment").GetProperty("string").GetProperty("length").GetInt32());

        JsonElement stock = report.Object("AccumulationRegister.Stock");
        Assert.Equal(["Catalog.Products"], LabMetadata.TypesOf(MetadataReport.FieldOf(stock, "Dimensions", "Product")));
        Assert.Equal(["Number"], LabMetadata.TypesOf(MetadataReport.FieldOf(stock, "Resources", "Quantity")));

        Assert.True(report.Object("Catalog.Products").GetProperty("properties").GetProperty("hierarchical").GetBoolean());

        await AssertStorageAsync(About, report, receipt, metadata.Named, LabWorkers.TestBaseReadAsync);
        metadata.Record(About);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task On_each_copy_of_a_typical_base_the_report_holds_every_object_and_that_of_its_document_type_with_its_fields_and_its_storage()
    {
        LabFile lab = LabFile.Read();

        Assert.True(lab.TypicalBases.Count > 0, "lab.local.json names no copy of a typical base.");

        for (int index = 0; index < lab.TypicalBases.Count; index++)
        {
            LabTypicalBase typicalBase = lab.TypicalBases[index];
            string about = $"typicalBases[{index}]";

            using LabAgent agent = typicalBase.WriteSafeMode
                ? LabAgent.InSafeMode(typicalBase)
                : LabAgent.WithoutSafeMode(typicalBase, LabAgent.WorkerAccountOf(lab));
            using TemporaryFolder folder = new();

            LabMetadata metadata = await LabConsole.MetadataAsync(agent, folder.PathOf("typical.json"));

            metadata.AssertEveryObjectRead(about);
            MetadataReport report = metadata.Report;
            string fullName = $"Document.{typicalBase.DocumentType}";
            List<JsonElement> matching = [.. report.Objects.Where(item => MetadataReport.TextOf(item, "fullName") == fullName)];
            Assert.True(matching.Count == 1, $"{about}: the report holds {matching.Count} objects of the configured document type, expected one.");

            JsonElement document = matching[0];
            Assert.True(document.GetProperty("fields").GetArrayLength() > 0, $"{about}: the configured document type has no field.");

            await AssertStorageAsync(about, report, document, metadata.Named, () => LabWorkers.TypicalBaseReadAsync(typicalBase));
            metadata.Record(about);
        }
    }

    // With the storage available, the object holds its tables, and the report holds every storage name of the base that
    // the lab's own worker reads, and no other; without it, the report says why. Either way the run records it, and the
    // language of the columns that the worker found. A message names storage names only when named is true; for a copy
    // it counts them.
    private static async Task AssertStorageAsync(
        string about,
        MetadataReport report,
        JsonElement item,
        bool named,
        Func<Task<WorkerProcess>> start)
    {
        if (!report.StorageAvailable)
        {
            Assert.False(string.IsNullOrWhiteSpace(report.StorageReason), $"{about}: the storage is unavailable without a reason.");

            return;
        }

        string subject = named ? MetadataReport.TextOf(item, "fullName")! : "the configured document type";
        JsonElement tables = item.GetProperty("storage");
        Assert.True(tables.GetArrayLength() > 0, $"{about}: {subject} has no table.");

        foreach (JsonElement table in tables.EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(table.GetProperty("storageName").GetString()), $"{about}: a table of {subject} has no storage name.");
            Assert.True(table.GetProperty("fields").GetArrayLength() > 0, $"{about}: a table of {subject} has no field.");
        }

        await using WorkerProcess worker = await start();
        (bool english, IReadOnlySet<string> names) = await LabStorage.ReadAsync(worker);
        IReadOnlySet<string> reported = report.StorageNames();

        string[] onlyReported = [.. reported.Except(names)];
        string[] onlyInTheBase = [.. names.Except(reported)];
        string differ = named
            ? $"only in the report {string.Join(", ", onlyReported.Take(10))}; only in the base {string.Join(", ", onlyInTheBase.Take(10))}"
            : $"{onlyReported.Length} only in the report, {onlyInTheBase.Length} only in the base";

        Assert.True(reported.SetEquals(names), $"{about}: the storage names of the report differ from those of the base: {differ}");
        TestContext.Current.TestOutputHelper?.WriteLine($"{about}: {names.Count} storage tables, their columns in {(english ? "English" : "Russian")}");
    }
}
