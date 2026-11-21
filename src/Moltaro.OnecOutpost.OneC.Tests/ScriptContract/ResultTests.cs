using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.ScriptContract;

// Result is set by the script and written as JSON: a date as YYYY-MM-DDThh:mm:ss without an offset, Undefined and Null
// as null, and any other value, such as a reference, fails the command with result-type (concept §3, "The two
// commands"). The executor replaces Null with Undefined before it writes, since WriteJSON refuses Null (concept §12, O4).
// A result that holds U+0000 or a lone surrogate, or nests beyond 64 levels, fails with result-type too (§8 of
// docs/protocol.md).
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

    public static TheoryData<string> ResultsThatFailTheTypeCheck =>
    [
        ResultStatements.HoldingU0000,
        ResultStatements.HoldingALoneSurrogate,
        ResultStatements.NestedArrays(65),
    ];

    [Fact]
    public async Task A_date_in_the_result_is_written_without_an_offset()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Result = Date(2026, 11, 21, 10, 5, 7);");

        read.AssertIs("succeeded", null);
        Assert.Equal("2026-11-21T10:05:07", read.Result.GetString());
    }

    // A script that sets no result leaves it Undefined.
    [Theory]
    [MemberData(nameof(ResultsWrittenAsNull))]
    public async Task Undefined_and_Null_in_the_result_are_written_as_null(string script, string result)
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(script);

        read.AssertIs("succeeded", null);
        using JsonDocument expected = JsonDocument.Parse(result);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, read.Result), read.ToString());
    }

    [Fact]
    public async Task A_reference_in_the_result_of_a_read_fails_with_result_type()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Result = Catalogs.Products.EmptyRef();");

        read.AssertIs("failed", "result-type");
    }

    // WriteTests show that the same results roll a Write back before its commit.
    [Theory]
    [MemberData(nameof(ResultsThatFailTheTypeCheck))]
    public async Task A_result_holding_U0000_or_a_lone_surrogate_or_nesting_65_levels_fails_with_result_type(string script)
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(script);

        read.AssertIs("failed", "result-type");
    }

    [Fact]
    public async Task A_result_of_64_levels_is_written()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(ResultStatements.NestedArrays(64));

        read.AssertIs("succeeded", null);
        Assert.Equal(64, Depth(read.Result));
    }

    // The level of a value, an object or an array at the top being level 1 and a scalar level 0.
    private static int Depth(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => 1 + value.EnumerateArray().Select(Depth).DefaultIfEmpty(0).Max(),
            JsonValueKind.Object => 1 + value.EnumerateObject().Select(each => Depth(each.Value)).DefaultIfEmpty(0).Max(),
            _ => 0,
        };
    }
}
