// -----------------------------------------------------------------------
// <copyright file="DaemonClientService.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Net;
using Netclaw.Configuration;
using Refit;

namespace Netclaw.Web.Services;

/// <summary>
/// Single thin client over <see cref="IDaemonApi"/> consumed by every Web page that
/// talks to <c>netclawd</c>. Refit handles the HTTP plumbing (URL composition, JSON
/// (de)serialization, auth header attached at registration time); this service maps
/// Refit outcomes onto the unified <see cref="DaemonApiResult{T}"/> shape so razor
/// pages render identical "ok / unreachable / unauthorized" surfaces regardless of
/// which endpoint they consumed.
/// </summary>
public sealed class DaemonClientService(IDaemonApi api, DaemonTargetStore targets)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StatusPollTimeout = TimeSpan.FromSeconds(5);

    // Read live from the store so result/display surfaces ("unreachable at …") track
    // an operator re-point without the scoped client being recreated.
    public string Endpoint => targets.EffectiveEndpoint;

    // Health + lifecycle

    public Task<DaemonApiResult<DaemonRuntimeStatus.Response>> GetStatusAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.GetStatusAsync, cancellationToken, timeout: StatusPollTimeout);

    public async Task<DaemonStopResult> StopAsync(string reason = "web-stop", CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeout);
        try
        {
            using var response = await api.StopAsync(reason, cts.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized)
                return new DaemonStopResult(false, "Unauthorized — pair this host with the daemon to obtain a device token.");
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                return new DaemonStopResult(false, $"Daemon refused stop ({(int)response.StatusCode}): {body}");
            }
            return new DaemonStopResult(true, "Stop request accepted by daemon.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DaemonStopResult(false, "Timed out contacting daemon.");
        }
        catch (HttpRequestException ex)
        {
            return new DaemonStopResult(false, ex.Message);
        }
    }

    // Discord configuration

    public Task<DaemonApiResult<DiscordConfigWire.GetResponse>> GetDiscordConfigAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.GetDiscordConfigAsync, cancellationToken);

    public Task<DaemonApiResult<DiscordConfigWire.PutResponse>> PutDiscordConfigAsync(
        DiscordConfigWire.PutRequest request,
        CancellationToken cancellationToken = default)
        => RunAsync(ct => api.PutDiscordConfigAsync(request, ct), cancellationToken);

    // Model catalog + selection

    public Task<DaemonApiResult<ModelCatalogWire.GetCatalogResponse>> GetModelCatalogAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.GetModelCatalogAsync, cancellationToken);

    public Task<DaemonApiResult<ModelCatalogWire.GetSelectionResponse>> GetModelSelectionAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.GetModelSelectionAsync, cancellationToken);

    public Task<DaemonApiResult<ModelCatalogWire.PutSelectionResponse>> PutModelSelectionAsync(
        string role,
        ModelCatalogWire.ModelReferenceWire reference,
        CancellationToken cancellationToken = default)
    {
        var request = new ModelCatalogWire.PutSelectionRequest { Role = role, Reference = reference };
        return RunAsync(ct => api.PutModelSelectionAsync(request, ct), cancellationToken);
    }

    // Sessions catalog

    public Task<DaemonApiResult<List<SessionCatalogEntryWire>>> GetSessionsAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.GetSessionsAsync, cancellationToken);

    // Stats

    public Task<DaemonApiResult<DaemonStats.Response>> GetStatsAsync(int days, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.GetStatsAsync(days, ct), cancellationToken);

    public Task<DaemonApiResult<SkillUsageStats.Response>> GetSkillUsageStatsAsync(int days, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.GetSkillUsageStatsAsync(days, ct), cancellationToken);

    // MCP

    public Task<DaemonApiResult<Dictionary<string, McpServerStatusWire>>> GetMcpStatusesAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.GetMcpStatusesAsync, cancellationToken);

    public Task<DaemonApiResult<List<string>>> GetMcpToolsAsync(string name, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.GetMcpToolsAsync(name, ct), cancellationToken);

    public Task<DaemonApiResult<McpOAuthStartWire>> StartMcpOAuthAsync(string name, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.StartMcpOAuthAsync(name, ct), cancellationToken);

    public Task<DaemonApiResult<McpOAuthStatusWire>> GetMcpOAuthStatusAsync(string name, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.GetMcpOAuthStatusAsync(name, ct), cancellationToken);

    // Reminders

    public Task<DaemonApiResult<List<ReminderWire.ListItem>>> ListRemindersAsync(CancellationToken cancellationToken = default)
        => RunAsync(api.ListRemindersAsync, cancellationToken);

    public Task<DaemonApiResult<ReminderWire.CreateResponse>> CreateReminderAsync(
        ReminderWire.CreateRequest request,
        CancellationToken cancellationToken = default)
        => RunAsync(ct => api.CreateReminderAsync(request, ct), cancellationToken);

    public Task<DaemonApiResult<ReminderWire.ValidateResponse>> ValidateReminderAsync(
        ReminderWire.CreateRequest request,
        CancellationToken cancellationToken = default)
        // Validation returns 400 with a body on invalid schedule — treat as Ok so caller can surface the error.
        => RunAsync(ct => api.ValidateReminderAsync(request, ct), cancellationToken, parseBodyOnAnyStatus: true);

    public Task<DaemonApiResult<ReminderWire.DeleteResponse>> DeleteReminderAsync(
        string id,
        bool permanent = true,
        CancellationToken cancellationToken = default)
        => RunAsync(ct => api.DeleteReminderAsync(id, permanent, ct), cancellationToken);

    public Task<DaemonApiResult<ReminderWire.ToggleResponse>> DisableReminderAsync(string id, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.DisableReminderAsync(id, ct), cancellationToken);

    public Task<DaemonApiResult<ReminderWire.ToggleResponse>> EnableReminderAsync(string id, CancellationToken cancellationToken = default)
        => RunAsync(ct => api.EnableReminderAsync(id, ct), cancellationToken);

    public Task<DaemonApiResult<ReminderWire.DetailItem>> GetReminderAsync(string id, CancellationToken cancellationToken = default)
        => RunAsync(
            ct => api.GetReminderAsync(id, ct),
            cancellationToken,
            notFoundAsUnreachable: true,
            notFoundDetail: $"Reminder '{id}' not found.");

    public Task<DaemonApiResult<List<ReminderWire.HistoryRecord>>> GetReminderHistoryAsync(
        string id,
        int last = 20,
        CancellationToken cancellationToken = default)
        => RunAsync(
            ct => api.GetReminderHistoryAsync(id, last, ct),
            cancellationToken,
            notFoundAsUnreachable: true,
            notFoundDetail: $"Reminder '{id}' not found.");

    // Shared result translation

    private async Task<DaemonApiResult<T>> RunAsync<T>(
        Func<CancellationToken, Task<IApiResponse<T>>> call,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        bool parseBodyOnAnyStatus = false,
        bool notFoundAsUnreachable = false,
        string? notFoundDetail = null)
        where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            using var response = await call(cts.Token);

            if (response.StatusCode is HttpStatusCode.Unauthorized)
                return DaemonApiResult<T>.Unauthorized(Endpoint);

            if (notFoundAsUnreachable && response.StatusCode is HttpStatusCode.NotFound)
                return DaemonApiResult<T>.Unreachable(Endpoint, notFoundDetail ?? "Resource not found.");

            if (!parseBodyOnAnyStatus && !response.IsSuccessStatusCode)
            {
                var detail = response.Error?.Content ?? string.Empty;
                return DaemonApiResult<T>.Unreachable(
                    Endpoint,
                    $"Daemon rejected the request ({(int)response.StatusCode}): {Truncate(detail, 400)}");
            }

            return response.Content is null
                ? DaemonApiResult<T>.Unreachable(Endpoint, "Daemon returned an empty response.")
                : DaemonApiResult<T>.Ok(Endpoint, response.Content);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DaemonApiResult<T>.Unreachable(Endpoint, "Timed out contacting daemon.");
        }
        catch (HttpRequestException ex)
        {
            return DaemonApiResult<T>.Unreachable(Endpoint, ex.Message);
        }
        catch (ApiException ex)
        {
            return DaemonApiResult<T>.Unreachable(Endpoint, ex.Message);
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max] + "…";
}
