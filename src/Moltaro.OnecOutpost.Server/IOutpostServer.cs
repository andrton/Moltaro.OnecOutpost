namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// The server of the tunnel, as an application uses it: it pairs agents, lists and revokes them, releases base keys,
/// and runs Read and Write commands in the bases that the agents serve.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OutpostServiceCollectionExtensions.AddOutpostServer"/> registers it as a singleton. It keeps nothing in
/// memory that another node needs: every fact is in the store, so any node of the application serves any call and any
/// request of an agent, and a node may restart at any time. Every time decision takes its time from
/// <see cref="OutpostServerOptions.TimeProvider"/>.
/// </para>
/// <para>
/// Every member may be called from any thread at any time. An argument error, and
/// <see cref="InvalidOperationException"/> for a misuse, are thrown by the call itself, before anything happens. A
/// failure of the operation is reported by the returned task; a failure of the store or of the signals is an exception
/// of the task, never an outcome. A token that is already cancelled when a method is called gives a cancelled task, and
/// the method changes nothing.
/// </para>
/// <para>
/// A name that a member takes follows its rule: <see langword="null"/> gives <see cref="ArgumentNullException"/>, and
/// any other value that breaks the rule gives <see cref="ArgumentException"/>. The
/// <see cref="ArgumentException.ParamName"/> is the name of the parameter, or of the property for a property of a
/// request.
/// </para>
/// <para>
/// Who may call it is the application's decision: the library authenticates only agents. An application that tests its
/// own use of the server fakes this interface, with the factories of <see cref="OutpostOutcome"/> and the constructor
/// of <see cref="OutpostPairingToken"/>.
/// </para>
/// </remarks>
public interface IOutpostServer
{
    /// <summary>
    /// Creates a pending agent with a name, and a pairing token for it that the administrator of the agent machine
    /// gives the console.
    /// </summary>
    /// <param name="agentName">
    /// The name of the agent, a display label for people: 1 to 100 characters of any kind but control characters,
    /// counted as UTF-16 code units (<see cref="string.Length"/>), with no white space at the start or the end, and no
    /// lone surrogate, a code unit from U+D800 to U+DFFF without its pair, which UTF-8 cannot carry. Names need not be
    /// unique.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The token, with the identity the agent will have once it is paired, and its expiry.</returns>
    /// <remarks>
    /// <para>
    /// The token works once, until <see cref="OutpostServerOptions.PairingTokenLifetime"/> has passed, by the clock of
    /// the node that redeems it. The store keeps its secret until the token is used and deletes it in the same change
    /// that redeems it. A token that expired unused is removed by the next purge, with its pending agent. Revoking the
    /// pending agent ends its token.
    /// </para>
    /// <para>
    /// The token is a credential: whoever holds it in time can pair an agent of their own.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="agentName"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="agentName"/> breaks the rule of agent names.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    Task<OutpostPairingToken> CreatePairingTokenAsync(string agentName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every agent that is not removed: pending, paired and revoked, each with the bases it declared in its newest
    /// poll.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The agents.</returns>
    /// <remarks>
    /// <see cref="OutpostAgent.Connected"/> is judged at the moment of the call, by the clock of the node that answers.
    /// A pending agent is removed, with its token, by the first purge after the token expired unused.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    Task<IReadOnlyList<OutpostAgent>> GetAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an agent: from then on its requests are refused and its polls take nothing, and the base keys it held
    /// are free.
    /// </summary>
    /// <param name="agentId">The identity of the agent.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// <see langword="true"/> when this call revoked the agent; <see langword="false"/> when there is no such agent, or
    /// it was revoked already.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Its commands that no poll took end as <see cref="OutpostOutcomeKindEnum.Refused"/> with
    /// <see cref="OutpostCodes.Revoked"/>. Its taken commands without an outcome end at once: a Write as
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/> with <see cref="OutpostCodes.Revoked"/>, a Read as
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> with <see cref="OutpostCodes.Lost"/>.
    /// </para>
    /// <para>
    /// Revoking a pending agent also ends its token. A revoked agent works again only after a new pairing. A revoked
    /// agent stays in <see cref="GetAgentsAsync"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a base key from the agent that holds it, so that the next agent to declare the key gets it.
    /// </summary>
    /// <param name="baseKey">
    /// The key of the base: 1 to 64 characters, each one of <c>a–z 0–9 - _ .</c>, the first a letter or a digit.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// <see cref="OutpostReleaseResultEnum.Released"/> when the key is free;
    /// <see cref="OutpostReleaseResultEnum.StillDeclared"/> when the agent that holds it still declares it, and nothing
    /// changes; <see cref="OutpostReleaseResultEnum.NotHeld"/> when no agent holds it.
    /// </returns>
    /// <remarks>
    /// A base key belongs to the first agent that declares it, until that agent is revoked or the application releases
    /// the key. Another agent that declares the key is refused that base meanwhile. The release is refused while the
    /// holder still declares the key, so that the scripts of one database never reach another.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="baseKey"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="baseKey"/> breaks the rule of base keys.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    Task<OutpostReleaseResultEnum> ReleaseBaseAsync(string baseKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a Read in a base and returns when its outcome is final.
    /// </summary>
    /// <param name="request">The base, the script, its parameters and the caller key, if any.</param>
    /// <param name="cancellationToken">Ends the wait for the outcome; it does not stop the command.</param>
    /// <returns>The outcome of the command.</returns>
    /// <remarks>
    /// <para>The checks run in this order:</para>
    /// <list type="number">
    /// <item><description>
    /// The arguments, by the rules of names and sizes: <see cref="OutpostReadRequest.BaseKey"/>,
    /// <see cref="OutpostReadRequest.CallerKey"/>, <see cref="OutpostReadRequest.Script"/>,
    /// <see cref="OutpostReadRequest.Parameters"/>, and then the size of the command against
    /// <see cref="OutpostServerOptions.CommandSizeLimit"/>.
    /// </description></item>
    /// <item><description>
    /// The caller key: a key that names an existing command of the base makes the call follow that command, as
    /// <see cref="OutpostReadRequest.CallerKey"/> says. If the purge removes the matching command during the wait,
    /// the call checks the key once more. If the key is free, the remaining checks apply. Two concurrent first calls
    /// with one base and one key create one command, and the other call follows it.
    /// </description></item>
    /// <item><description>
    /// The base: a key that no agent holds is <see cref="OutpostOutcomeKindEnum.Refused"/> with
    /// <see cref="OutpostCodes.UnknownBase"/>, and a mode that the holding agent does not open is
    /// <see cref="OutpostOutcomeKindEnum.Refused"/> with <see cref="OutpostCodes.ModeClosed"/>. The modes are those of
    /// the newest poll of that agent, so a key that this poll no longer declares opens neither.
    /// </description></item>
    /// <item><description>
    /// The agent: one that is not connected gives <see cref="OutpostOutcomeKindEnum.Unavailable"/> at once, and no
    /// command is created.
    /// </description></item>
    /// </list>
    /// <para>
    /// Then the command is created, signed and handed to a poll of the agent, and the call waits for its
    /// outcome. A command that no poll took before its take deadline ends as
    /// <see cref="OutpostOutcomeKindEnum.Expired"/>; nothing is delivered later.
    /// </para>
    /// <para>
    /// A cancelled call ends with <see cref="OperationCanceledException"/> and leaves the command running. An exception
    /// of the task, a cancellation included, does not mean that the command did not run. Repeating the call with the
    /// same caller key follows the first command while it holds that key.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="request"/>, its <see cref="OutpostReadRequest.BaseKey"/> or its
    /// <see cref="OutpostReadRequest.Script"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: a property of <paramref name="request"/> breaks its rule, a lone surrogate in its
    /// <see cref="OutpostReadRequest.Script"/> or in a string of its <see cref="OutpostReadRequest.Parameters"/>
    /// included; its <see cref="OutpostReadRequest.Parameters"/> cannot be written as JSON, such as a number that is
    /// not finite, a value that System.Text.Json cannot write, a nesting deeper than 64 levels or a name twice in one
    /// object, which has the
    /// <see cref="ArgumentException.ParamName"/> <c>Parameters</c>; or the command is larger than
    /// <see cref="OutpostServerOptions.CommandSizeLimit"/>, which has the <see cref="ArgumentException.ParamName"/>
    /// <c>request</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Reported by the task if the command this call created disappears during the wait, or if a command disappears
    /// during the wait after the call checks the caller key again.
    /// </exception>
    Task<OutpostOutcome> ReadAsync(OutpostReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a Write in a base and returns when its outcome is final. The caller key allows at most one run in that
    /// base while the store retains a command holding the key.
    /// </summary>
    /// <param name="request">The base, the caller key, the script and its parameters.</param>
    /// <param name="cancellationToken">Ends the wait for the outcome; it does not stop the command.</param>
    /// <returns>The outcome of the command.</returns>
    /// <remarks>
    /// <para>The checks run in this order:</para>
    /// <list type="number">
    /// <item><description>
    /// The arguments, by the rules of names and sizes: <see cref="OutpostWriteRequest.BaseKey"/>,
    /// <see cref="OutpostWriteRequest.CallerKey"/>, <see cref="OutpostWriteRequest.Script"/>,
    /// <see cref="OutpostWriteRequest.Parameters"/>, and then the size of the command against
    /// <see cref="OutpostServerOptions.CommandSizeLimit"/>.
    /// </description></item>
    /// <item><description>
    /// The caller key: a key that names an existing command of the base makes the call follow that command, as
    /// <see cref="OutpostWriteRequest.CallerKey"/> says. If the purge removes the matching command during the wait,
    /// the call checks the key once more. If the key is free, the remaining checks apply. Two concurrent first calls
    /// with one base and one key create one command, and the other call follows it.
    /// </description></item>
    /// <item><description>
    /// The base: a key that no agent holds is <see cref="OutpostOutcomeKindEnum.Refused"/> with
    /// <see cref="OutpostCodes.UnknownBase"/>, and a mode that the holding agent does not open is
    /// <see cref="OutpostOutcomeKindEnum.Refused"/> with <see cref="OutpostCodes.ModeClosed"/>. The modes are those of
    /// the newest poll of that agent, so a key that this poll no longer declares opens neither.
    /// </description></item>
    /// <item><description>
    /// The agent: one that is not connected gives <see cref="OutpostOutcomeKindEnum.Unavailable"/> at once, and no
    /// command is created.
    /// </description></item>
    /// </list>
    /// <para>
    /// Then the command is created, signed and handed to a poll of the agent, and the call waits for its
    /// outcome. A command that no poll took before its take deadline ends as
    /// <see cref="OutpostOutcomeKindEnum.Expired"/>; nothing is delivered later.
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> means that the Write wrote nothing, because 1C rolled its
    /// transaction back or its script never started, and <see cref="OutpostOutcomeKindEnum.Unknown"/> that the agent
    /// cannot tell whether it committed; the server never turns the outcome of a Write into
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> after the agent reported it.
    /// </para>
    /// <para>
    /// A cancelled call ends with <see cref="OperationCanceledException"/> and leaves the command running. An exception
    /// of the task, a cancellation included, does not mean that the command did not run. Repeating the call with the
    /// same caller key follows the first command while it holds that key. A new key could run the Write twice.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="request"/>, its <see cref="OutpostWriteRequest.BaseKey"/>, its
    /// <see cref="OutpostWriteRequest.CallerKey"/> or its <see cref="OutpostWriteRequest.Script"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: a property of <paramref name="request"/> breaks its rule, a lone surrogate in its
    /// <see cref="OutpostWriteRequest.Script"/> or in a string of its <see cref="OutpostWriteRequest.Parameters"/>
    /// included; its <see cref="OutpostWriteRequest.Parameters"/> cannot be written as JSON, such as a number that is
    /// not finite, a value that System.Text.Json cannot write, a nesting deeper than 64 levels or a name twice in one
    /// object, which has the
    /// <see cref="ArgumentException.ParamName"/> <c>Parameters</c>; or the command is larger than
    /// <see cref="OutpostServerOptions.CommandSizeLimit"/>, which has the <see cref="ArgumentException.ParamName"/>
    /// <c>request</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Reported by the task if the command this call created disappears during the wait, or if a command disappears
    /// during the wait after the call checks the caller key again.
    /// </exception>
    Task<OutpostOutcome> WriteAsync(OutpostWriteRequest request, CancellationToken cancellationToken = default);
}
