// -----------------------------------------------------------------------
// <copyright file="NetclawConfigReaderTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;
using Netclaw.Web.Services;
using Xunit;

namespace Netclaw.Web.Tests;

public sealed class NetclawConfigReaderTests : IDisposable
{
    private readonly string _tempHome;
    private readonly NetclawPaths _paths;

    public NetclawConfigReaderTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "netclaw-web-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempHome);
        _paths = new NetclawPaths(_tempHome);
        _paths.EnsureDirectoriesExist();
    }

    [Fact]
    public void Missing_file_returns_missing_state()
    {
        var reader = new NetclawConfigReader(_paths);

        var result = reader.Read();

        Assert.Equal(NetclawConfigReadState.Missing, result.State);
        Assert.Equal(_paths.NetclawConfigPath, result.Path);
    }

    [Fact]
    public void Existing_file_returns_normalized_json()
    {
        File.WriteAllText(_paths.NetclawConfigPath, "{\"Daemon\":{\"Port\":5199}}");

        var reader = new NetclawConfigReader(_paths);
        var result = reader.Read();

        Assert.Equal(NetclawConfigReadState.Ok, result.State);
        Assert.NotNull(result.Content);
        Assert.Contains("\"Daemon\"", result.Content);
        Assert.Contains("\"Port\": 5199", result.Content);
    }

    [Fact]
    public void Invalid_json_returns_parse_error_state()
    {
        File.WriteAllText(_paths.NetclawConfigPath, "{ not valid json");

        var reader = new NetclawConfigReader(_paths);
        var result = reader.Read();

        Assert.Equal(NetclawConfigReadState.ParseError, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorDetail));
    }

    [Fact]
    public void Reader_exposes_only_the_netclaw_config_path()
    {
        // Web's configuration page must not surface secrets.json or devices.json.
        // The reader hard-binds to NetclawConfigPath.
        var reader = new NetclawConfigReader(_paths);

        Assert.Equal(_paths.NetclawConfigPath, reader.ConfigPath);
        Assert.NotEqual(_paths.SecretsPath, reader.ConfigPath);
        Assert.NotEqual(_paths.DevicesPath, reader.ConfigPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempHome, recursive: true); } catch (IOException) { } // slopwatch-ignore: SW003 temp-dir cleanup is best-effort across test runs.
    }
}
