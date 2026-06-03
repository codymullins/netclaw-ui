// -----------------------------------------------------------------------
// <copyright file="ToolApprovalWriter.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Write adapter over <see cref="ToolApprovalStore"/> for the web UI.
/// Only revocation operations are exposed — grants are issued exclusively
/// through the in-session actor flow and must never be created from the web.
/// All write paths surface errors as result values so the caller can display
/// them without crashing the render cycle.
/// </summary>
public sealed class ToolApprovalWriter
{
    private readonly ToolApprovalStore _store;

    public ToolApprovalWriter(ToolApprovalStore store) => _store = store;

    /// <summary>
    /// Removes a single approval entry for the given audience and tool.
    /// </summary>
    public ToolApprovalWriteResult RevokeEntry(TrustAudience audience, string toolName, ApprovalEntry entry)
    {
        try
        {
            var removed = _store.RemoveApproval(audience, toolName, entry);
            return ToolApprovalWriteResult.Ok(removed);
        }
        catch (Exception ex)
        {
            return ToolApprovalWriteResult.Error(ex.Message);
        }
    }

    /// <summary>
    /// Removes every approval entry for the given audience and tool.
    /// </summary>
    public ToolApprovalWriteResult RevokeAllForTool(TrustAudience audience, string toolName)
    {
        try
        {
            var count = _store.RemoveAllForTool(audience, toolName);
            return ToolApprovalWriteResult.Ok(count > 0);
        }
        catch (Exception ex)
        {
            return ToolApprovalWriteResult.Error(ex.Message);
        }
    }

    /// <summary>
    /// Removes every approval entry for all tools in the given audience.
    /// Iterates the current snapshot to discover tool names, then revokes
    /// each. Safe to call when the audience has no entries — returns
    /// <c>Changed = false</c> in that case.
    /// </summary>
    public ToolApprovalWriteResult RevokeAllForAudience(TrustAudience audience)
    {
        try
        {
            var snapshot = _store.Snapshot();
            var audienceKey = audience.ToWireValue();
            var totalRemoved = 0;

            if (snapshot.TryGetValue(audienceKey, out var tools))
            {
                // Copy keys before iterating — RemoveAllForTool mutates the
                // underlying store, which invalidates the snapshot's live data
                // on same-process writes. Keys are stable strings.
                foreach (var toolName in tools.Keys.ToList())
                    totalRemoved += _store.RemoveAllForTool(audience, toolName);
            }

            return ToolApprovalWriteResult.Ok(totalRemoved > 0);
        }
        catch (Exception ex)
        {
            return ToolApprovalWriteResult.Error(ex.Message);
        }
    }
}

public sealed record ToolApprovalWriteResult(bool Changed, string? ErrorDetail)
{
    public bool IsError => ErrorDetail is not null;

    public static ToolApprovalWriteResult Ok(bool changed) => new(changed, null);
    public static ToolApprovalWriteResult Error(string detail) => new(false, detail);
}
