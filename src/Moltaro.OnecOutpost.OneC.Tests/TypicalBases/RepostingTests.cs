using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.TypicalBases;

// On each copy of a typical base that lab.local.json names, the latest posted document of its documentType, posted
// again by the tunnel with the safe mode that writeSafeMode gives, has the register movements it had before, which a
// person's posting made (concept §12, O1). The document and its movements come from the base while the test runs; the
// scripts name no object of a typical configuration and read the movements through the register records of the
// document, whatever its registers are.
public sealed class RepostingTests
{
    // Gives the identifier of the latest posted document of the type.
    private const string LatestPostedDocument = """
        Query = New Query;
        Query.Text = "SELECT TOP 1 Documents.Ref AS Ref FROM Document." + Parameters["documentType"] + " AS Documents WHERE Documents.Posted ORDER BY Documents.Date DESC";
        Selection = Query.Execute().Select();
        If Not Selection.Next() Then
            Raise "The base has no posted document of the type.";
        EndIf;
        Result = String(Selection.Ref.UUID());
        """;

    // Gives the movements of the document: for each register of its register records, by the full name of the
    // register, its records in their order, each as the list of its columns with their values in the internal string
    // form of 1C, which tells apart any two values, references included.
    private const string MovementsOfTheDocument = """
        Document = Documents[Parameters["documentType"]].GetRef(New UUID(Parameters["id"])).GetObject();
        Movements = New Map;
        For Each RecordSet In Document.RegisterRecords Do
            RecordSet.Read();
            Table = RecordSet.Unload();
            Records = New Array;
            For Each Row In Table Do
                Columns = New Array;
                For Each Column In Table.Columns Do
                    Columns.Add(Column.Name + "=" + ValueToStringInternal(Row[Column.Name]));
                EndDo;
                Records.Add(Columns);
            EndDo;
            Movements.Insert(RecordSet.Metadata().FullName(), Records);
        EndDo;
        Result = Movements;
        """;

    private const string PostTheDocumentAgain = """
        Document = Documents[Parameters["documentType"]].GetRef(New UUID(Parameters["id"])).GetObject();
        Document.Write(DocumentWriteMode.Posting);
        """;

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_latest_posted_document_posted_again_by_the_tunnel_has_the_movements_it_had_before()
    {
        IReadOnlyList<LabTypicalBase> typicalBases = LabFile.Read().TypicalBases;

        Assert.True(typicalBases.Count > 0, "lab.local.json names no copy of a typical base in typicalBases.");

        for (int index = 0; index < typicalBases.Count; index++)
        {
            await PostTheLatestDocumentAgainAsync(typicalBases[index], $"typicalBases[{index}]");
        }
    }

    private static async Task PostTheLatestDocumentAgainAsync(LabTypicalBase typicalBase, string name)
    {
        string about = $"{name}, {typicalBase.Configuration} {typicalBase.Version}, {typicalBase.DocumentType}";

        await using WorkerProcess reader = await LabWorkers.TypicalBaseReadAsync(typicalBase);
        await using WorkerProcess writer = await LabWorkers.TypicalBaseWriteAsync(typicalBase);

        OutcomeLine latest = await reader.RunAsync(
            LatestPostedDocument,
            new JsonObject { ["documentType"] = typicalBase.DocumentType }.ToJsonString());

        latest.AssertIs("succeeded", null);

        string document = new JsonObject
        {
            ["documentType"] = typicalBase.DocumentType,
            ["id"] = latest.Result.GetString(),
        }.ToJsonString();

        OutcomeLine before = await reader.RunAsync(MovementsOfTheDocument, document);
        OutcomeLine posted = await writer.RunAsync(PostTheDocumentAgain, document);
        OutcomeLine after = await reader.RunAsync(MovementsOfTheDocument, document);

        before.AssertIs("succeeded", null);
        posted.AssertIs("succeeded", null);
        after.AssertIs("succeeded", null);

        Assert.True(
            before.Result.EnumerateObject().Any(register => register.Value.GetArrayLength() > 0),
            $"{about}: the latest posted document has no movements to compare.");

        string[] changed = [.. Changed(before.Result, after.Result)];
        Assert.True(changed.Length == 0, $"{about}: the movements changed in {string.Join(", ", changed)}.");
    }

    // The registers whose records differ before and after, or that only one side holds.
    private static IEnumerable<string> Changed(JsonElement before, JsonElement after)
    {
        Dictionary<string, JsonElement> registersAfter = after
            .EnumerateObject()
            .ToDictionary(register => register.Name, register => register.Value);

        foreach (JsonProperty register in before.EnumerateObject())
        {
            bool same = registersAfter.Remove(register.Name, out JsonElement records)
                && JsonElement.DeepEquals(register.Value, records);

            if (!same)
            {
                yield return register.Name;
            }
        }

        foreach (string register in registersAfter.Keys)
        {
            yield return register;
        }
    }
}
