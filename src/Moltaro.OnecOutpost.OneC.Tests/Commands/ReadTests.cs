using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Commands;

// A Read runs its script under the read account of the base, outside a transaction, and is read-only only through the
// rights of that account (concept §3, "The two commands").
public sealed class ReadTests
{
    private const string CreateTwoProducts = """
        For Number = 1 To 2 Do
            Product = Catalogs.Products.CreateItem();
            Product.Description = Parameters["description"];
            Product.Write();
        EndDo;
        """;

    private const string ProductsWithTheDescription = """
        Query = New Query;
        Query.Text = "SELECT Products.Code AS Code, Products.Description AS Description FROM Catalog.Products AS Products WHERE Products.Description = &Description";
        Query.SetParameter("Description", Parameters["description"]);
        Selection = Query.Execute().Select();
        Rows = New Array;
        While Selection.Next() Do
            Rows.Add(New Structure("Code, Description", Selection.Code, Selection.Description));
        EndDo;
        Result = Rows;
        """;

    // The rows are those a Write of the test created a moment before.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_read_query_returns_rows()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            (await writer.RunAsync(CreateTwoProducts, parameters)).AssertIs("succeeded", null);
        }

        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();
        OutcomeLine read = await reader.RunAsync(ProductsWithTheDescription, parameters);

        read.AssertIs("succeeded", null);
        Assert.Equal(2, read.Result.GetArrayLength());
        Assert.All(read.Result.EnumerateArray(), row => Assert.False(string.IsNullOrEmpty(row.GetProperty("Code").GetString())));
        Assert.NotEqual(read.Result[0].GetProperty("Code").GetString(), read.Result[1].GetProperty("Code").GetString());
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_read_account_cannot_write()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine write = await reader.RunAsync(TestBaseScripts.CreateProduct, parameters);
        OutcomeLine count = await reader.RunAsync(TestBaseScripts.CountProducts, parameters);

        write.AssertIs("failed", "script-error");
        count.AssertIs("succeeded", null);
        Assert.Equal(0, count.Result.GetInt32());
    }
}
