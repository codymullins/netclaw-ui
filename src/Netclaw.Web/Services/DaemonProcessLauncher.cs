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
    /// Attempts to locate the <c>netclawd</c> binary. Search order:
    /// <list type="number">
    /// <item>the <c>NETCLAW_DAEMON_PATH</c> env var (full path to the binary),</item>
    /// <item>the directory of the current process (side-by-side install),</item>
    /// <item>the default install location — <see cref="NetclawPaths.BinDirectory"/>,
    ///       which is where <c>install.sh</c> drops <c>netclawd</c> (<c>~/.netclaw/bin</c>),</item>
    /// <item>the Windows installer default (<c>%LOCALAPPDATA%\Programs\netclaw</c>),</item>
    /// <item>anywhere on <c>PATH</c>.</item>
    /// </list>
    /// </summary>
    public string? FindDaemonBinary()
    {
        foreach (var candidate in EnumerateBinaryCandidates())
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string BinaryName =>
        OperatingSystem.IsWindows() ? "netclawd.exe" : "netclawd";

    private IEnumerable<string?> EnumerateBinaryCandidates()
    {
        // 1. Explicit override — a full path to the binary.
        yield return Environment.GetEnvironmentVariable("NETCLAW_DAEMON_PATH");

        // 2. Next to netclaw-web (side-by-side install or publish output).
        var webDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (webDir is not null)
            yield return Path.Combine(webDir, BinaryName);

        // 3. Default install location. install.sh installs netclawd into
        //    ~/.netclaw/bin, which is exactly NetclawPaths.BinDirectory (and
        //    follows NETCLAW_HOME when set).
        yield return Path.Combine(_paths.BinDirectory, BinaryName);

        // 4. Windows installer default (%LOCALAPPDATA%\Programs\netclaw).
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                yield return Path.Combine(localAppData, "Programs", "netclaw", BinaryName);
        }

        // 5. Anywhere on PATH.
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return Path.Combine(dir, BinaryName);
        }
    }

    public DaemonLaunchResult Start()
    {
        if (IsLockFileHeld())
            return new DaemonLaunchResult(false, "Daemon is already running (lock file held).", null);

        var binary = FindDaemonBinary();
        if (binary is null)
            return new DaemonLaunchResult(
                false,
                $"Cannot find netclawd binary. Looked next to netclaw-web, in the default " +
                $"install location ({_paths.BinDirectory}), and on PATH. Install netclawd " +
                "(e.g. via install.sh) or set NETCLAW_DAEMON_PATH to the binary's full path.",
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
