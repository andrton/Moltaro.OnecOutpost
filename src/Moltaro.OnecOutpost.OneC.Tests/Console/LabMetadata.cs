using System.Globalization;
using System.Text.Json;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.TestSupport.Metadata;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// One run of the console's metadata on the lab, as its output and its report show it: its exit code, the report that it
// wrote, and what the lab run records of it for the brief of S10c, in the output of the test, which a passing test
// shows too: the coverage of each category, the reads, the largest outcome, the halvings, and whether the storage
// structure was available to the read account, or why not. The report of a copy of a typical base names objects of its
// configuration, which no message of a test and nothing it records names: for a copy, Named is false, and the messages
// and the records give counts and the codes of the protocol alone; for the test base, whose configuration is the
// repository's, they name the objects.
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

    // Every object of every category was read: none failed, and read equals found in each category; otherwise the
    // assertion names each error, of an object and of a Read, or, for a copy, counts them with the codes of the Reads.
    public void AssertEveryObjectRead(string about)
    {
        MetadataReport report = Report;
        List<JsonElement> failedObjects = [.. report.Objects.Where(item => MetadataReport.TextOf(item, "error") is not null)];
        List<MetadataRead> failedReads = [.. report.Reads.Where(read => read.Kind != "succeeded")];
        string errors = Named
            ? string.Join(
                "\n",
                failedObjects.Select(item => $"{MetadataReport.TextOf(item, "fullName")}: {MetadataReport.TextOf(item, "error")}")
                    .Concat(failedReads.Select(read => $"{read.Read} {read.Category} {read.From} {read.Count}: {read.Kind} {read.Code} {read.ErrorText}")))
            : $"{failedObjects.Count} objects with an error; {failedReads.Count} Reads that did not succeed, by code: "
                + string.Join(", ", failedReads.GroupBy(read => read.Code ?? "none").Select(code => $"{code.Key} {code.Count()}"));

        Assert.True(
            report.Categories.All(category => category.Failed == 0 && category.Read == category.Found),
            $"{about}: not every object was read: {string.Join("; ", report.Categories)}\n{errors}");
        Assert.True(failedObjects.Count == 0 && failedReads.Count == 0, $"{about}: the walk had errors:\n{errors}");
    }

    // Writes what the lab run records into the output of the test: counts, sizes and the storage's availability, and
    // for the test base the platform and the configuration of the report.
    public void Record(string about)
    {
        MetadataReport report = Report;
        IReadOnlyList<MetadataRead> reads = report.Reads;
        int halvings = reads.Count(read => read.Code is "result-too-large" or "run-limit");
        string storage = report.StorageAvailable
            ? $"available, {report.StorageNames().Count} storage names"
            : $"unavailable: {report.StorageReason}";
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
}
