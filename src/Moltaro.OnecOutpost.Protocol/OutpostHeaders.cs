using System.Diagnostics.CodeAnalysis;

namespace Moltaro.OnecOutpost.Protocol;

// The values of the headers `Outpost-Agent` and `Outpost-Signature` of §4 of docs/protocol.md, in the forms of §2,
// which OutpostForms holds.
internal static class OutpostHeaders
{
    // The 64 bytes of a signature are 86 characters of base64url without padding (§2), and 86 characters are never
    // any other number of bytes.
    private const int SignatureTextLength = 86;

    // Writes the value of `Outpost-Agent`: the identity of the agent in the form of §2.
    public static string FormatAgent(Guid agent)
    {
        return OutpostForms.WriteId(agent);
    }

    // Reads the value of `Outpost-Agent`. False, without an exception, for null and for any text but an identity in
    // the form of §2.
    public static bool TryParseAgent(string? value, out Guid agent)
    {
        agent = Guid.Empty;

        return value is not null && OutpostForms.TryReadId(value, out agent);
    }

    // Writes the value of `Outpost-Signature`: the 64 bytes of a signature of §2 as 86 base64url characters.
    public static string FormatSignature(ReadOnlySpan<byte> signature)
    {
        return OutpostForms.WriteBase64Url(signature);
    }

    // Reads the value of `Outpost-Signature`. True only for exactly 86 canonical base64url characters that decode to
    // 64 bytes (§2); false, without an exception, for null and for anything else. It verifies nothing.
    public static bool TryParseSignature(string? value, [NotNullWhen(true)] out byte[]? signature)
    {
        signature = null;

        if (value is null || value.Length != SignatureTextLength)
        {
            return false;
        }

        return OutpostForms.TryReadBase64Url(value, out signature);
    }
}
