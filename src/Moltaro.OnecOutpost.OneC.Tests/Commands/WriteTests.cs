using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.ScriptContract;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.Commands;

// A Write runs its script under the write account of the base, inside one 1C transaction: either everything the script
// did is committed, or nothing (concept §3, "The two commands"). The executor checks the type of the result and the
// size of the outcome before the commit, and a script that ends the transaction itself gets Unknown (concept rule 5).
// A script that leaves a transaction of its own open fails, since the executor rolls everything back. So does a script
// that begins and rolls back a transaction of its own: in the compatibility mode 8.2.13 that rolls the whole Write back
// without a raise, so the executor asks the base before it commits, and judges by the raise (the brief of S9, "The
// executor").
public sealed class WriteTests
{
    // Writes a product, begins a transaction inside the Write, writes another product and leaves that transaction open.
    private const string LeaveATransactionOpen =
        TestBaseScripts.CreateProduct + "\nBeginTransaction();\n" + TestBaseScripts.CreateProduct;

    // Writes a product, then begins and rolls back a transaction inside the Write. The lab showed that this leaves the
    // transaction active, its commit does not raise and nothing is written; only the next access to the base raises.
    private const string WriteAndRollBackANestedTransaction =
        TestBaseScripts.CreateProduct + "\nBeginTransaction();\nRollbackTransaction();";

    private const string CreateAndPostAGoodsReceipt = """
        Product = Catalogs.Products.CreateItem();
        Product.Description = Parameters["description"];
        Product.Write();
        Receipt = Documents.GoodsReceipt.CreateDocument();
        Receipt.Date = CurrentDate();
        Receipt.Product = Product.Ref;
        Receipt.Quantity = Parameters["quantity"];
        Receipt.Write(DocumentWriteMode.Posting);
        Result = String(Receipt.Ref.UUID());
        """;

    private const string TheGoodsReceiptAndItsStock = """
        Receipt = Documents.GoodsReceipt.GetRef(New UUID(Parameters["id"]));
        Query = New Query;
        Query.Text = "SELECT Stock.Product.Description AS Product, Stock.Quantity AS Quantity, Stock.RecordType AS RecordType FROM AccumulationRegister.Stock AS Stock WHERE Stock.Recorder = &Receipt";
        Query.SetParameter("Receipt", Receipt);
        Selection = Query.Execute().Select();
        Records = New Array;
        While Selection.Next() Do
            Records.Add(New Structure("Product, Quantity, Receipt", Selection.Product, Selection.Quantity, Selection.RecordType = AccumulationRecordType.Receipt));
        EndDo;
        Result = New Structure("Posted, Records", Receipt.Posted, Records);
        """;

    // The end of a script that wrote a product, and the code its Write fails with: a reference, U+0000, a lone
    // surrogate, a nesting of 65 levels, and a result beyond the limit of 1024 bytes.
    public static TheoryData<string, string> EndsThatFailTheCheckBeforeTheCommit => new()
    {
        { "Result = Product.Ref;", "result-type" },
        { ResultStatements.HoldingU0000, "result-type" },
        { ResultStatements.HoldingALoneSurrogate, "result-type" },
        { ResultStatements.NestedArrays(65), "result-type" },
        { "Result = \"\";\nFor Number = 1 To 2048 Do\n    Result = Result + \"x\";\nEndDo;", "result-too-large" },
    };

    [Fact]
    public async Task A_write_creates_and_posts_a_document_of_the_test_configuration()
    {
        string description = TestBaseScripts.NewDescription();
        JsonObject parameters = new() { ["description"] = description, ["quantity"] = 7.125m };

        string id;

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            OutcomeLine posted = await writer.RunAsync(CreateAndPostAGoodsReceipt, parameters.ToJsonString());

            posted.AssertIs("succeeded", null);
            id = posted.Result.GetString()!;
        }

        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();
        OutcomeLine read = await reader.RunAsync(TheGoodsReceiptAndItsStock, new JsonObject { ["id"] = id }.ToJsonString());

