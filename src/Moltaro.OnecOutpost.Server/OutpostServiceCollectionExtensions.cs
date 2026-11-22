using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moltaro.OnecOutpost.Server.Endpoints;
using Moltaro.OnecOutpost.Server.Purge;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// Registers the server in the service container of an ASP.NET Core application.
/// </summary>
public static class OutpostServiceCollectionExtensions
{
    // The one category of the library's log, part of the public contract.
    private const string LogCategory = "Moltaro.OnecOutpost.Server";

    /// <summary>
    /// Registers <see cref="IOutpostServer"/> as a singleton, the store, and a hosted service that removes what has
    /// outlived its retention every <see cref="OutpostServerOptions.PurgeInterval"/>.
    /// </summary>
    /// <param name="services">The container to register in.</param>
    /// <param name="configure">Fills the options; it runs once, on a new object, during this call.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <remarks>
    /// <para>
    /// The call validates the options as <see cref="OutpostServerOptions"/> says, the signing key included, and keeps a
    /// copy: a later change of the object given to <paramref name="configure"/> has no effect.
    /// </para>
    /// <para>
    /// The server needs an <see cref="ISignals"/> of Moltaro.Signals in the container, which the application registers,
    /// such as an in-process <see cref="SignalHost"/> on one node, or a host in one process and clients in the others.
    /// The library never registers one. Resolving <see cref="IOutpostServer"/> without one gives
    /// <see cref="InvalidOperationException"/> that says so. The library opens no subscription yet; once Read and Write
    /// work, it opens one per waiting poll and per waiting call, so the application sizes the subscription limit of its
    /// Signals host for its agents and its concurrent calls.
    /// </para>
    /// <para>
    /// The store has to exist in <see cref="OutpostServerOptions.Schema"/>: the application applies the migrations with
    /// <see cref="OutpostMigrations"/>. <see cref="OutpostEndpointRouteBuilderExtensions.MapOutpostAgents"/> then maps
    /// the requests of the agents.
    /// </para>
    /// <para>
    /// The hosted service purges once when the host starts, and then after every
    /// <see cref="OutpostServerOptions.PurgeInterval"/>, on every node. A purge that fails for anything but the stop of
    /// the host is logged as one warning, with its exception, under the log category <c>Moltaro.OnecOutpost.Server</c>,
    /// and the next purge comes after the interval, so that a store that is down does not stop the host. That warning
    /// is the only entry the library writes in its log.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the call: it was made before on the same collection. <paramref name="configure"/> does not run then.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown by the call: a number or a <see cref="TimeSpan"/> of the options is outside its range. The
    /// <see cref="ArgumentException.ParamName"/> is the name of the property.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: any other value of the options is wrong, such as a required property left
    /// <see langword="null"/>, a schema or a prefix that breaks its rule, or a signing key that is not exactly one
    /// PKCS#8 PEM block of an ECDSA P-256 private key. The <see cref="ArgumentException.ParamName"/> is the name of the
    /// property.
    /// </exception>
    public static IServiceCollection AddOutpostServer(
        this IServiceCollection services,
        Action<OutpostServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // The concrete server is registered only here, so its presence tells a second call, whatever else the
        // application registered as IOutpostServer.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(OutpostServer)))
        {
            throw new InvalidOperationException("AddOutpostServer was already called on this service collection.");
        }

        OutpostServerOptions options = new();
        configure(options);

        OutpostServerOptions validated = options.Copy();
        validated.Validate();

        // Made once, here, from the validated values; Validate has checked that neither is null. Every part of the
        // server below takes them from the container.
        services.AddSingleton(new OutpostStore(validated.ConnectionString!, validated.Schema, validated.TimeProvider));
        services.AddSingleton(new ServerSigningKey(validated.SigningKeyPem!));

        services.AddSingleton(provider => new OutpostServer(
            validated,
            GetSignals(provider),
            provider.GetRequiredService<OutpostStore>(),
            provider.GetRequiredService<ServerSigningKey>()));

        services.AddSingleton<IOutpostServer>(provider => provider.GetRequiredService<OutpostServer>());

        services.AddSingleton(provider => new PairHandler(
            provider.GetRequiredService<OutpostStore>(),
            provider.GetRequiredService<ServerSigningKey>(),
            validated));

        services.AddSingleton(provider => new PollHandler(
            provider.GetRequiredService<OutpostStore>(),
            provider.GetRequiredService<ServerSigningKey>(),
            validated,
            provider.GetRequiredService<IHostApplicationLifetime>()));

        services.AddSingleton(provider => new OutcomeHandler(provider.GetRequiredService<OutpostStore>(), validated));

        services.AddHostedService(provider => new PurgeService(
            provider.GetRequiredService<OutpostStore>(),
            validated.PurgeInterval,
            validated.TimeProvider,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory)));

        return services;
    }

    private static ISignals GetSignals(IServiceProvider provider)
    {
        ISignals? signals = provider.GetService<ISignals>();

        if (signals is null)
        {
            throw new InvalidOperationException(
                "The Outpost server needs an ISignals of Moltaro.Signals in the container, such as an in-process SignalHost; the application registers it, since AddOutpostServer never does.");
        }

        return signals;
    }
}
