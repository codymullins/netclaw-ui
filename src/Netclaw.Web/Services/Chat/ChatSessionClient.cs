// -----------------------------------------------------------------------
// <copyright file="ChatSessionClient.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Microsoft.AspNetCore.SignalR.Client;

namespace Netclaw.Web.Services.Chat;

/// <summary>Connection lifecycle states surfaced to the chat UI.</summary>
public enum ChatConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>A point-in-time view of the hub connection for status display.</summary>
public sealed record ChatConnectionSnapshot(ChatConnectionState State, string? Detail = null);

/// <summary>
/// SignalR client for one daemon-backed chat session — the web counterpart of
/// the CLI's <c>DaemonClient</c>. Connects to <c>{endpoint}/hub/session</c>,
/// binds (or resumes) a session, and relays <c>ReceiveOutput</c> events.
/// </summary>
/// <remarks>
/// The endpoint comes from <see cref="DaemonTargetStore.EffectiveEndpoint"/> at
/// connect time. Loopback daemon connections need no bearer token (matching the
/// rest of this UI's unauthenticated control-plane access), so no token provider
/// is wired up. Events are raised on SignalR worker threads — UI consumers must
/// marshal to the renderer themselves (e.g. <c>InvokeAsync</c>).
/// </remarks>
public sealed class ChatSessionClient : IAsyncDisposable
{
    /// <summary>Wire value of <c>Netclaw.Actors.Channels.ChannelType.SignalR</c>.</summary>
    internal const string ChannelType = "signalr";

    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    private readonly DaemonTargetStore _targets;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HubConnection? _connection;
    private string? _sessionId;
    private ChatConnectionSnapshot _snapshot = new(ChatConnectionState.Disconnected);
    private volatile bool _disposed;

    public ChatSessionClient(DaemonTargetStore targets)
    {
        _targets = targets;
    }

    /// <summary>Raised for every session output event streamed by the daemon.</summary>
    public event Action<SessionOutputDto>? OutputReceived;

    /// <summary>Raised whenever the connection state changes.</summary>
    public event Action<ChatConnectionSnapshot>? ConnectionChanged;

    public ChatConnectionSnapshot Connection => _snapshot;

    public string? SessionId => _sessionId;

    /// <summary>
    /// Connects to the daemon hub and binds a session: resumes
    /// <paramref name="resumeSessionId"/> when given (or a previously bound
    /// session on retry), otherwise creates a new one. Returns the session id.
    /// </summary>
    public async Task<string> StartSessionAsync(
        string? resumeSessionId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (resumeSessionId is not null)
                _sessionId = resumeSessionId;

            var connection = await EnsureConnectedAsync(cancellationToken);
            return await EnsureSessionAsync(connection, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var (connection, sessionId) = RequireSession();
        await connection.InvokeCoreAsync("SendMessage", [sessionId, text], cancellationToken);
    }

    public async Task RespondToInteractionAsync(
        string callId,
        string selectedKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedKey);
        var (connection, sessionId) = RequireSession();
        await connection.InvokeCoreAsync(
            "RespondToInteraction", [sessionId, callId, selectedKey], cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_connection is { } connection)
        {
            _connection = null;
            await connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    private (HubConnection Connection, string SessionId) RequireSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = _connection
            ?? throw new InvalidOperationException("Not connected. Call StartSessionAsync first.");
        var sessionId = _sessionId
            ?? throw new InvalidOperationException("No session bound. Call StartSessionAsync first.");
        return (connection, sessionId);
    }

    private async Task<HubConnection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { State: HubConnectionState.Connected } live)
            return live;

        // The endpoint can be re-pointed at runtime via DaemonTargetStore, so a
        // stale connection (built against an old endpoint, or dead) is replaced
        // rather than restarted.
        if (_connection is { } stale)
        {
            _connection = null;
            await stale.DisposeAsync();
        }

        var hubUrl = $"{_targets.EffectiveEndpoint.TrimEnd('/')}/hub/session";
        Publish(new(ChatConnectionState.Connecting, $"Connecting to {hubUrl}..."));

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(ReconnectDelays)
            .Build();

        connection.On<SessionOutputDto>("ReceiveOutput", dto => OutputReceived?.Invoke(dto));

        connection.Reconnecting += ex =>
        {
            Publish(new(
                ChatConnectionState.Reconnecting,
                $"Connection lost ({ex?.Message ?? "transport dropped"}). Reconnecting..."));
            return Task.CompletedTask;
        };

        connection.Reconnected += async _ =>
        {
            if (_disposed)
                return;

            // Re-bind the session before announcing Connected so consumers never
            // observe a live transport with a dead session attachment.
            try
            {
                await EnsureSessionAsync(connection, CancellationToken.None);
                Publish(new(ChatConnectionState.Connected, "Reconnected."));
            }
            catch (Exception ex)
            {
                Publish(new(
                    ChatConnectionState.Disconnected,
                    $"Reconnected but session re-attach failed: {ex.Message}"));
            }
        };

        connection.Closed += ex =>
        {
            if (!_disposed)
            {
                // Built-in auto-reconnect has given up. The chat page surfaces a
                // manual retry (StartSessionAsync) rather than looping forever.
                Publish(new(
                    ChatConnectionState.Disconnected,
                    ex?.Message ?? "Connection closed."));
            }

            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await connection.DisposeAsync();
            Publish(new(ChatConnectionState.Disconnected, $"Could not reach the daemon: {ex.Message}"));
            throw;
        }

        _connection = connection;
        Publish(new(ChatConnectionState.Connected));
        return connection;
    }

    private async Task<string> EnsureSessionAsync(
        HubConnection connection, CancellationToken cancellationToken)
    {
        // EnsureSession creates a session when the id is null and attaches
        // (rehydrating if needed) when it names an existing one.
        var result = await connection.InvokeCoreAsync<SessionEnsureResultDto>(
            "EnsureSession", [_sessionId, ChannelType], cancellationToken);

        _sessionId = result.SessionId;
        return result.SessionId;
    }

    private void Publish(ChatConnectionSnapshot snapshot)
    {
        _snapshot = snapshot;
        ConnectionChanged?.Invoke(snapshot);
    }
}
