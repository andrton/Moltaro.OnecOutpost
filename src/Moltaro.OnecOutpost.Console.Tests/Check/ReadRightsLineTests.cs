using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line ReadRights of check (the S10b brief, "check", and the stage contract), for each base: the rights to change
// data that its read account holds, as the read account's third script gives them, the global rights over the
// configuration and, for each category of objects and each right, how many objects and their full names. A read
// account holds none, so each is named: the global rights, each right of a category with its count, and the first ten
// full names, then how many more.
public sealed class ReadRightsLineTests : IDisposable
{
    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_read_account_that_holds_rights_to_change_data_fails_the_line_naming_them_and_the_first_ten_objects()
    {
        string[] catalogs = [.. Enumerable.Range(1, 12).Select(number => $"Catalog.Item{number:D2}")];
        Machine.Workers.Sessions[("warehouse", "outpost-read")].Gives(
            FakeCheckProcess.RightsScript,
            Rights(["DataAdministration"], Category("catalogs", "Insert", catalogs)));

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            $"outpost-read holds rights to change data: DataAdministration; Insert on 12 catalogs: {string.Join(", ", catalogs[..10])} and 2 more",
            run.Failed(CheckLineEnum.ReadRights, "warehouse"));
        run.Passed(CheckLineEnum.ReadRights, "accounting");
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Every_global_right_and_every_right_of_each_category_is_named()
    {
        Machine.Workers.Sessions[("warehouse", "outpost-read")].Gives(
            FakeCheckProcess.RightsScript,
            Rights(
                ["Administration", "UpdateDataBaseConfiguration"],
                Category("catalogs", "Delete", ["Catalog.Products"]),
                Category("constants", "Update", ["Constant.Currency"])));

        CheckRun run = await _checked.RunAsync();

        string reason = run.Failed(CheckLineEnum.ReadRights, "warehouse");
        Assert.StartsWith("outpost-read holds rights to change data: ", reason, StringComparison.Ordinal);
        Assert.Contains("Administration", reason, StringComparison.Ordinal);
        Assert.Contains("UpdateDataBaseConfiguration", reason, StringComparison.Ordinal);
        Assert.Contains("Delete on 1 catalogs", reason, StringComparison.Ordinal);
        Assert.Contains("Update on 1 constants", reason, StringComparison.Ordinal);
        Assert.Contains("Catalog.Products", reason, StringComparison.Ordinal);
        Assert.Contains("Constant.Currency", reason, StringComparison.Ordinal);
    }

    // The result of the script of the rights: the global rights, then each right of a category with its objects.
    private static JsonObject Rights(string[] global, params JsonObject[] categories)
    {
        return new JsonObject
        {
            ["global"] = new JsonArray([.. global.Select(right => JsonValue.Create(right))]),
            ["categories"] = new JsonArray(categories),
        };
    }

    private static JsonObject Category(string category, string right, string[] objects)
    {
        return new JsonObject
        {
            ["category"] = category,
            ["right"] = right,
            ["count"] = objects.Length,
            ["objects"] = new JsonArray([.. objects.Select(name => JsonValue.Create(name))]),
        };
    }
}
