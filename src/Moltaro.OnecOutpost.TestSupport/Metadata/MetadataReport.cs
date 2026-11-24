using System.Text;
using System.Text.Json;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Metadata;

// The report that the console's metadata writes (the S10 brief, "The report"), read back for the console's tests and
// the lab's: its bytes as they are on the disk, and its JSON, format 1, with its categories, its objects by their full
// names, the fields of an object, its common storage and its reads in their order. A member that the report gives as
// null and one that it leaves out read alike, as null.
internal sealed class MetadataReport
{
    private MetadataReport(byte[] bytes, JsonElement root)
    {
        Bytes = bytes;
        Root = root;
    }

    public byte[] Bytes { get; }

    public JsonElement Root { get; }

    // The bytes as UTF-8, which a report is written in.
    public string Text => Encoding.UTF8.GetString(Bytes);

    public bool StorageAvailable => Root.GetProperty("storage").GetProperty("available").GetBoolean();

    public string? StorageReason => TextOf(Root.GetProperty("storage"), "reason");

    public IReadOnlyList<MetadataCategory> Categories =>
    [
        .. Root.GetProperty("categories").EnumerateArray().Select(category => new MetadataCategory(
            category.GetProperty("category").GetString()!,
            category.GetProperty("found").GetInt32(),
            category.GetProperty("read").GetInt32(),
            category.GetProperty("failed").GetInt32())),
    ];

    public IReadOnlyList<JsonElement> Objects => [.. Root.GetProperty("objects").EnumerateArray()];

    // The rows of the storage of no object of the fifteen categories, or null when they were not read.
    public JsonElement? CommonStorage =>
        Root.TryGetProperty("commonStorage", out JsonElement common) && common.ValueKind != JsonValueKind.Null ? common : null;

    public IReadOnlyList<MetadataRead> Reads =>
    [
        .. Root.GetProperty("reads").EnumerateArray().Select(read => new MetadataRead(
            read.GetProperty("read").GetString()!,
            TextOf(read, "category"),
            NumberOf(read, "from"),
            NumberOf(read, "count"),
            read.GetProperty("size").GetInt64(),
            read.GetProperty("kind").GetString()!,
            TextOf(read, "code"),
            TextOf(read, "errorText"))),
    ];

    // The report in the file.
    public static MetadataReport Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        using JsonDocument document = JsonDocument.Parse(bytes);

        return new MetadataReport(bytes, document.RootElement.Clone());
    }

    // The text of a member, null when it is null or absent.
    public static string? TextOf(JsonElement element, string member)
    {
        return element.TryGetProperty(member, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    // The number of a member, null when it is null or absent.
    public static int? NumberOf(JsonElement element, string member)
    {
        return element.TryGetProperty(member, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    // The one field of the object in the collection, such as Attributes or Dimensions, by its name, outside any tabular
    // section.
    public static JsonElement FieldOf(JsonElement item, string collection, string name)
    {
        List<JsonElement> found =
        [
            .. item.GetProperty("fields").EnumerateArray().Where(field =>
                TextOf(field, "collection") == collection
                && TextOf(field, "tabularSection") is null
                && TextOf(field, "name") == name),
        ];

        Assert.True(found.Count == 1, $"{TextOf(item, "fullName")} has {found.Count} fields {collection}.{name}, not one.");

        return found[0];
    }

    // The one object of the report with the full name, such as Document.GoodsReceipt.
    public JsonElement Object(string fullName)
    {
        List<JsonElement> found = [.. Objects.Where(item => TextOf(item, "fullName") == fullName)];

        Assert.True(found.Count == 1, $"The report holds {found.Count} objects {fullName}, not one.");

        return found[0];
    }

    // The category of the report by its name, such as Catalogs.
    public MetadataCategory Category(string name)
    {
        return Assert.Single(Categories, category => category.Category == name);
    }

    // The storage names of every table that the report holds, of its objects and of its common storage.
    public IReadOnlySet<string> StorageNames()
    {
        IEnumerable<JsonElement> objectRows = Objects
            .Where(item => item.TryGetProperty("storage", out JsonElement storage) && storage.ValueKind == JsonValueKind.Array)
            .SelectMany(item => item.GetProperty("storage").EnumerateArray());
        IEnumerable<JsonElement> commonRows = CommonStorage?.EnumerateArray() ?? Enumerable.Empty<JsonElement>();

        return objectRows.Concat(commonRows).Select(row => row.GetProperty("storageName").GetString()!).ToHashSet(StringComparer.Ordinal);
    }
}
