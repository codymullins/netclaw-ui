// -----------------------------------------------------------------------
// <copyright file="IDaemonApi.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;
using Refit;

namespace Netclaw.Web.Services;

/// <summary>
/// Refit-generated typed client for every <c>netclawd</c> control-plane endpoint
/// consumed by <c>Netclaw.Web</c>. Returning <see cref="IApiResponse{T}"/> keeps
/// non-2xx outcomes out of the exception path so <see cref="DaemonClientService"/>
/// can map them onto <see cref="DaemonApiResult{T}"/>'s three-state shape.
/// </summary>
public interface IDaemonApi
{
    // Health + lifecycle
    [Get("/api/health/status")]
    Task<IApiResponse<DaemonRuntimeStatus.Response>> GetStatusAsync(CancellationToken cancellationToken);

    [Post("/api/lifecycle/stop")]
    Task<HttpResponseMessage> StopAsync([Query] string reason, CancellationToken cancellationToken);

    // Discord configuration
    [Get("/api/config/discord")]
    Task<IApiResponse<DiscordConfigWire.GetResponse>> GetDiscordConfigAsync(CancellationToken cancellationToken);

    [Put("/api/config/discord")]
    Task<IApiResponse<DiscordConfigWire.PutResponse>> PutDiscordConfigAsync(
        [Body] DiscordConfigWire.PutRequest request,
        CancellationToken cancellationToken);

    // Model catalog + selection
    [Get("/api/models")]
    Task<IApiResponse<ModelCatalogWire.GetCatalogResponse>> GetModelCatalogAsync(CancellationToken cancellationToken);

    [Get("/api/model/selection")]
    Task<IApiResponse<ModelCatalogWire.GetSelectionResponse>> GetModelSelectionAsync(CancellationToken cancellationToken);

    [Put("/api/model/selection")]
    Task<IApiResponse<ModelCatalogWire.PutSelectionResponse>> PutModelSelectionAsync(
        [Body] ModelCatalogWire.PutSelectionRequest request,
        CancellationToken cancellationToken);

    // Sessions catalog
    [Get("/api/sessions")]
    Task<IApiResponse<List<SessionCatalogEntryWire>>> GetSessionsAsync(CancellationToken cancellationToken);

    // Stats
    [Get("/api/stats")]
    Task<IApiResponse<DaemonStats.Response>> GetStatsAsync([Query] int days, CancellationToken cancellationToken);

    [Get("/api/stats/skills")]
    Task<IApiResponse<SkillUsageStats.Response>> GetSkillUsageStatsAsync([Query] int days, CancellationToken cancellationToken);

    // MCP
    [Get("/api/mcp/statuses")]
    Task<IApiResponse<Dictionary<string, McpServerStatusWire>>> GetMcpStatusesAsync(CancellationToken cancellationToken);

    [Get("/api/mcp/tools/{name}")]
    Task<IApiResponse<List<string>>> GetMcpToolsAsync(string name, CancellationToken cancellationToken);

    [Post("/api/mcp/oauth/start/{name}")]
    Task<IApiResponse<McpOAuthStartWire>> StartMcpOAuthAsync(string name, CancellationToken cancellationToken);

    [Get("/api/mcp/oauth/status/{name}")]
    Task<IApiResponse<McpOAuthStatusWire>> GetMcpOAuthStatusAsync(string name, CancellationToken cancellationToken);

    // Reminders
    [Get("/api/reminders")]
    Task<IApiResponse<List<ReminderWire.ListItem>>> ListRemindersAsync(CancellationToken cancellationToken);

    [Post("/api/reminders")]
    Task<IApiResponse<ReminderWire.CreateResponse>> CreateReminderAsync(
        [Body] ReminderWire.CreateRequest request,
        CancellationToken cancellationToken);

    [Post("/api/reminders/validate")]
    Task<IApiResponse<ReminderWire.ValidateResponse>> ValidateReminderAsync(
        [Body] ReminderWire.CreateRequest request,
        CancellationToken cancellationToken);

    [Delete("/api/reminders/{id}")]
    Task<IApiResponse<ReminderWire.DeleteResponse>> DeleteReminderAsync(
        string id,
        [Query] bool permanent,
        CancellationToken cancellationToken);

    [Post("/api/reminders/{id}/disable")]
    Task<IApiResponse<ReminderWire.ToggleResponse>> DisableReminderAsync(string id, CancellationToken cancellationToken);

    [Post("/api/reminders/{id}/enable")]
    Task<IApiResponse<ReminderWire.ToggleResponse>> EnableReminderAsync(string id, CancellationToken cancellationToken);

    [Get("/api/reminders/{id}")]
    Task<IApiResponse<ReminderWire.DetailItem>> GetReminderAsync(string id, CancellationToken cancellationToken);

    [Get("/api/reminders/{id}/history")]
    Task<IApiResponse<List<ReminderWire.HistoryRecord>>> GetReminderHistoryAsync(
        string id,
        [Query] int last,
        CancellationToken cancellationToken);
}