        read.AssertIs("succeeded", null);
        Assert.True(read.Result.GetProperty("Posted").GetBoolean());

        JsonElement record = Assert.Single(read.Result.GetProperty("Records").EnumerateArray());
        Assert.Equal(description, record.GetProperty("Product").GetString());
        Assert.Equal(7.125m, record.GetProperty("Quantity").GetDecimal());
        Assert.True(record.GetProperty("Receipt").GetBoolean());
    }

    [Fact]
    public async Task A_write_whose_script_raises_an_error_rolls_back()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();
        const string Script = TestBaseScripts.CreateProduct + "\nRaise \"The script stops after it wrote a product.\";";

        OutcomeLine write;

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            write = await writer.RunAsync(Script, parameters);
        }

        write.AssertIs("failed", "script-error");
        Assert.Contains("The script stops after it wrote a product.", write.ErrorText);
        Assert.Equal(0, await CountProductsAsync(parameters));
    }

    [Theory]
    [MemberData(nameof(EndsThatFailTheCheckBeforeTheCommit))]
    public async Task A_result_of_a_wrong_type_or_too_large_rolls_the_write_back_before_the_commit(string end, string code)
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();

        OutcomeLine write;

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            write = await writer.RunAsync(TestBaseScripts.CreateProduct + "\n" + end, parameters, outcomeLimit: 1024);
        }

        write.AssertIs("failed", code);
        Assert.Equal(0, await CountProductsAsync(parameters));
    }

    [Theory]
    [InlineData("CommitTransaction();")]
    [InlineData("RollbackTransaction();")]
    public async Task A_script_that_ends_the_transaction_itself_gives_unknown_transaction_ended(string script)
    {
        await using WorkerProcess writer = await LabWorkers.TestBaseWriteAsync();

        OutcomeLine write = await writer.RunAsync(script);

        write.AssertIs("unknown", "transaction-ended");
    }

    // 1C raised nothing, so the error text is a sentence of the executor, whose wording is the executor's.
    [Fact]
    public async Task A_write_whose_script_leaves_a_transaction_open_fails_with_script_error_and_rolls_back()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();

        OutcomeLine write;

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            write = await writer.RunAsync(LeaveATransactionOpen, parameters);
        }

        write.AssertIs("failed", "script-error");
        Assert.False(string.IsNullOrWhiteSpace(write.ErrorText), write.ToString());
        Assert.Equal(0, await CountProductsAsync(parameters));
    }

    // 1C raised nothing in the script, so the error text is a sentence of the executor, whose wording is the executor's.
    [Fact]
    public async Task A_write_whose_script_rolls_back_a_nested_transaction_after_it_wrote_an_item_fails_with_script_error_and_writes_nothing()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();

        OutcomeLine write;

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            write = await writer.RunAsync(WriteAndRollBackANestedTransaction, parameters);
        }

        write.AssertIs("failed", "script-error");
        Assert.False(string.IsNullOrWhiteSpace(write.ErrorText), write.ToString());
        Assert.Equal(0, await CountProductsAsync(parameters));
    }

    // The same item without the nested pair, so that the failure of the test above is that of the nested rollback, and
    // the question the executor asks the base before the commit fails no Write that is whole.
    [Fact]
    public async Task A_write_whose_script_writes_an_item_without_a_nested_transaction_succeeds_and_the_item_is_in_the_base()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();

        OutcomeLine write;

        await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync())
        {
            write = await writer.RunAsync(TestBaseScripts.CreateProduct, parameters);
        }

        write.AssertIs("succeeded", null);
        Assert.Equal(1, await CountProductsAsync(parameters));
    }

    private static async Task<int> CountProductsAsync(string parameters)
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();
        OutcomeLine count = await reader.RunAsync(TestBaseScripts.CountProducts, parameters);

        count.AssertIs("succeeded", null);

        return count.Result.GetInt32();
    }
}
