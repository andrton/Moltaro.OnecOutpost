using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeWindows;

namespace Moltaro.OnecOutpost.Agent.Tests.Secrets;

// The form of secrets.dat (the S6a brief, and the S9c brief for its workers): the protection over
// {"key":…,"passwords":[{"base":…,"mode":"read","password":…},…],"workers":[{"base":…,"password":…},…]}, written by the
// writer of §2 of docs/protocol.md with the key in base64url. workers holds the password of the worker account of each
// base whose write names one; it is required, and empty when no base names one. The secrets stay on their machine
// (principle 9): the file holds no plaintext, and no error quotes what the file holds. The fake protection stands for
// DPAPI.
public sealed class SecretsFileTests
{
    private readonly FakeProtection _protection = new();

    private readonly byte[] _key;

    public SecretsFileTests()
    {
        using ECDsa agent = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        _key = agent.ExportPkcs8PrivateKey();
    }

    // An empty password is a password, and a password may hold any text.
    [Fact]
    public void The_secrets_read_back_as_they_were_written()
    {
        AgentSecrets secrets = new(_key, new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "read-pw",
            [("accounting", OutpostModeEnum.Write)] = "wr\"ite\\ пароль",
            [("warehouse", OutpostModeEnum.Read)] = "",
        }, new Dictionary<string, string>());

        AgentSecrets read = SecretsFile.Read(SecretsFile.Write(secrets, _protection), _protection);

