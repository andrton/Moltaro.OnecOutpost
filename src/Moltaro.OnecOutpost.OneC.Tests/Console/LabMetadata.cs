using System.Globalization;
using System.Text.Json;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.TestSupport.Metadata;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// One run of the console's metadata on the lab, as its output and its report show it: its exit code, the report that it
// wrote, and what the lab run records of it for the brief of S10c, in the output of the test: the reads, the largest
// outcome, the halvings, and whether the storage structure was available to the read account, with a storage name it
// gave or the reason.
internal sealed class LabMetadata
{
    public LabMetadata(CommandAppResult result, string file)
    {
        Result = result;
        File = file;
    }

    public CommandAppResult Result { get; }

    public string File { get; }

    public int ExitCode => Result.ExitCode;

    // The report of the run, which ended with Success.
    public MetadataReport Report
    {
        get
        {
            Assert.True(
                ExitCode == ConsoleExitCodes.Success && System.IO.File.Exists(File),
                $"metadata ended with {ExitCode} and wrote {(System.IO.File.Exists(File) ? "a report" : "no report")}; its output:\n{Result.Output}");

            return MetadataReport.Read(File);
        }
    }

    // Every object of every category was read: none failed, and read equals found in each category; otherwise the
    // assertion names each error, of an object and of a Read.
    public void AssertEveryObjectRead(string about)
    {
        MetadataReport report = Report;
        IEnumerable<string> objectErrors = report.Objects
            .Where(item => MetadataReport.TextOf(item, "error") is not null)
            .Select(item => $"{MetadataReport.TextOf(item, "fullName")}: {MetadataReport.TextOf(item, "error")}");
        IEnumerable<string> readErrors = report.Reads
            .Where(read => read.Kind != "succeeded")
            .Select(read => $"{read.Read} {read.Category} {read.From} {read.Count}: {read.Kind} {read.Code} {read.ErrorText}");
        List<string> errors = [.. objectErrors, .. readErrors];

        Assert.True(
            report.Categories.All(category => category.Failed == 0 && category.Read == category.Found),
            $"{about}: not every object was read: {string.Join("; ", report.Categories)}\n{string.Join("\n", errors)}");
        Assert.True(errors.Count == 0, $"{about}: the walk had errors:\n{string.Join("\n", errors)}");
    }

    // Writes what the lab run records into the output of the test.
    public void Record(string about)
    {
        MetadataReport report = Report;
        IReadOnlyList<MetadataRead> reads = report.Reads;
        int halvings = reads.Count(read => read.Code is "result-too-large" or "run-limit");
        string storage = report.StorageAvailable
            ? $"available, such as {report.StorageNames().Order(StringComparer.Ordinal).FirstOrDefault()}"
            : $"unavailable: {report.StorageReason}";

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{about}: {report.Objects.Count} objects in {reads.Count} reads, the largest outcome {reads.Max(read => read.Size)} bytes, {halvings} halvings; the storage {storage}; the report {new FileInfo(File).Length} bytes, the platform {report.Root.GetProperty("platform").GetString()}, the configuration {report.Root.GetProperty("configuration").GetRawText()}"));
    }

    // The field of the object, which names a type of each of its types.
    public static IReadOnlyList<string> TypesOf(JsonElement field)
    {
        return [.. field.GetProperty("types").EnumerateArray().Select(type => type.GetString()!)];
    }
}
