using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport.Metadata;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// One run of metadata on a test's machine, as its output and the folder of its report show it (the S10 brief, "metadata
// <BASE> <FILE>"): how it ended, the rows of its table, and the report, which a run writes whole or not at all, never
// leaving its temporary file: Success with the line Wrote the metadata report …, the folder holding the report alone;
// any other end with its line {Kind} ({code}): {reason} and no file.
internal sealed class MetadataRun
{
    public MetadataRun(CommandAppResult result, string baseKey, string file)
    {
        Result = result;
        Base = baseKey;
        File = file;
    }

    public CommandAppResult Result { get; }

    public string Base { get; }

    // The full path of the report.
    public string File { get; }

    public int ExitCode => Result.ExitCode;

    public string Output => Result.Output;

    // The lines of the output, each without the spaces at its end.
    public IReadOnlyList<string> Lines => [.. TestMachine.LinesOf(Result).Select(line => line.TrimEnd())];

    public IReadOnlyList<MetadataCategory> Rows => MetadataTable.Rows(Output);

    // The report that the run wrote.
    public MetadataReport Report
    {
        get
        {
            Assert.True(System.IO.File.Exists(File), $"The run wrote no report; its output:\n{Output}");

            return MetadataReport.Read(File);
        }
    }

    // The run ended with Success, saying how many objects of how many found it read and how many failed, and its folder
    // holds the report alone, no temporary file beside it.
    public void AssertSucceeded(int read, int found, int failed)
    {
        Assert.True(ExitCode == ConsoleExitCodes.Success, $"The run ended with {ExitCode}, not Success; its output:\n{Output}");
        Assert.Contains(
            $"Wrote the metadata report of the base {Base} to {File}: {read} of {found} objects read, {failed} failed.",
            Lines);
        Assert.Equal([File], Directory.GetFileSystemEntries(Path.GetDirectoryName(File)!));
    }

    // The run ended with the code and its line, and wrote nothing: neither the report nor a temporary file.
    public void AssertEnded(int code, string line)
    {
        Assert.True(ExitCode == code, $"The run ended with {ExitCode}, not {code}; its output:\n{Output}");
        Assert.Contains(line, Lines);
        AssertNoFile();
    }

    // The folder of the report holds nothing.
    public void AssertNoFile()
    {
        Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(File)!));
    }

    // The JSON of the report holds the members that the test gives, in any order.
    public static void AssertJson(JsonNode expected, JsonElement actual)
    {
        JsonNode? read = JsonNode.Parse(actual.GetRawText());

        Assert.True(
            JsonNode.DeepEquals(expected, read),
            $"Expected\n{expected.ToJsonString()}\nreceived\n{actual.GetRawText()}");
    }

    // The reads of the report are those given, in their order, each with the bytes of the configuration's answer to it:
    // 0 for one that overran, and for one never run, which comes after every Read that the configuration answered.
    public void AssertReads(
        FakeConfiguration configuration,
        params (string Read, string? Category, int? From, int? Count, string Kind, string? Code)[] expected)
    {
        IReadOnlyList<MetadataRead> reads = Report.Reads;

        Assert.True(
            reads.Count == expected.Length,
            $"The report holds {reads.Count} reads, not {expected.Length}:\n{string.Join("\n", reads)}");

        for (int index = 0; index < expected.Length; index++)
        {
            long size = index < configuration.Answers.Count ? configuration.Answers[index].Outcome?.Length ?? 0 : 0;
            MetadataRead read = reads[index];

            Assert.Equal(
                (expected[index].Read, expected[index].Category, expected[index].From, expected[index].Count, size, expected[index].Kind, expected[index].Code),
                (read.Read, read.Category, read.From, read.Count, read.Size, read.Kind, read.Code));
        }
    }
}
