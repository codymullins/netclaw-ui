// -----------------------------------------------------------------------
// <copyright file="DaemonWireTypes.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
namespace Netclaw.Web.Services;

/// <summary>Wire DTO for a single server entry from <c>GET /api/mcp/statuses</c>.</summary>
public sealed class McpServerStatusWire
{
    public string State { get; set; } = string.Empty;
    public int ToolCount { get; set; }
    public string? Error { get; set; }
}

/// <summary>Wire DTO for <c>POST /api/mcp/oauth/start/{name}</c>.</summary>
public sealed class McpOAuthStartWire
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

/// <summary>Wire DTO for <c>GET /api/mcp/oauth/status/{name}</c>.</summary>
public sealed class McpOAuthStatusWire
{
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Wire DTO for session catalog entries returned by <c>GET /api/sessions</c>.
/// Mirrors <c>SessionCatalogEntry</c> in the daemon but lives in the Web project to
/// avoid a cross-project dependency on the daemon assembly.
/// </summary>
public sealed class SessionCatalogEntryWire
{
    public required string PersistenceId { get; init; }
    public required string Channel { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public required string Status { get; init; }
    public required int TurnCount { get; init; }
    public required long CreatedAt { get; init; }
    public required long LastActivity { get; init; }
    public string? LogPath { get; init; }
    public long? LastInputTokens { get; init; }

    /// <summary>
    /// Returns the human-readable session thread id by stripping the
    /// <c>session-</c> persistence prefix. For Slack/Discord sessions this
    /// yields the <c>{channelId}/{threadTs}</c> identity.
    /// </summary>
    public string ThreadId => PersistenceId.StartsWith("session-", StringComparison.Ordinal)
        ? PersistenceId["session-".Length..]
        : PersistenceId;
}

/// <summary>
/// Wire types for the /api/reminders surface. Mirrors the anonymous projections returned by
/// <c>ReminderEndpointRouteBuilderExtensions</c> on the daemon side.
/// </summary>
public static class ReminderWire
{
    public sealed record ListItem(
        string Id,
        string Title,
        bool Enabled,
        string Schedule,
        string? NextFire,
        string? ExpiresAt,
        string? Audience);

    public sealed record DetailItem(
        string Id,
        string Title,
        bool Enabled,
        string Schedule,
        string? NextFire,
        string? ExpiresAt,
        string? Instructions,
        string DeliveryKind,
        string? DeliveryTransport,
        string? DeliveryAddress,
        bool DeliveryRequired,
        string? DeliveryInstructions,
        string? Audience);

    public sealed record CreateRequest
    {
        public string? Id { get; init; }
        public required string Name { get; init; }
        public required string Prompt { get; init; }
        public required string ScheduleType { get; init; }
        public required string Schedule { get; init; }
        public string? DeliveryKind { get; init; }
        public string? DeliveryTransport { get; init; }
        public string? DeliveryAddress { get; init; }
        public bool DeliveryRequired { get; init; } = true;
        public string? DeliveryInstructions { get; init; }
        public string? Audience { get; init; }
        public string? ExpiresIn { get; init; }
    }

    public sealed record CreateResponse(string? Message, string? Error);

    public sealed record ValidateResponse(bool Valid, string? Error, string? ScheduleType, DateTimeOffset? NextFire);

    public sealed record DeleteResponse(string? Message, string? Error);

    public sealed record ToggleResponse(string? Id, bool Enabled, string? NextFire, string? Message, string? Error);

    public sealed record HistoryRecord(
        DateTimeOffset FiredAt,
        bool Success,
        long DurationMs,
        string SessionId,
        string? ErrorMessage);
}

public sealed record DaemonStopResult(bool Success, string Message);
