using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Console.Commands;

// The JSON of the report of metadata, format 1 (the S10 brief, "The report"): UTF-8 without a byte order mark,
// indented, every character written as it is, since the report is a local file for people and not a page of HTML. Its
// members in their order: format, base, written in the form of §2, platform, configuration, storage, categories in the
// census's order, objects, each with the category of its page first and its error as the walk judged it, commonStorage
// and reads. What the script gave is copied as it gave it, so nothing of the report is data of the base, a password or
// an account.
internal static class MetadataJson
{
    public const int Format = 1;

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Writes the report of the walk of the base, at the time given, to the stream, which stays open.
    public static void Write(Stream stream, string baseKey, DateTimeOffset written, MetadataWalk walk)
    {
        using Utf8JsonWriter writer = new(stream, Options);

        writer.WriteStartObject();
        writer.WriteNumber("format", Format);
        writer.WriteString("base", baseKey);
        writer.WriteString("written", written.UtcDateTime.ToString(OutpostForms.TimeFormat, CultureInfo.InvariantCulture));
        writer.WritePropertyName("platform");
        walk.Platform.WriteTo(writer);
        writer.WritePropertyName("configuration");
        walk.Configuration.WriteTo(writer);

        writer.WriteStartObject("storage");
        writer.WriteBoolean("available", walk.StorageReason is null);
        writer.WriteString("reason", walk.StorageReason);
        writer.WriteEndObject();

        writer.WriteStartArray("categories");

        foreach (WalkCategory category in walk.Categories)
        {
            writer.WriteStartObject();
            writer.WriteString("category", category.Name);
            writer.WriteNumber("found", category.Found);
            writer.WriteNumber("read", category.Read);
            writer.WriteNumber("failed", category.Failed);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("objects");

        foreach (WalkObject item in walk.Objects)
        {
            WriteObject(writer, item);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("commonStorage");

        if (walk.CommonStorage is JsonElement common)
        {
            common.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteStartArray("reads");

        foreach (WalkRead read in walk.Reads)
        {
            WriteRead(writer, read);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    // The object as its page gave it, after the category of the page, with the error that the walk judged.
    private static void WriteObject(Utf8JsonWriter writer, WalkObject item)
    {
        writer.WriteStartObject();
        writer.WriteString("category", item.Category);

        foreach (JsonProperty property in item.Item.EnumerateObject())
        {
            if (property.NameEquals("category"))
            {
                continue;
            }

            if (property.NameEquals("error"))
            {
                writer.WriteString("error", item.Error);
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteRead(Utf8JsonWriter writer, WalkRead read)
    {
        writer.WriteStartObject();
        writer.WriteString("read", read.Read);
        writer.WriteString("category", read.Category);
        WriteNumber(writer, "from", read.From);
        WriteNumber(writer, "count", read.Count);
        writer.WriteNumber("size", read.Size);
        writer.WriteString("kind", OutpostForms.WriteOutcomeKind(read.Kind));
        writer.WriteString("code", read.Code);
        writer.WriteString("errorText", read.ErrorText);
        writer.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is int number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}