        Assert.Equal(_key, read.PrivateKey);
        Assert.Equal(
            secrets.Passwords.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal),
            read.Passwords.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal));
    }

    // The worker passwords of the bases come after the passwords of the 1C accounts.
    [Fact]
    public void The_protection_is_given_the_secrets_in_the_form_of_the_file_written_by_the_writer_of_section_2()
    {
        AgentSecrets secrets = new(_key, new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Write)] = "wr\"ite\\ пароль",
        }, new Dictionary<string, string>
        {
            ["accounting"] = "wor\"ker\\ пароль",
        });

        byte[] plain = _protection.Unprotect(SecretsFile.Write(secrets, _protection));

        string expected =
            $$"""{"key":"{{Base64Url.EncodeToString(_key)}}","passwords":[{"base":"accounting","mode":"write","password":"wr\"ite\\ пароль"}],"workers":[{"base":"accounting","password":"wor\"ker\\ пароль"}]}""";

        Assert.Equal(expected, Encoding.UTF8.GetString(plain));
    }

    // An empty password is a password, and a password may hold any text, for a worker account as for a 1C account.
    [Fact]
    public void The_worker_passwords_read_back_as_they_were_written()
    {
        AgentSecrets secrets = new(_key, new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "read-pw",
            [("accounting", OutpostModeEnum.Write)] = "write-pw",
            [("payroll", OutpostModeEnum.Read)] = "read-pw",
            [("payroll", OutpostModeEnum.Write)] = "write-pw",
        }, new Dictionary<string, string>
        {
            ["accounting"] = "wor\"ker\\ пароль",
            ["payroll"] = "",
        });

        AgentSecrets read = SecretsFile.Read(SecretsFile.Write(secrets, _protection), _protection);

        Assert.Equal(
            secrets.WorkerPasswords.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            read.WorkerPasswords.OrderBy(entry => entry.Key, StringComparer.Ordinal));
        Assert.Equal(
            secrets.Passwords.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal),
            read.Passwords.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal));
    }

    [Fact]
    public void The_file_holds_no_plaintext_of_a_password_or_the_key()
    {
        AgentSecrets secrets = new(_key, new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "S3cret-password",
        }, new Dictionary<string, string>
        {
            ["accounting"] = "S3cret-worker-password",
        });

        byte[] file = SecretsFile.Write(secrets, _protection);

        Assert.Equal(-1, file.AsSpan().IndexOf("S3cret-password"u8));
        Assert.Equal(-1, file.AsSpan().IndexOf("S3cret-worker-password"u8));
        Assert.Equal(-1, file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Base64Url.EncodeToString(_key))));
        Assert.Equal(-1, file.AsSpan().IndexOf("accounting"u8));
    }

    // Such as a file copied from another machine: the exception of the protection passes.
    [Fact]
    public void A_file_that_the_protection_refuses_to_unprotect_fails_with_its_CryptographicException()
    {
        AgentSecrets secrets = new(_key, new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "read-pw",
        }, new Dictionary<string, string>());

        byte[] file = SecretsFile.Write(secrets, _protection);
        _protection.Refuses = true;

        Assert.ThrowsAny<CryptographicException>(() => SecretsFile.Read(file, _protection));
    }

    // Each content breaks the form and holds S3cret where a secret would be, which the error must not quote. The key
    // of the content is written in the place of KEY.
    [Theory]
    [InlineData("S3cret is not JSON")]
    [InlineData("""["KEY","S3cret"]""")]
    [InlineData("""{"passwords":[{"base":"accounting","mode":"read","password":"S3cret"}]}""")]
    [InlineData("""{"key":"KEY","passwords":[{"base":"accounting","mode":"read","password":"S3cret"}],"note":"S3cret"}""")]
    [InlineData("""{"key":"S3cret!","passwords":[]}""")]
    [InlineData("""{"key":"KEY","passwords":[{"base":"accounting","mode":"admin","password":"S3cret"}]}""")]
    [InlineData("""{"key":"KEY","passwords":[{"base":"Accounting","mode":"read","password":"S3cret"}]}""")]
    [InlineData("""{"key":"KEY","passwords":[{"base":"accounting","mode":"read","password":42}]}""")]
    [InlineData("""{"key":"KEY","passwords":[{"base":"accounting","mode":"read","password":null}]}""")]
    [InlineData("""{"key":"KEY","passwords":[{"base":"accounting","mode":"read"}]}""")]
    [InlineData("""{"key":"KEY","passwords":{"base":"accounting","mode":"read","password":"S3cret"}}""")]
    public void Content_that_breaks_the_form_of_the_file_fails_with_FormatException_quoting_nothing(string content)
    {
        string plain = content.Replace("KEY", Base64Url.EncodeToString(_key), StringComparison.Ordinal);
        byte[] file = _protection.Protect(Encoding.UTF8.GetBytes(plain));

        FormatException exception = Assert.ThrowsAny<FormatException>(() => SecretsFile.Read(file, _protection));

        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
    }

    // Each content holds a valid key and passwords and breaks the form in its workers, with S3cret where a secret would
    // be, which the error must not quote: a base against the rule of base keys, a password that is not a string, null or
    // left out, a member besides base and password, workers that are not an array or null, an entry that is not an
    // object, and a base given twice, since a base has one worker account.
    [Theory]
    [InlineData("""[{"base":"Accounting","password":"S3cret"}]""")]
    [InlineData("""[{"base":"accounting","password":42}]""")]
    [InlineData("""[{"base":"accounting","password":null}]""")]
    [InlineData("""[{"base":"accounting"}]""")]
    [InlineData("""[{"password":"S3cret"}]""")]
    [InlineData("""[{"base":"accounting","mode":"write","password":"S3cret"}]""")]
    [InlineData("""{"base":"accounting","password":"S3cret"}""")]
    [InlineData("""null""")]
    [InlineData("""["S3cret"]""")]
    [InlineData("""[{"base":"accounting","password":"S3cret-1"},{"base":"accounting","password":"S3cret-2"}]""")]
    public void Workers_that_break_the_form_of_the_file_fail_with_FormatException_quoting_nothing(string workers)
    {
        string plain =
            $$"""{"key":"{{Base64Url.EncodeToString(_key)}}","passwords":[{"base":"accounting","mode":"read","password":"read-pw"}],"workers":{{workers}}}""";

        byte[] file = _protection.Protect(Encoding.UTF8.GetBytes(plain));

        FormatException exception = Assert.ThrowsAny<FormatException>(() => SecretsFile.Read(file, _protection));

        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
    }

    // workers is required in 0.x, even when no base names a worker account.
    [Fact]
    public void A_file_without_workers_fails_with_FormatException()
    {
        string plain =
            $$"""{"key":"{{Base64Url.EncodeToString(_key)}}","passwords":[{"base":"accounting","mode":"read","password":"read-pw"}]}""";

        byte[] file = _protection.Protect(Encoding.UTF8.GetBytes(plain));

        Assert.ThrowsAny<FormatException>(() => SecretsFile.Read(file, _protection));
    }

    // A base and a mode name one 1C account, so one password each.
    [Fact]
    public void A_base_and_a_mode_given_twice_fail_with_FormatException_quoting_nothing()
    {
        string plain =
            $$"""{"key":"{{Base64Url.EncodeToString(_key)}}","passwords":[{"base":"accounting","mode":"read","password":"S3cret-1"},{"base":"accounting","mode":"read","password":"S3cret-2"}]}""";

        byte[] file = _protection.Protect(Encoding.UTF8.GetBytes(plain));

        FormatException exception = Assert.ThrowsAny<FormatException>(() => SecretsFile.Read(file, _protection));

        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
    }

    // The S6 brief, "AgentHost": the agent signs with its key from the first poll on, so a key that is not an ECDSA key
    // on P-256 in PKCS#8 is refused when the file is read, not when the agent first signs: random bytes, a key on
    // P-384, and an RSA key.
    [Theory]
    [InlineData("random")]
    [InlineData("p384")]
    [InlineData("rsa")]
    public void A_key_that_is_not_an_ECDSA_key_on_P256_in_PKCS8_fails_with_FormatException(string kind)
    {
        AgentSecrets secrets = new(KeyOf(kind), new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "read-pw",
        }, new Dictionary<string, string>());

        byte[] file = SecretsFile.Write(secrets, _protection);

        Assert.ThrowsAny<FormatException>(() => SecretsFile.Read(file, _protection));
    }

    private static byte[] KeyOf(string kind)
    {
        if (kind == "p384")
        {
            using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP384);

            return other.ExportPkcs8PrivateKey();
        }

        if (kind == "rsa")
        {
            using RSA rsa = RSA.Create(2048);

            return rsa.ExportPkcs8PrivateKey();
        }

        return RandomNumberGenerator.GetBytes(138);
    }
}
