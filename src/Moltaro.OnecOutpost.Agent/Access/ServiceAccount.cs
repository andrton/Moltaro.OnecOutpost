using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Moltaro.OnecOutpost.Agent.Access;

// The account NT SERVICE\{name} that Windows gives a service, such as the agent's virtual account.
internal static class ServiceAccount
{
    // The SID in its string form, as Windows derives it from the name, so that it is known before the service exists and
    // without Windows: S-1-5-80- and the SHA-1 of the upper-cased name in UTF-16 as five sub-authorities, each four bytes
    // in little-endian order.
    public static string Sid(string serviceName)
    {
        byte[] hash = SHA1.HashData(Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant()));
        StringBuilder sid = new("S-1-5-80");

        for (int offset = 0; offset < hash.Length; offset += sizeof(uint))
        {
            uint subAuthority = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(offset));

            sid.Append('-');
            sid.Append(subAuthority.ToString(CultureInfo.InvariantCulture));
        }

        return sid.ToString();
    }
}
