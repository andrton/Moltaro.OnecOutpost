using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server.Tests;

// Options and a container that pass every check, for tests whose subject is something else. These tests never reach
// the store, so the connection string names a database that need not exist.
internal static class ServerSetup
{
    // A signing key of the form the options require, made once.
    private static readonly string SigningKeyPem = CreateSigningKeyPem();

    // Sets the options that have no default.
    public static void Require(OutpostServerOptions options)
    {
        options.ConnectionString = "Host=localhost;Database=outpost_tests";
        options.SigningKeyPem = SigningKeyPem;
    }

    // Registers the ISignals that the application provides: an in-process SignalHost, which needs no socket.
    public static void AddSignals(IServiceCollection services)
    {
        services.AddSingleton<ISignals>(_ => new SignalHost(new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "test-token",
        }));
    }

    // A container with the signals and the server, whose options the test may change after the required ones are set.
    public static ServiceProvider Create(Action<OutpostServerOptions>? configure = null)
    {
        ServiceCollection services = new();
        AddSignals(services);

        services.AddOutpostServer(options =>
        {
            Require(options);
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    private static string CreateSigningKeyPem()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return key.ExportPkcs8PrivateKeyPem();
    }
}
