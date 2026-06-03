// -----------------------------------------------------------------------
// <copyright file="DaemonProcessLauncher.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Diagnostics;
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Local process control for the <c>netclawd</c> daemon. Mirrors a small
/// subset of <c>Netclaw.Cli.Daemon.DaemonManager</c> — only the bits Web
/// needs (binary discovery, lock-file probe, detached start).
///
/// Stop is intentionally NOT implemented here: it flows through the
/// daemon's own <c>POST /api/lifecycle/stop</c> endpoint via
/// <see cref="DaemonClientService.StopAsync"/> so Web never needs to send OS signals.
/// </summary>
public sealed class DaemonProcessLauncher
{
    private readonly NetclawPaths _paths;

    public DaemonProcessLauncher(NetclawPaths paths) => _paths = paths;

    /// <summary>
    /// Probes the daemon lock file. <c>true</c> means a daemon already holds
    /// the lock and a new process must not be launched.
    /// </summary>
    public bool IsLockFileHeld()
    {
        try
        {
            using var probe = new FileStream(
                _paths.LockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// Attempts to locate the <c>netclawd</c> binary, preferring the
    /// <c>NETCLAW_DAEMON_PATH</c> env var, then the directory of the
    /// current process.
    /// </summary>
    public string? FindDaemonBinary()
    {
        var envPath = Environment.GetEnvironmentVariable("NETCLAW_DAEMON_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return Path.GetFullPath(envPath);

        var webDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (webDir is not null)
        {
            var candidate = OperatingSystem.IsWindows()
                ? Path.Combine(webDir, "netclawd.exe")
                : Path.Combine(webDir, "netclawd");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public DaemonLaunchResult Start()
    {
        if (IsLockFileHeld())
            return new DaemonLaunchResult(false, "Daemon is already running (lock file held).", null);

        var binary = FindDaemonBinary();
        if (binary is null)
            return new DaemonLaunchResult(
                false,
                "Cannot find netclawd binary. Set NETCLAW_DAEMON_PATH or place it next to netclaw-web.",
                null);

        _paths.EnsureDirectoriesExist();

        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Do not redirect stdio — holding pipe handles prevents clean exit.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
                return new DaemonLaunchResult(false, "Process.Start returned null.", null);

            // Quick early-exit check so we can report startup failures synchronously.
            if (process.WaitForExit(1500))
            {
                return new DaemonLaunchResult(
                    false,
                    $"Daemon process exited immediately with code {process.ExitCode}. Check the daemon logs.",
                    null);
            }

            return new DaemonLaunchResult(true, $"Daemon started (PID {process.Id}).", process.Id);
        }
        catch (Exception ex)
        {
            return new DaemonLaunchResult(false, $"Failed to start daemon: {ex.Message}", null);
        }
    }
}

public sealed record DaemonLaunchResult(bool Success, string Message, int? Pid);
