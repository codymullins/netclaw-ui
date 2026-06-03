// -----------------------------------------------------------------------
// <copyright file="DaemonApiResult.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
namespace Netclaw.Web.Services;

/// <summary>
/// Unified outcome of a daemon control-plane call. Every <c>Daemon*Client</c> in
/// <c>Netclaw.Web</c> returns this same shape so razor pages can render an
/// identical "unreachable / unauthorized / ok" surface regardless of which
/// endpoint they consumed.
/// </summary>
public sealed record DaemonApiResult<T>(
    DaemonApiResultState State,
    string Endpoint,
    T? Value,
    string? ErrorDetail)
    where T : class
{
    public static DaemonApiResult<T> Ok(string endpoint, T value)
        => new(DaemonApiResultState.Ok, endpoint, value, null);

    public static DaemonApiResult<T> Unreachable(string endpoint, string? detail)
        => new(DaemonApiResultState.Unreachable, endpoint, null, detail);

    public static DaemonApiResult<T> Unauthorized(string endpoint)
        => new(DaemonApiResultState.Unauthorized, endpoint, null, null);
}

public enum DaemonApiResultState
{
    Ok,
    Unreachable,
    Unauthorized,
}
