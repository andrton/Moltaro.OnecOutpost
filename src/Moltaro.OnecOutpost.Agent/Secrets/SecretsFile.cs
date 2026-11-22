using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Secrets;

// The form of secrets.dat: Protect over {"key":…,"passwords":[{"base":…,"mode":"read","password":…},…]}, written by
// the writer of §2 of docs/protocol.md with the key in base64url.
internal static class SecretsFile
{
    // Unprotects the file and reads its secrets. The CryptographicException of the protection passes; content that
    // breaks the form, a base key against its rule or a base and mode given twice is FormatException, quoting nothing.
    public static AgentSecrets Read(byte[] file, IMachineProtection protection)
    {
        byte[] plain = protection.Unprotect(file);

        try
        {
            using JsonDocument document = OutpostJsonReader.ParseMessage(plain);
            JsonElement secrets = OutpostJsonReader.Members(document.RootElement, "key", "passwords");

            byte[] key = OutpostJsonReader.ReadBytes(secrets, "key");
            Dictionary<(string Base, OutpostModeEnum Mode), string> passwords = [];

            foreach (JsonElement item in OutpostJsonReader.ReadArray(secrets, "passwords").EnumerateArray())
            {
                JsonElement entry = OutpostJsonReader.Members(item, "base", "mode", "password");

                string baseKey = OutpostJsonReader.ReadString(entry, "base");
                OutpostModeEnum mode = ReadMode(entry);
                string password = OutpostJsonReader.ReadString(entry, "password");

                if (!OutpostNames.IsBaseKey(baseKey))
                {
                    throw OutpostJsonReader.Form("A base is not a base key.");
                }

                // A base and a mode name one 1C account, so one password each.
                if (!passwords.TryAdd((baseKey, mode), password))
                {
                    throw OutpostJsonReader.Form("A base and a mode are given twice.");
                }
            }

            return new AgentSecrets(key, passwords);
        }
        catch (OutpostProtocolException exception)
        {
            // The reasons of the reader name members, never their values, so none quotes a secret.
            throw new FormatException($"The content is not in the form of secrets.dat: {exception.Message}");
        }
    }

    // Writes the secrets in the form of the file and protects them.
    public static byte[] Write(AgentSecrets secrets, IMachineProtection protection)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("key");
        writer.Bytes(secrets.PrivateKey);
        writer.Name("passwords");
        writer.StartArray();

        foreach (((string baseKey, OutpostModeEnum mode), string password) in secrets.Passwords)
        {
            writer.StartObject();
            writer.Name("base");
            writer.String(baseKey);
            writer.Name("mode");
            writer.String(mode == OutpostModeEnum.Read ? "read" : "write");
            writer.Name("password");
            writer.String(password);
            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();

        return protection.Protect(writer.ToArray());
    }

    private static OutpostModeEnum ReadMode(JsonElement entry)
    {
        return OutpostJsonReader.ReadString(entry, "mode") switch
        {
            "read" => OutpostModeEnum.Read,
            "write" => OutpostModeEnum.Write,
            _ => throw OutpostJsonReader.Form("A mode is not read or write."),
        };
    }
}
