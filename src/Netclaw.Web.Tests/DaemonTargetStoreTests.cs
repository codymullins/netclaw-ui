// -----------------------------------------------------------------------
// <copyright file="DaemonTargetStoreTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;
using Netclaw.Web.Services;
using Xunit;

namespace Netclaw.Web.Tests;

/// <summary>
/// The override store decides which daemon the UI targets. A regression here
/// silently re-points API traffic or the launcher at the wrong daemon.
/// </summary>
[Collection(nameof(EnvVarSerialCollection))]
public sealed class DaemonTargetStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string _overrideDir;
    private readonly string? _previousEndpointEnv;
    private readonly NetclawPaths _paths;

    public DaemonTargetStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "netclaw-target-tests-" + Guid.NewGuid().ToString("N"));
        _overrideDir = Path.Combine(_tempHome, "ui");
        Directory.CreateDirectory(_tempHome);
        _paths = new NetclawPaths(_tempHome);
        _paths.EnsureDirectoriesExist();
        _previousEndpointEnv = Environment.GetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT");
        Environment.SetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT", null);
    }

    [Fact]
    public void Effective_endpoint_falls_back_to_resolver_when_no_override()
    {
        var store = new DaemonTargetStore(_paths, _overrideDir);

        Assert.False(store.EndpointIsOverridden);
        Assert.Equal("http://127.0.0.1:5199", store.EffectiveEndpoint);
    }

    [Fact]
    public void Endpoint_override_wins_over_env_var()
    {
        Environment.SetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT", "http://env-host:6000");
        var store = new DaemonTargetStore(_paths, _overrideDir);

        store.Update(new DaemonTargetOverride { Endpoint = "http://dev-host:5210/" });

        Assert.True(store.EndpointIsOverridden);
        Assert.Equal("http://dev-host:5210", store.EffectiveEndpoint);
    }

    [Fact]
    public void Override_persists_across_instances()
    {
        var first = new DaemonTargetStore(_paths, _overrideDir);
        first.Update(new DaemonTargetOverride { Endpoint = "http://dev-host:5210", BinaryPath = "/tmp/netclawd" });

        var second = new DaemonTargetStore(_paths, _overrideDir);

        Assert.Equal("http://dev-host:5210", second.EffectiveEndpoint);
        Assert.Equal("/tmp/netclawd", second.Current.BinaryPath);
    }

    [Fact]
    public void Blank_fields_are_normalized_to_unset()
    {
        var store = new DaemonTargetStore(_paths, _overrideDir);

        store.Update(new DaemonTargetOverride { Endpoint = "   ", BinaryPath = "" });

        Assert.False(store.EndpointIsOverridden);
        Assert.False(store.BinaryIsOverridden);
        Assert.Null(store.Current.Endpoint);
    }

    [Fact]
    public void Reset_clears_override_and_raises_changed()
    {
        var store = new DaemonTargetStore(_paths, _overrideDir);
        store.Update(new DaemonTargetOverride { Endpoint = "http://dev-host:5210" });

        var raised = false;
        store.Changed += () => raised = true;
        store.Reset();

        Assert.True(raised);
        Assert.False(store.EndpointIsOverridden);
        Assert.Equal("http://127.0.0.1:5199", store.EffectiveEndpoint);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT", _previousEndpointEnv);
        try { Directory.Delete(_tempHome, recursive: true); } catch (IOException) { } // slopwatch-ignore: SW003 temp-dir cleanup is best-effort.
    }
}
