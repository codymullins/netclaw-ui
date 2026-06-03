// -----------------------------------------------------------------------
// <copyright file="ToolApprovalReader.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Read-only adapter over <see cref="ToolApprovalStore"/> for the web UI.
/// Security gate: only <c>tool-approvals.json</c> is accessed — never
/// <c>secrets.json</c> or any other privileged path. Quarantine files
/// (<c>.v1.bak</c>, <c>.invalid</c>) are detected and surfaced as warnings
/// in the read result so operators see them in the UI rather than silently
/// getting an empty list.
/// </summary>
public sealed class ToolApprovalReader
{
    private readonly ToolApprovalStore _store;
    private readonly NetclawPaths _paths;

    public ToolApprovalReader(ToolApprovalStore store, NetclawPaths paths)
    {
        _store = store;
        _paths = paths;
    }

    public string ApprovalsPath => _paths.ToolApprovalsPath;

    public ToolApprovalReadResult Read()
    {
        var fileExists = File.Exists(_paths.ToolApprovalsPath);
        var v1Quarantined = File.Exists(_store.V1QuarantinePath);
        var malformedQuarantined = File.Exists(_store.MalformedQuarantinePath);

        // No active file and no quarantine remnants → fresh install with zero approvals.
        if (!fileExists && !v1Quarantined && !malformedQuarantined)
            return ToolApprovalReadResult.Missing(_paths.ToolApprovalsPath);

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ApprovalEntry>>> snapshot;
        try
        {
            snapshot = _store.Snapshot();
        }
        catch (Exception ex)
        {
            return ToolApprovalReadResult.ParseError(_paths.ToolApprovalsPath, ex.Message);
        }

        var warnings = new List<string>();
        if (v1Quarantined)
            warnings.Add(
                $"Legacy v1 approvals file quarantined to '{_store.V1QuarantinePath}' during the v2 schema upgrade. " +
                "Inspect or restore manually if needed; the active store started empty.");
        if (malformedQuarantined)
            warnings.Add(
                $"Malformed approvals file quarantined to '{_store.MalformedQuarantinePath}'. " +
                "The active store was reset to empty after a parse failure. Inspect the .invalid copy before restoring grants.");

        return ToolApprovalReadResult.Ok(_paths.ToolApprovalsPath, snapshot, warnings);
    }
}

public sealed record ToolApprovalReadResult(
    ToolApprovalReadState State,
    string Path,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ApprovalEntry>>>? Snapshot,
    IReadOnlyList<string> Warnings,
    string? ErrorDetail)
{
    public static ToolApprovalReadResult Ok(
        string path,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ApprovalEntry>>> snapshot,
        IReadOnlyList<string> warnings)
        => new(ToolApprovalReadState.Ok, path, snapshot, warnings, null);

    public static ToolApprovalReadResult Missing(string path)
        => new(ToolApprovalReadState.Missing, path, null, [], null);

    public static ToolApprovalReadResult ParseError(string path, string detail)
        => new(ToolApprovalReadState.ParseError, path, null, [], detail);
}

public enum ToolApprovalReadState
{
    Ok,
    Missing,
    ParseError,
}
