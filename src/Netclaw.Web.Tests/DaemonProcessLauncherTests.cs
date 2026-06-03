// -----------------------------------------------------------------------
// <copyright file="DaemonProcessLauncherTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;
using Netclaw.Web.Services;
using Xunit;

namespace Netclaw.Web.Tests;

/// <summary>
/// The launch override must be authoritative: an explicit path or a PATH name is
/// resolved, and an unresolvable override returns null rather than silently
/// launching the default daemon.
/// </summary>
[Collection(nameof(EnvVarSerialCollection))]
public sealed class DaemonProcessLauncherTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string _overrideDir;
    private readonly string? _previousPath;
    private readonly NetclawPaths _paths;

    public DaemonProcessLauncherTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "netclaw-launcher-tests-" + Guid.NewGuid().ToString("N"));
        _overrideDir = Path.Combine(_tempHome, "ui");
        Directory.CreateDirectory(_tempHome);
        _paths = new NetclawPaths(_tempHome);
        _previousPath = Environment.GetEnvironmentVariable("PATH");
    }

    [Fact]
    public void Override_with_explicit_existing_path_is_used()
    {
        var binary = Path.Combine(_tempHome, "netclawd-dev");
        File.WriteAllText(binary, "#!/bin/sh\n");
        var launcher = LauncherWithOverride(binary);

        Assert.Equal(Path.GetFullPath(binary), launcher.FindDaemonBinary());
    }

    [Fact]
    public void Override_with_missing_path_resolves_to_null()
    {
        var launcher = LauncherWithOverride(Path.Combine(_tempHome, "does-not-exist", "netclawd"));

        Assert.Null(launcher.FindDaemonBinary());
    }

    [Fact]
    public void Override_with_bare_name_resolves_on_path()
    {
        var binDir = Path.Combine(_tempHome, "bin");
        Directory.CreateDirectory(binDir);
        var binary = Path.Combine(binDir, "ncl-dev");
        File.WriteAllText(binary, "#!/bin/sh\n");
        Environment.SetEnvironmentVariable("PATH", binDir);

        var launcher = LauncherWithOverride("ncl-dev");

        Assert.Equal(Path.GetFullPath(binary), launcher.FindDaemonBinary());
    }

    private DaemonProcessLauncher LauncherWithOverride(string binaryPath)
    {
        var targets = new DaemonTargetStore(_paths, _overrideDir);
        targets.Update(new DaemonTargetOverride { BinaryPath = binaryPath });
        return new DaemonProcessLauncher(_paths, targets);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _previousPath);
        try { Directory.Delete(_tempHome, recursive: true); } catch (IOException) { } // slopwatch-ignore: SW003 temp-dir cleanup is best-effort.
    }
}
