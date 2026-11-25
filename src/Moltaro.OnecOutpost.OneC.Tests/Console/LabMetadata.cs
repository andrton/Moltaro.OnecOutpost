using System.Globalization;
using System.Text.Json;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport.Metadata;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// One run of the console's metadata in the lab, with its exit code and report. The test output records each
// category's coverage, the Reads, the largest outcome, the page halvings and whether the read account could read the
// storage structure. For a copy of a typical base, Named is false, and the messages give
// counts and outcome codes. For the test base, whose configuration is the repository's, they name failed objects and
// give the details of failed Reads and unavailable storage.
internal sealed class LabMetadata
{
    public LabMetadata(CommandAppResult result, string file, bool named)
    {
        Result = result;
        File = file;
        Named = named;
    }

    public CommandAppResult Result { get; }

    public string File { get; }

    // Whether a message may name an object of the base and quote the output of metadata.
    public bool Named { get; }

    public int ExitCode => Result.ExitCode;

    // The report of the run, which ended with Success.
    public MetadataReport Report
    {
        get
        {
            string output = Named ? $"; its output:\n{Result.Output}" : "";

            Assert.True(
                ExitCode == ConsoleExitCodes.Success && System.IO.File.Exists(File),
                $"metadata ended with {ExitCode} and wrote {(System.IO.File.Exists(File) ? "a report" : "no report")}{output}");

            return MetadataReport.Read(File);
        }
    }

    // Every object of every category was read and recorded once, and no terminal Read or object failed. A page the
    // walk halves is an intermediate Read. For a copy, report only counts and codes when an assertion fails.
    public void AssertEveryObjectRead(string about)
    {
        MetadataReport report = Report;
        IReadOnlyList<MetadataCategory> categories = report.Categories;
        IReadOnlyList<JsonElement> objects = report.Objects;
        List<JsonElement> failedObjects = [.. objects.Where(item => MetadataReport.TextOf(item, "error") is not null)];
        List<MetadataRead> failedReads = [.. report.Reads.Where(read => read.Kind != "succeeded" && !IsHalvedPage(read))];
        string errors = Named
            ? string.Join(
                "\n",
                failedObjects.Select(item => $"{MetadataReport.TextOf(item, "fullName")}: {MetadataReport.TextOf(item, "error")}")
                    .Concat(failedReads.Select(read => $"{read.Read} {read.Category} {read.From} {read.Count}: {read.Kind} {read.Code} {read.ErrorText}")))
            : $"{failedObjects.Count} objects with an error; {failedReads.Count} terminal Reads that did not succeed, by code: "
                + string.Join(", ", failedReads.GroupBy(read => read.Code ?? "none").Select(code => $"{code.Key} {code.Count()}"));

        Assert.True(
            categories.All(category => category.Failed == 0 && category.Read == category.Found),
            $"{about}: not every object was read: {string.Join("; ", categories)}\n{errors}");
        Assert.True(failedObjects.Count == 0 && failedReads.Count == 0, $"{about}: the walk had terminal errors:\n{errors}");

        ILookup<string?, string?> namesByCategory = objects.ToLookup(
            item => MetadataReport.TextOf(item, "category"),
            item => MetadataReport.TextOf(item, "fullName"));

        int expected = categories.Sum(category => category.Found);
        Assert.True(objects.Count == expected, $"{about}: the report holds {objects.Count} objects, expected {expected}.");

        foreach (MetadataCategory category in categories)
        {
            int names = namesByCategory[category.Category].Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).Count();
            Assert.True(names == category.Found, $"{about}: {category.Category} holds {names} distinct named objects, expected {category.Found}.");
        }
    }

    // Writes what the lab run records into the output of the test: counts, sizes and the storage's availability, and
    // for the test base the platform and the configuration of the report.
    public void Record(string about)
    {
        MetadataReport report = Report;
        IReadOnlyList<MetadataRead> reads = report.Reads;
        int halvings = reads.Count(IsHalvedPage);
        string storage = report.StorageAvailable ? $"available, {report.StorageNames().Count} storage names" : "unavailable";

        if (Named && !report.StorageAvailable)
        {
            storage += $": {report.StorageReason}";
        }

        string coverage = string.Join(
            ", ",
            report.Categories.Where(category => category.Found > 0).Select(category => $"{category.Category} {category.Read} of {category.Found}"));
        string census = Named
            ? $"; the platform {report.Root.GetProperty("platform").GetRawText()}, the configuration {report.Root.GetProperty("configuration").GetRawText()}"
            : "";

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{about}: read {coverage}; {reads.Count} reads, the largest outcome {reads.Max(read => read.Size)} bytes, {halvings} halvings; the storage {storage}; the report {new FileInfo(File).Length} bytes{census}"));
    }

    // The field of the object, which names a type of each of its types.
    public static IReadOnlyList<string> TypesOf(JsonElement field)
    {
        return [.. field.GetProperty("types").EnumerateArray().Select(type => type.GetString()!)];
    }

    // These failures split into two Reads. The same codes on a single object or another Read are terminal.
    private static bool IsHalvedPage(MetadataRead read)
    {
        return read.Read == "page" && read.Kind == "failed" && read.Count is > 1
            && read.Code is OutpostOutcomeCodes.ResultTooLarge or OutpostOutcomeCodes.RunLimit;
    }
}
