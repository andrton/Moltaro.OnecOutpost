using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Agent.Secrets;

// The form of secrets.dat: Protect over {"key":…,"passwords":[{"base":…,"mode":"read","password":…},…]}, written by
// the writer of §2 of docs/protocol.md with the key in base64url.
internal static class SecretsFile
{
    // Unprotects the file and reads its secrets. The CryptographicException of the protection passes; content that
    // breaks the form, a key that is not an ECDSA key on P-256 in PKCS#8, a base key against its rule or a base and mode
    // given twice is FormatException, quoting nothing.
    public static AgentSecrets Read(byte[] file, IMachineProtection protection)
    {
        byte[] plain = protection.Unprotect(file);

        try
        {
            using JsonDocument document = OutpostJsonReader.ParseMessage(plain);
            JsonElement secrets = OutpostJsonReader.Members(document.RootElement, "key", "passwords");

            byte[] key = OutpostJsonReader.ReadBytes(secrets, "key");
            CheckKey(key);

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

    // The agent signs with its key from its first poll on, so a key it cannot sign with is refused here, when the file is
    // read, and not when it first signs: the key's public part has to be the one form of §2 of docs/protocol.md, and
    // the PKCS#8 bytes hold nothing beyond the key.
    private static void CheckKey(byte[] key)
    {
        using ECDsa imported = ECDsa.Create();
        int read;

        try
        {
            imported.ImportPkcs8PrivateKey(key, out read);
        }
        catch (CryptographicException)
        {
            throw OutpostJsonReader.Form("The key is not an ECDSA key in PKCS#8.");
        }

        if (read != key.Length)
        {
            throw OutpostJsonReader.Form("The key has bytes beyond its PKCS#8 form.");
        }

        // ImportPublicKey refuses every key but one on P-256 with the code form, which the caller turns into a
        // FormatException.
        OutpostKeys.ImportPublicKey(OutpostKeys.ExportPublicKey(imported)).Dispose();
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
