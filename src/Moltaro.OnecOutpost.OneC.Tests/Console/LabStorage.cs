using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// The whole storage structure of a base as the lab's own worker reads it, apart from the console (the S10 brief, "Lab
// tests" of "Internal of S10c"): one GetDBStorageStructureInfo(Undefined, True), with the DBMS's names, its storage
// names as a set, and whether the columns of its table have their English names, StorageTableName, or the Russian
// ones, ИмяТаблицыХранения, which a configuration in Russian gives. The script uses only what the compatibility mode
// 8.2.13 allows.
internal static class LabStorage
{
    public const string Script = """
        Table = GetDBStorageStructureInfo(Undefined, True);
        English = Table.Columns.Find("StorageTableName") <> Undefined;

        If English Then
            Column = "StorageTableName";
        Else
            Column = "ИмяТаблицыХранения";
        EndIf;

        Names = New Array;

        For Each Row In Table Do
            Names.Add(Row[Column]);
        EndDo;

        Result = New Structure("english, names", English, Names);
        """;

    // The storage of the base of the worker, opened in safe mode under its read account.
    public static async Task<(bool English, IReadOnlySet<string> Names)> ReadAsync(WorkerProcess worker)
    {
        OutcomeLine outcome = await worker.RunAsync(Script);

        outcome.AssertIs("succeeded", null);

        IReadOnlySet<string> names = outcome.Result
            .GetProperty("names")
            .EnumerateArray()
            .Select(name => name.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        return (outcome.Result.GetProperty("english").GetBoolean(), names);
    }
}
