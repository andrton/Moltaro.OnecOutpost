using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Shell.Tests.Sessions;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Programs;
using Npgsql;

namespace Moltaro.OnecOutpost.Shell.Tests.Startup;

public sealed class StartupTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("TF_BUILD", "True")]
    public async Task Startup_help_needs_no_resources_and_is_plain_text_under_ci_variables(string variable, string value)
    {
        await using ShellSession shell = ShellSession.Start(null, new Dictionary<string, string> { [variable] = value }, "--help");
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(0, end.ExitCode);
        Assert.Contains("--key-folder", end.Output, StringComparison.Ordinal);
        Assert.Contains("--listen", end.Output, StringComparison.Ordinal);
        Assert.Contains("--certificate", end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", end.Output + end.Error, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, end.Output, StringComparison.Ordinal);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("http://localhost:0")]
    [InlineData("http://0.0.0.0:5000")]
    [InlineData("http://[::]:5000")]
    [InlineData("http://*:5000")]
    [InlineData("http://outpost.example.com:5000")]
    [InlineData("http://someone@127.0.0.1:5000")]
    [InlineData("http://127.0.0.1:5000/path")]
    [InlineData("http://127.0.0.1:5000/?query")]
    [InlineData("http://127.0.0.1:5000/#fragment")]
    [InlineData("https://localhost:443")]
    public async Task An_origin_outside_the_startup_contract_is_a_usage_error(string origin)
    {
        using TemporaryFolder keys = new();
        await using ShellSession shell = ShellSession.Start(
            "Host=127.0.0.1;Port=1;Timeout=1;Database=unused;Username=unused;Password=unused",
            "--key-folder",
            keys.Path,
            "--listen",
            origin);
        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(2, end.ExitCode);
        Assert.Contains("Usage", end.Output + end.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(keys.Path));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    [Trait("Requires", "PostgreSQL")]
    public async Task A_restart_reuses_the_private_signing_key_and_the_agents_in_the_database()
    {
        string connection = await ShellDatabase.CreateAsync(postgres, TestContext.Current.CancellationToken);
        using TemporaryFolder keys = new();
        string file = keys.PathOf("signing-key.pem");
        OutpostPairingTokenText first;

        await using (ShellSession shell = Start(connection, keys))
        {
            _ = await shell.AddressAsync();
            await shell.SendAsync("token Agent from the first run\n");
            Assert.True(OutpostPairingTokenText.TryRead(await shell.TokenAsync(), out OutpostPairingTokenText? parts));
            first = parts;
            Assert.Equal(0, (await shell.EndAsync()).ExitCode);
        }

        string pem = File.ReadAllText(file);
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(pem);
        Assert.Equal(256, key.KeySize);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem, StringComparison.Ordinal);
        Assert.Equal(first.Fingerprint, OutpostKeys.Fingerprint(key.ExportSubjectPublicKeyInfo()));
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode Owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(file) & ~Owner);
        }

        await using (ShellSession shell = Start(connection, keys))
        {
            _ = await shell.AddressAsync();
            await shell.SendAsync("agents\ntoken Agent from the second run\n");
            Assert.True(OutpostPairingTokenText.TryRead(await shell.TokenAsync(), out OutpostPairingTokenText? second));
            ProgramEnd end = await shell.EndAsync();
            Assert.Equal(0, end.ExitCode);
            Assert.Equal(first.Fingerprint, second.Fingerprint);
            Assert.NotEqual(first.Agent, second.Agent);
            Assert.Contains("Agent from the first run", end.Output, StringComparison.Ordinal);
        }

        Assert.Equal(pem, File.ReadAllText(file));
        Assert.Single(Directory.EnumerateFileSystemEntries(keys.Path));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    [Trait("Requires", "PostgreSQL")]
    public async Task An_invalid_existing_signing_key_fails_startup_without_replacing_or_printing_it()
    {
        string connection = await ShellDatabase.CreateAsync(postgres, TestContext.Current.CancellationToken);
        using TemporaryFolder keys = new();
        string file = keys.PathOf("signing-key.pem");
        const string Contents = "invalid-private-key-of-the-test";
        File.WriteAllText(file, Contents);
        await using ShellSession shell = Start(connection, keys);

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(1, end.ExitCode);
        Assert.DoesNotContain("/outpost", end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Contents, end.Output + end.Error, StringComparison.Ordinal);
        Assert.Equal(Contents, File.ReadAllText(file));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    [Trait("Requires", "PostgreSQL")]
    public async Task A_postgresql_startup_failure_does_not_print_its_connection_string_or_password()
    {
        string database = await ShellDatabase.CreateAsync(postgres, TestContext.Current.CancellationToken);
        const string Password = "a-private-password-that-postgres-will-refuse";
        string wrong = new NpgsqlConnectionStringBuilder(database) { Password = Password }.ConnectionString;
        using TemporaryFolder keys = new();
        await using ShellSession shell = Start(wrong, keys);

        ProgramEnd end = await shell.EndAsync();

        Assert.Equal(1, end.ExitCode);
        Assert.DoesNotContain("/outpost", end.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong, end.Output + end.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, end.Output + end.Error, StringComparison.Ordinal);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    [Trait("Requires", "PostgreSQL")]
    public async Task An_https_origin_uses_the_pfx_and_prints_its_usable_agent_url()
    {
        string connection = await ShellDatabase.CreateAsync(postgres, TestContext.Current.CancellationToken);
        using TemporaryFolder keys = new();
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
        const string Password = "temporary-pfx-password";
        string pfx = keys.PathOf("test.pfx");
        File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pfx, Password));

        using TcpListener unused = new(IPAddress.Loopback, 0);
        unused.Start();
        int port = ((IPEndPoint)unused.LocalEndpoint).Port;
        unused.Stop();

        await using ShellSession shell = ShellSession.Start(
            connection,
            new Dictionary<string, string> { [ShellSession.CertificatePasswordVariable] = Password },
            "--key-folder",
            keys.Path,
            "--listen",
            $"https://127.0.0.1:{port}/",
            "--certificate",
            pfx);
        Uri address = await shell.AddressAsync();
        Assert.Equal($"https://127.0.0.1:{port}/outpost", address.AbsoluteUri.TrimEnd('/'));

        using HttpClient client = new(new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, actual, _, _) =>
                actual?.GetCertHashString() == certificate.GetCertHashString(),
        });
        Uri pair = new(address.AbsoluteUri.TrimEnd('/') + "/v1/pair");
        using HttpResponseMessage response = await TestTimes.EndOf(client.GetAsync(pair, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        ProgramEnd end = await shell.EndAsync();
        Assert.Equal(0, end.ExitCode);
        Assert.DoesNotContain(Password, end.Output + end.Error, StringComparison.Ordinal);
    }

    private static ShellSession Start(string connection, TemporaryFolder keys)
    {
        return ShellSession.Start(connection, "--key-folder", keys.Path, "--listen", ShellSession.LoopbackOrigin);
    }
}
