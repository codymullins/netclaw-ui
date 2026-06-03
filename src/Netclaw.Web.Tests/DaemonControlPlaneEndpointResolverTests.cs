// -----------------------------------------------------------------------
// <copyright file="DaemonControlPlaneEndpointResolverTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;
using Xunit;

namespace Netclaw.Web.Tests;

/// <summary>
/// Endpoint resolution is shared by CLI and Web. Drift here means the two
/// clients silently target different daemons.
/// </summary>
[Collection(nameof(EnvVarSerialCollection))]
public sealed class DaemonControlPlaneEndpointResolverTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string? _previousEnv;
    private readonly NetclawPaths _paths;

    public DaemonControlPlaneEndpointResolverTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "netclaw-web-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempHome);
        _paths = new NetclawPaths(_tempHome);
        _paths.EnsureDirectoriesExist();
        _previousEnv = Environment.GetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT");
        Environment.SetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT", null);
    }

    [Fact]
    public void Environment_variable_wins_over_everything()
    {
        Environment.SetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT", "http://env-host:6000/");
        File.WriteAllText(_paths.ClientConfigPath, "{\"Endpoint\":\"http://client-host:7000\"}");

        var resolved = DaemonControlPlaneEndpointResolver.ResolveEndpoint(_paths);

        Assert.Equal("http://env-host:6000", resolved);
    }

    [Fact]
    public void Client_config_used_when_no_env_var()
    {
        File.WriteAllText(_paths.ClientConfigPath, "{\"Endpoint\":\"http://client-host:7000\"}");

        var resolved = DaemonControlPlaneEndpointResolver.ResolveEndpoint(_paths);

        Assert.Equal("http://client-host:7000", resolved);
    }

    [Fact]
    public void Daemon_config_used_when_no_env_var_and_no_client_config()
    {
        File.WriteAllText(_paths.NetclawConfigPath,
            "{\"Daemon\":{\"Host\":\"10.0.0.5\",\"Port\":5300}}");

        var resolved = DaemonControlPlaneEndpointResolver.ResolveEndpoint(_paths);

        Assert.Equal("http://10.0.0.5:5300", resolved);
    }

    [Fact]
    public void Default_used_when_nothing_configured()
    {
        var resolved = DaemonControlPlaneEndpointResolver.ResolveEndpoint(_paths);

        Assert.Equal("http://127.0.0.1:5199", resolved);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NETCLAW_DAEMON_ENDPOINT", _previousEnv);
        try { Directory.Delete(_tempHome, recursive: true); } catch (IOException) { } // slopwatch-ignore: SW003 temp-dir cleanup is best-effort across test runs.
    }
}
