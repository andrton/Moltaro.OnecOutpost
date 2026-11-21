using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.ScriptContract;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.Commands;

// A Write runs its script under the write account of the base, inside one 1C transaction: either everything the script
// did is committed, or nothing (concept §3, "The two commands"). The executor checks the type of the result and the
// size of the outcome before the commit, and a script that ends the transaction itself gets Unknown (concept rule 5).
// So does a Write whose commit raises, since the executor cannot tell whether it was written. A script that leaves a
// transaction of its own open fails, since the executor rolls everything back.
public sealed class WriteTests
{
    // Writes a product, begins a transaction inside the Write, writes another product and leaves that transaction open.
    private const string LeaveATransactionOpen =
        TestBaseScripts.CreateProduct + "\nBeginTransaction();\n" + TestBaseScripts.CreateProduct;

    // Rolls back a transaction begun inside the Write: 1C has one transaction with a count of the transactions begun,
    // so the whole Write can no longer be committed.
    private const string RollBackANestedTransaction = "BeginTransaction();\nRollbackTransaction();";

    // In a Read, outside any transaction of the executor: whether a transaction whose nested transaction was rolled
    // back is still active and refuses its commit. Every transaction the script began is rolled back before it ends.
    private const string CommitAfterANestedRollback = """
        BeginTransaction();
        BeginTransaction();
        RollbackTransaction();
        StillActive = TransactionActive();
        CommitRaised = False;
        Try
            CommitTransaction();
        Except
            CommitRaised = True;
        EndTry;
        While TransactionActive() Do
            RollbackTransaction();
        EndDo;
        Result = New Structure("StillActive, CommitRaised", StillActive, CommitRaised);
        """;

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

    // The probe in a Read shows first that on this platform the transaction stays active after a nested rollback and
    // its commit raises, so that the Write reaches a commit that raises and not a transaction the script ended.
    [Fact]
    public async Task A_write_whose_commit_raises_gives_unknown_transaction_ended()
    {
        await using (WorkerProcess reader = await LabWorkers.TestBaseReadAsync())
        {
            OutcomeLine probe = await reader.RunAsync(CommitAfterANestedRollback);

            probe.AssertIs("succeeded", null);

            bool stillActive = probe.Result.GetProperty("StillActive").GetBoolean();
            bool commitRaised = probe.Result.GetProperty("CommitRaised").GetBoolean();
            Assert.True(
                stillActive && commitRaised,
                $"A nested rollback does not leave a transaction whose commit raises on this platform: {probe}");
        }

        await using WorkerProcess writer = await LabWorkers.TestBaseWriteAsync();

        OutcomeLine write = await writer.RunAsync(RollBackANestedTransaction);

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

    private static async Task<int> CountProductsAsync(string parameters)
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();
        OutcomeLine count = await reader.RunAsync(TestBaseScripts.CountProducts, parameters);

        count.AssertIs("succeeded", null);

        return count.Result.GetInt32();
    }
}
