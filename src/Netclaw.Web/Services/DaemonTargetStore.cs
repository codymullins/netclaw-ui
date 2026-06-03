// -----------------------------------------------------------------------
// <copyright file="DaemonTargetStore.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Text.Json;
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Operator-set, runtime override for which <c>netclawd</c> the UI targets:
/// the control-plane <see cref="DaemonTargetOverride.Endpoint"/> every API call
/// hits, and the <see cref="DaemonTargetOverride.BinaryPath"/> the Start/Restart
/// buttons launch. Both are optional; an unset field falls through to the normal
/// discovery (<see cref="DaemonControlPlaneEndpointResolver"/> for the endpoint,
/// <see cref="DaemonProcessLauncher"/>'s search order for the binary).
/// </summary>
public sealed record DaemonTargetOverride
{
    /// <summary>Absolute <c>http(s)</c> URL of the daemon control plane, or null to use discovery.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Full path to a <c>netclawd</c> binary, or a bare command name on PATH, or null to use discovery.</summary>
    public string? BinaryPath { get; init; }
}

/// <summary>
/// Singleton holding the current <see cref="DaemonTargetOverride"/> and persisting
/// it to <c>~/.netclaw-ui/daemon-override.json</c> so a chosen dev target survives
/// UI restarts. Reads are lock-guarded because the override is consulted from the
/// HTTP send path (<see cref="DaemonEndpointHandler"/>), the status poll loop, and
/// Razor circuits concurrently.
/// </summary>
public sealed class DaemonTargetStore
{
    private readonly NetclawPaths _paths;
    private readonly string _overrideFilePath;
    private readonly object _gate = new();
    private DaemonTargetOverride _current;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Raised after the override changes so live surfaces (topbar, target panel) can refresh.</summary>
    public event Action? Changed;

    public DaemonTargetStore(NetclawPaths paths, string? overrideDirectory = null)
    {
        _paths = paths;
        var dir = overrideDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".netclaw-ui");
        _overrideFilePath = Path.Combine(dir, "daemon-override.json");
        _current = Load();
    }

    public DaemonTargetOverride Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Path of the JSON file the override persists to (surfaced in the UI for transparency).</summary>
    public string OverrideFilePath => _overrideFilePath;

    public bool EndpointIsOverridden => !string.IsNullOrWhiteSpace(Current.Endpoint);

    public bool BinaryIsOverridden => !string.IsNullOrWhiteSpace(Current.BinaryPath);

    /// <summary>
    /// The endpoint the UI should actually talk to: the operator override when set,
    /// otherwise the standard shared resolution (env var → client config → daemon
    /// config → default). The override wins over the env var because it is the most
    /// specific, deliberately-set-right-now intent.
    /// </summary>
    public string EffectiveEndpoint
    {
        get
        {
            var endpoint = Current.Endpoint;
            return string.IsNullOrWhiteSpace(endpoint)
                ? DaemonControlPlaneEndpointResolver.ResolveEndpoint(_paths)
                : endpoint.TrimEnd('/');
        }
    }

    /// <summary>Replace the override (any null/blank field clears that dimension) and persist it.</summary>
    public void Update(DaemonTargetOverride next)
    {
        var normalized = new DaemonTargetOverride
        {
            Endpoint = Blank(next.Endpoint),
            BinaryPath = Blank(next.BinaryPath),
        };

        lock (_gate)
        {
            _current = normalized;
            Save(normalized);
        }

        Changed?.Invoke();
    }

    /// <summary>Clear both overrides, returning the UI to default discovery.</summary>
    public void Reset() => Update(new DaemonTargetOverride());

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DaemonTargetOverride Load()
    {
        if (!File.Exists(_overrideFilePath))
            return new DaemonTargetOverride();

        try
        {
            var json = File.ReadAllText(_overrideFilePath);
            var loaded = JsonSerializer.Deserialize<DaemonTargetOverride>(json, JsonOptions);
            return loaded is null
                ? new DaemonTargetOverride()
                : new DaemonTargetOverride { Endpoint = Blank(loaded.Endpoint), BinaryPath = Blank(loaded.BinaryPath) };
        }
        catch (JsonException) { return new DaemonTargetOverride(); } // slopwatch-ignore: SW003 corrupt override file → treat as unset, not a crash.
        catch (IOException) { return new DaemonTargetOverride(); } // slopwatch-ignore: SW003 unreadable override file → treat as unset.
    }

    private void Save(DaemonTargetOverride value)
    {
        var dir = Path.GetDirectoryName(_overrideFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_overrideFilePath, JsonSerializer.Serialize(value, JsonOptions));
    }
}
