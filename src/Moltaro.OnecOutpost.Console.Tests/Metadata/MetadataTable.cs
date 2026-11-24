using System.Globalization;
using Moltaro.OnecOutpost.TestSupport.Check;
using Moltaro.OnecOutpost.TestSupport.Metadata;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The table that the console's metadata writes after its walk (the S10 brief, "metadata <BASE> <FILE>"), read back from
// its output: the columns Category, Found, Read and Failed, one row per category of the census, drawn with borders as
// check's table is. Lines that are not rows of the table, such as its header and the line of its end, are passed over.
internal static class MetadataTable
{
    // The rows of the table in their order; none when the output holds no table.
    public static IReadOnlyList<MetadataCategory> Rows(string output)
    {
        List<MetadataCategory> rows = [];

        foreach (string line in output.Split('\n'))
        {
            if (CheckTable.Cells(line) is not [string category, string found, string read, string failed])
            {
                continue;
            }

            if (Number(found) is int foundCount && Number(read) is int readCount && Number(failed) is int failedCount)
            {
                rows.Add(new MetadataCategory(category, foundCount, readCount, failedCount));
            }
        }

        return rows;
    }

    private static int? Number(string cell)
    {
        return int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : null;
    }
}
