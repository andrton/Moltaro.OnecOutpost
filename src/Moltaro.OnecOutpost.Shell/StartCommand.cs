using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moltaro.OnecOutpost.Server;
using Moltaro.Signals;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class StartCommand(ShellOutput output) : AsyncCommand<StartCommand.Settings>
{
    private const string Prefix = "/outpost";
    private const string Schema = "outpost";
    private const string PostgresVariable = "OUTPOST_SHELL_POSTGRES";
    private const string CertificatePasswordVariable = "OUTPOST_SHELL_CERTIFICATE_PASSWORD";

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ShellOrigin origin = settings.Origin!;
        string? connection = Environment.GetEnvironmentVariable(PostgresVariable);

        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new ShellError(ShellExit.Usage, $"Set {PostgresVariable} to the PostgreSQL connection string.");
        }

        using X509Certificate2? certificate = origin.Https ? LoadCertificate(settings.Certificate!) : null;
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        // Startup and request exceptions from the host or database may carry configuration. Print shell errors only.
        builder.Logging.ClearProviders();
        builder.WebHost.PreferHostingUrls(false);
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Endpoints supplied by ASP.NET configuration would be added to the origin chosen by the operator.
            options.Configure(new ConfigurationManager());
            origin.Listen(options, listener =>
            {
                if (certificate is not null)
                {
                    listener.UseHttps(certificate);
                }
            });
        });

        builder.Services.AddSingleton<ISignals>(_ => new SignalHost(new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "shell-local-signals",
        }));

        string pem = ReadSigningKey(settings.KeyFolder!);

        try
        {
            builder.Services.AddOutpostServer(options =>
            {
                options.ConnectionString = connection;
                options.Schema = Schema;
                options.SigningKeyPem = pem;
            });
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(OutpostServerOptions.SigningKeyPem))
        {
            throw new ShellError(ShellExit.Failure, "signing-key.pem must contain a PKCS#8 P-256 private key.");
        }

        await using WebApplication host = builder.Build();
        await output.WaitAsync("Migrating the PostgreSQL schema", async () =>
        {
            try
            {
                await OutpostMigrations.MigrateAsync(connection, Schema, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ShellError(ShellExit.Failure, $"Could not migrate the outpost schema. Check {PostgresVariable} and the database permissions.");
            }
        });

        host.MapOutpostAgents(Prefix);

        try
        {
            await output.WaitAsync("Starting the server", () => host.StartAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShellError(ShellExit.Failure, "Could not start the server. Check --listen and the certificate.");
        }

        try
        {
            IServer server = host.Services.GetRequiredService<IServer>();
            IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()!;
            int port = new Uri(addresses.Addresses.Single()).Port;
            output.Line($"Agent URL: {origin.AgentUrl(port, Prefix)}");

            using CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, host.Lifetime.ApplicationStopping);

            try
            {
                IOutpostServer outpost = host.Services.GetRequiredService<IOutpostServer>();
                return await new ShellSession(output, outpost).RunAsync(session.Token);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
                return output.Error(ShellExit.Cancelled, "The session was cancelled.");
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static X509Certificate2 LoadCertificate(string file)
    {
        string? password = Environment.GetEnvironmentVariable(CertificatePasswordVariable);
        X509Certificate2 certificate;

        try
        {
            // Windows TLS needs access to the private key through the system provider.
            certificate = X509CertificateLoader.LoadPkcs12FromFile(file, password);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ShellError(ShellExit.Failure, $"Could not load the PFX. Check --certificate and {CertificatePasswordVariable}.");
        }

        if (certificate.HasPrivateKey)
        {
            return certificate;
        }

        certificate.Dispose();
        throw new ShellError(ShellExit.Failure, "The PFX must contain the server certificate and its private key.");
    }

    private static string ReadSigningKey(string folder)
    {
        try
        {
            return ShellSigningKey.ReadOrCreate(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new ShellError(ShellExit.Failure, "Could not open or create signing-key.pem in the key folder.");
        }
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--key-folder <DIRECTORY>")]
        [Description("Required. An existing private directory for the server signing key.")]
        public string? KeyFolder { get; init; }

        [CommandOption("--listen <ORIGIN>")]
        [Description("The server origin. Defaults to http://127.0.0.1:5000.")]
        public string Listen { get; init; } = "http://127.0.0.1:5000";

        [CommandOption("--certificate <PFX>")]
        [Description("A PFX containing the certificate and key for HTTPS.")]
        public string? Certificate { get; init; }

        internal ShellOrigin? Origin { get; private set; }

        public override ValidationResult Validate()
        {
            if (!Directory.Exists(KeyFolder))
            {
                return ValidationResult.Error("--key-folder requires an existing private directory.");
            }

            if (!ShellOrigin.TryParse(Listen, out ShellOrigin? origin))
            {
                return ValidationResult.Error("--listen requires a loopback HTTP origin or a concrete HTTPS origin with a nonzero port.");
            }

            if (origin.Https && string.IsNullOrWhiteSpace(Certificate))
            {
                return ValidationResult.Error("An HTTPS origin requires --certificate <PFX>.");
            }

            Origin = origin;
            return ValidationResult.Success();
        }
    }
}
