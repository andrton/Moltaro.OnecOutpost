using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace Moltaro.OnecOutpost.Protocol;

// The forms of §2 of docs/protocol.md that the headers, the pairing token, the codec and the audit log need: canonical
// base64url, the 36-character lowercase identity and the form of a time; and the name of a request, which its path
// (§1) and its signing inputs (§3) carry. They live here once, in the root, so that neither the signing types nor the
// codec needs the other for them.
internal static class OutpostForms
{
    // The one form of a time of §2, UTC as yyyy-MM-ddTHH:mm:ss.fffZ, which times are written and read in.
    public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // The alphabet of base64url (RFC 4648 §5), without the padding character.
    private static readonly SearchValues<char> Base64UrlAlphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    // Writes a binary value as §2 says: base64url without padding (RFC 4648 §5).
    public static string WriteBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Base64Url.EncodeToString(bytes);
    }

    // Reads a binary value of §2. False, without an exception, for any text but canonical base64url without padding:
    // a character outside the alphabet, padding, a length that no bytes give and non-zero padding bits included.
    public static bool TryReadBase64Url(ReadOnlySpan<char> text, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;

        // The decoder of .NET also accepts padding and white space, so only the alphabet itself is let through to it.
        if (text.ContainsAnyExcept(Base64UrlAlphabet))
        {
            return false;
        }

        // What is left for the decoder to refuse is a length that no bytes give and padding bits that are not zero,
        // so that one value has one text.
        if (!Base64Url.IsValid(text))
        {
            return false;
        }

        bytes = Base64Url.DecodeFromChars(text);

        return true;
    }

    // Writes an identity as §2 says: 36 lowercase characters in the form xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.
    public static string WriteId(Guid id)
    {
        return id.ToString("D");
    }

    // Reads an identity of §2. False, without an exception, for any text but exactly the form that WriteId writes:
    // uppercase digits, braces and every other form of a GUID are refused.
    public static bool TryReadId(ReadOnlySpan<char> text, out Guid id)
    {
        id = Guid.Empty;

        if (text.Length != 36)
        {
            return false;
        }

        // Guid.TryParseExact also reads uppercase digits, so the form is checked here character by character.
        for (int index = 0; index < text.Length; index++)
        {
            bool isHyphen = index is 8 or 13 or 18 or 23;

            if (isHyphen ? text[index] != '-' : !char.IsAsciiHexDigitLower(text[index]))
            {
                return false;
            }
        }

        return Guid.TryParseExact(text, "D", out id);
    }

    // Writes the name of a request, as its path (§1) and its signing inputs (§3) carry it: pair, poll or outcome.
    public static string WriteRequestKind(OutpostRequestKindEnum kind)
    {
        return kind switch
        {
            OutpostRequestKindEnum.Pair => "pair",
            OutpostRequestKindEnum.Poll => "poll",
            OutpostRequestKindEnum.Outcome => "outcome",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // Reads the name of a request that WriteRequestKind writes. False, without an exception, for any other text.
    public static bool TryReadRequestKind(string name, out OutpostRequestKindEnum kind)
    {
        switch (name)
        {
            case "pair":
                kind = OutpostRequestKindEnum.Pair;
                return true;
            case "poll":
                kind = OutpostRequestKindEnum.Poll;
                return true;
            case "outcome":
                kind = OutpostRequestKindEnum.Outcome;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
