// -----------------------------------------------------------------------
// <copyright file="StatusTone.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace Netclaw.Web.Components;

/// <summary>
/// Semantic status tones used to drive Northstar design-system color
/// modifiers (.nc-pill--*, .nc-card--status-*, .nc-nav-dot--*).
/// </summary>
/// <remarks>
/// The orange interactive accent (--nc-accent) is deliberately not a
/// status tone — status colors come from --nc-good / --nc-warn /
/// --nc-bad / muted neutral only. See netclaw-web-ui spec, "Status
/// color tone is semantic".
/// </remarks>
public enum StatusToneKind
{
    Good,
    Warn,
    Bad,
    Neutral
}

public static class StatusTone
{
    public static StatusToneKind From(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return StatusToneKind.Neutral;

        return status.Trim().ToLowerInvariant() switch
        {
            "healthy" or "ok" or "connected" or "running" or "good"
                => StatusToneKind.Good,
            "degraded" or "warn" or "warning" or "restarting" or "reconnecting"
                => StatusToneKind.Warn,
            "unhealthy" or "stopped" or "failed" or "error" or "bad" or "down"
                => StatusToneKind.Bad,
            _ => StatusToneKind.Neutral,
        };
    }

    public static string PillClass(StatusToneKind tone) => tone switch
    {
        StatusToneKind.Good => "nc-pill nc-pill--good",
        StatusToneKind.Warn => "nc-pill nc-pill--warn",
        StatusToneKind.Bad => "nc-pill nc-pill--bad",
        _ => "nc-pill nc-pill--neutral",
    };

    public static string CardStatusClass(StatusToneKind tone) => tone switch
    {
        StatusToneKind.Good => "nc-card nc-card--kpi nc-card--status-good",
        StatusToneKind.Warn => "nc-card nc-card--kpi nc-card--status-warn",
        StatusToneKind.Bad => "nc-card nc-card--kpi nc-card--status-bad",
        _ => "nc-card nc-card--kpi",
    };

    public static string DotClass(StatusToneKind tone) => tone switch
    {
        StatusToneKind.Good => "nc-nav-dot nc-nav-dot--good",
        StatusToneKind.Warn => "nc-nav-dot nc-nav-dot--warn",
        StatusToneKind.Bad => "nc-nav-dot nc-nav-dot--bad",
        _ => "nc-nav-dot nc-nav-dot--neutral",
    };
}
