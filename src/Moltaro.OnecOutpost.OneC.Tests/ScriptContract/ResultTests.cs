using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.ScriptContract;

// Result is set by the script and written as JSON: a date as YYYY-MM-DDThh:mm:ss without an offset, Undefined and Null
// as null, and any other value, such as a reference, fails the command with result-type (concept §3, "The two
// commands"). The executor replaces Null with Undefined before it writes, since WriteJSON refuses Null (concept §12, O4).
public sealed class ResultTests
{
    // A script, and the JSON of the result it gives.
    public static TheoryData<string, string> ResultsWrittenAsNull => new()
    {
        { "Result = Undefined;", "null" },
        { "Result = Null;", "null" },
        { "Result = New Array;\nResult.Add(Undefined);\nResult.Add(Null);\nResult.Add(New Structure(\"Value\", Null));", "[null,null,{\"Value\":null}]" },
        { "Value = 1;", "null" },
    };

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_date_in_the_result_is_written_without_an_offset()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Result = Date(2026, 11, 21, 10, 5, 7);");

        read.AssertIs("succeeded", null);
        Assert.Equal("2026-11-21T10:05:07", read.Result.GetString());
    }

    // A script that sets no result leaves it Undefined.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(ResultsWrittenAsNull))]
    public async Task Undefined_and_Null_in_the_result_are_written_as_null(string script, string result)
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(script);

        read.AssertIs("succeeded", null);
        using JsonDocument expected = JsonDocument.Parse(result);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, read.Result), read.ToString());
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_reference_in_the_result_of_a_read_fails_with_result_type()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Result = Catalogs.Products.EmptyRef();");

        read.AssertIs("failed", "result-type");
    }
}
