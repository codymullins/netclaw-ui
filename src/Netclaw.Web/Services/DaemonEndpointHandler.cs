// -----------------------------------------------------------------------
// <copyright file="DaemonEndpointHandler.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Net.Http.Headers;
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Rewrites every outgoing <see cref="IDaemonApi"/> request onto the
/// <see cref="DaemonTargetStore.EffectiveEndpoint"/> at send time, and attaches the
/// bearer token appropriate for that endpoint. Resolving per request (rather than
/// baking the endpoint into the client at registration) is what lets the operator
/// re-point the UI at a different daemon without restarting it. The Refit client's
/// <c>BaseAddress</c> only supplies the request path; this handler owns scheme,
/// host, port, and auth.
/// </summary>
public sealed class DaemonEndpointHandler : DelegatingHandler
{
    private readonly DaemonTargetStore _targets;
    private readonly NetclawPaths _paths;

    public DaemonEndpointHandler(DaemonTargetStore targets, NetclawPaths paths)
    {
        _targets = targets;
        _paths = paths;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endpoint = _targets.EffectiveEndpoint;

        if (request.RequestUri is { } requestUri
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var target))
        {
            request.RequestUri = new UriBuilder(requestUri)
            {
                Scheme = target.Scheme,
                Host = target.Host,
                Port = target.IsDefaultPort ? -1 : target.Port,
            }.Uri;

            // Loopback endpoints authenticate without a token; non-loopback needs the
            // device token. Resolve per request so a re-point picks up the right posture.
            var token = DaemonControlPlaneEndpointResolver.ResolveBearerToken(endpoint, _paths);
            request.Headers.Authorization = string.IsNullOrWhiteSpace(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
