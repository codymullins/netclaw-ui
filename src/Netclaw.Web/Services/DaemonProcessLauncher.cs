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
    private readonly DaemonTargetStore _targets;

    public DaemonProcessLauncher(NetclawPaths paths, DaemonTargetStore targets)
    {
        _paths = paths;
        _targets = targets;
    }

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
    /// Attempts to locate the <c>netclawd</c> binary.
    /// <para>
    /// When the operator has set a launch override (<see cref="DaemonTargetStore"/>),
    /// that is the <em>only</em> source consulted: an explicit path is used as-is and a
    /// bare name is resolved on PATH. A set-but-unresolvable override returns <c>null</c>
    /// rather than silently falling back to the default daemon — re-pointing the UI at a
    /// dev build must never quietly launch the installed one.
    /// </para>
    /// Without an override the default search order applies:
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
        // Operator launch override is authoritative — no fallback to default discovery.
        var launchOverride = _targets.Current.BinaryPath;
        if (!string.IsNullOrWhiteSpace(launchOverride))
        {
            if (LooksLikePath(launchOverride))
                yield return launchOverride;
            else
                foreach (var resolved in ResolveOnPath(launchOverride))
                    yield return resolved;
            yield break;
        }

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
        foreach (var resolved in ResolveOnPath(BinaryName))
            yield return resolved;
    }

    /// <summary>True when the value names a location (rooted or containing a separator) rather than a bare command.</summary>
    private static bool LooksLikePath(string value) =>
        Path.IsPathRooted(value)
        || value.Contains(Path.DirectorySeparatorChar)
        || value.Contains(Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Yields candidate full paths for a bare command name across every PATH directory.
    /// On Windows the <c>.exe</c> variant is tried too so an override like <c>ncl</c>
    /// resolves to <c>ncl.exe</c>.
    /// </summary>
    private static IEnumerable<string> ResolveOnPath(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            yield break;

        var hasExtension = Path.HasExtension(name);
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(dir, name);
            if (OperatingSystem.IsWindows() && !hasExtension)
                yield return Path.Combine(dir, name + ".exe");
        }
    }

    public DaemonLaunchResult Start()
    {
        if (IsLockFileHeld())
            return new DaemonLaunchResult(false, "Daemon is already running (lock file held).", null);

        var binary = FindDaemonBinary();
        if (binary is null)
        {
            var launchOverride = _targets.Current.BinaryPath;
            var message = !string.IsNullOrWhiteSpace(launchOverride)
                ? $"Launch override '{launchOverride}' could not be resolved. It must be a full path to an " +
                  "existing file or a command name on PATH (a shell alias from your profile won't resolve). " +
                  "Fix it or clear the override on the Daemon control page."
                : $"Cannot find netclawd binary. Looked next to netclaw-web, in the default " +
                  $"install location ({_paths.BinDirectory}), and on PATH. Install netclawd " +
                  "(e.g. via install.sh), set a launch override, or set NETCLAW_DAEMON_PATH to the binary's full path.";
            return new DaemonLaunchResult(false, message, null);
        }

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
