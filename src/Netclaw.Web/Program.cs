// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Net.Http.Headers;
using Netclaw.Configuration;
using Netclaw.Web.Components;
using Netclaw.Web.Services;
using Refit;

var builder = WebApplication.CreateBuilder(args);

// Default to loopback. The management UI has no authentication of its own, so an
// operator who wants to expose it beyond loopback must do so explicitly (and at
// their own risk) via ASPNETCORE_URLS — never silently bind a non-loopback address.
if (string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"])
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5198");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<NetclawPaths>(_ => new NetclawPaths());
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Endpoint + bearer token are resolved once at registration time from the same files the CLI
// reads, so every Web call uses the same loopback-friendly auth posture.
builder.Services.AddRefitClient<IDaemonApi>()
    .ConfigureHttpClient((sp, client) =>
    {
        var paths = sp.GetRequiredService<NetclawPaths>();
        var endpoint = DaemonControlPlaneEndpointResolver.ResolveEndpoint(paths);
        client.BaseAddress = new Uri(endpoint);
        var token = DaemonControlPlaneEndpointResolver.ResolveBearerToken(endpoint, paths);
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    });

builder.Services.AddScoped<DaemonClientService>(sp =>
{
    var paths = sp.GetRequiredService<NetclawPaths>();
    var endpoint = DaemonControlPlaneEndpointResolver.ResolveEndpoint(paths);
    return new DaemonClientService(sp.GetRequiredService<IDaemonApi>(), endpoint);
});
builder.Services.AddScoped<DaemonProcessLauncher>();
builder.Services.AddScoped<NetclawConfigReader>();
builder.Services.AddScoped<ModelSelectionReader>();

// ToolApprovalStore MUST be a singleton: it owns the in-memory cache + lock
// over tool-approvals.json. A scoped registration would create one cache per
// circuit and writes from one tab would not invalidate another's view.
builder.Services.AddSingleton(sp =>
    new ToolApprovalStore(
        sp.GetRequiredService<NetclawPaths>().ToolApprovalsPath,
        sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<ToolApprovalReader>();
builder.Services.AddScoped<ToolApprovalWriter>();

// DaemonStatusFeed MUST be a singleton: it is the shared IHostedService
// polling loop and event source backing every page + the topbar (one poll
// per interval regardless of how many components are mounted). It resolves
// DaemonClientService through a per-poll scope to avoid a captive dependency.
builder.Services.AddSingleton<DaemonStatusFeed>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DaemonStatusFeed>());

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

// Marker partial so WebApplicationFactory<Program> can target this assembly.
public partial class Program;
