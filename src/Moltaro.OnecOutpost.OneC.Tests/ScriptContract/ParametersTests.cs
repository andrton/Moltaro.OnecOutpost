using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.ScriptContract;

// Parameters holds the parameters, read from a JSON object into a Map with string keys, and an empty Map without
// parameters. An object becomes a Map, an array an Array, a string, number or boolean the matching 1C value, null
// Undefined, and a string is never turned into a date by guessing (concept §3, "The two commands").
public sealed class ParametersTests
{
    private const string Parameters = """
        {"object":{"key":"value"},"array":[1,"two",null],"null":null,"date":"2026-11-21T10:05:07","number":12.5,"boolean":true,"string":"text"}
        """;

    // Each check is named, so that a failure says which mapping broke. And in 1C stops at its first false operand, so
    // that the check of a value never runs on a value of another type.
    private const string CheckTheParameters = """
        Checks = New Map;
        Checks.Insert("Parameters is a Map", TypeOf(Parameters) = Type("Map"));
        Checks.Insert("every member is in it", Parameters.Count() = 7);
        Checks.Insert("an object is a Map", TypeOf(Parameters["object"]) = Type("Map") And Parameters["object"]["key"] = "value");
        Checks.Insert("an array is an Array", TypeOf(Parameters["array"]) = Type("Array") And Parameters["array"].Count() = 3);
        Checks.Insert("a number in an array is a Number", Parameters["array"][0] = 1);
        Checks.Insert("a string in an array is a String", Parameters["array"][1] = "two");
        Checks.Insert("null in an array is Undefined", Parameters["array"][2] = Undefined);
        Checks.Insert("null is Undefined", Parameters["null"] = Undefined);
        Checks.Insert("a string that looks like a date stays a string", TypeOf(Parameters["date"]) = Type("String") And Parameters["date"] = "2026-11-21T10:05:07");
        Checks.Insert("a number is a Number", TypeOf(Parameters["number"]) = Type("Number") And Parameters["number"] = 12.5);
        Checks.Insert("a boolean is a Boolean", TypeOf(Parameters["boolean"]) = Type("Boolean") And Parameters["boolean"] = True);
        Checks.Insert("a string is a String", TypeOf(Parameters["string"]) = Type("String") And Parameters["string"] = "text");
        Result = Checks;
        """;

    [Fact]
    public async Task Parameters_maps_JSON_as_the_concept_says()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(CheckTheParameters, Parameters);

        read.AssertIs("succeeded", null);
        JsonProperty[] checks = [.. read.Result.EnumerateObject()];
        Assert.Equal(12, checks.Length);
        Assert.All(checks, check => Assert.True(check.Value.GetBoolean(), check.Name));
    }

    [Fact]
    public async Task No_parameters_are_an_empty_Map()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync("Result = TypeOf(Parameters) = Type(\"Map\") And Parameters.Count() = 0;", "{}");

        read.AssertIs("succeeded", null);
        Assert.True(read.Result.GetBoolean());
    }
}
