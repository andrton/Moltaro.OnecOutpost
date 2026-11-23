using System.Text.Json;

namespace Moltaro.OnecOutpost.OneC.Tests.Lab;

// Scripts that more than one test sends to a copy of a typical base, in the English variant of the language: the
// latest posted document of a type, its movements and its posting again, which name no object of a typical
// configuration, and the comparison of the movements before and after.
internal static class TypicalBaseScripts
{
    // Gives the identifier of the latest posted document of the type.
    public const string LatestPostedDocument = """
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
    public const string MovementsOfTheDocument = """
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

    public const string PostTheDocumentAgain = """
        Document = Documents[Parameters["documentType"]].GetRef(New UUID(Parameters["id"])).GetObject();
        Document.Write(DocumentWriteMode.Posting);
        """;

    // The registers whose records differ before and after, or that only one side holds.
    public static IEnumerable<string> Changed(JsonElement before, JsonElement after)
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
