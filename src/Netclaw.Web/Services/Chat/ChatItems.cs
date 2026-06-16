// -----------------------------------------------------------------------
// <copyright file="ChatItems.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
namespace Netclaw.Web.Services.Chat;

/// <summary>A renderable entry in the chat transcript.</summary>
public abstract class ChatItem;

/// <summary>A message the operator sent (or a replayed historic user message).</summary>
public sealed class UserMessageItem : ChatItem
{
    public required string Text { get; init; }

    /// <summary>True when replayed from session history rather than typed live.</summary>
    public bool IsHistoric { get; init; }
}

/// <summary>
/// An assistant message. While <see cref="IsStreaming"/> is true, <see cref="Text"/>
/// grows as <c>text_delta</c> events append to it.
/// </summary>
public sealed class AssistantMessageItem : ChatItem
{
    public string Text { get; set; } = string.Empty;

    public bool IsStreaming { get; set; }

    public bool IsHistoric { get; init; }
}

/// <summary>
/// A reasoning block. Streams via <c>thinking_delta</c> while open; closes
/// (and collapses) when answer text or a tool call begins.
/// </summary>
public sealed class ThinkingItem : ChatItem
{
    public string Text { get; set; } = string.Empty;

    public bool IsStreaming { get; set; }

    /// <summary>Operator-toggled disclosure state; auto-collapses on close.</summary>
    public bool Expanded { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// A tool invocation. Created running by <c>tool_call</c>, completed in place
/// by the matching <c>tool_result</c>.
/// </summary>
public sealed class ToolCallItem : ChatItem
{
    public required string CallId { get; init; }

    public required string ToolName { get; init; }

    public string? ArgumentsJson { get; init; }

    public string? Result { get; set; }

    public bool IsComplete { get; set; }

    /// <summary>Operator-toggled disclosure of the full input/result payloads.</summary>
    public bool Expanded { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// A subagent run. Created running by the <c>started</c> phase, completed in
/// place by the matching <c>completed</c> phase.
/// </summary>
public sealed class SubAgentItem : ChatItem
{
    public required string AgentName { get; init; }

    public bool IsComplete { get; set; }

    public bool? Success { get; set; }

    public int? ToolCount { get; set; }

    public TimeSpan? Duration { get; set; }

    public string? MemoryDecision { get; set; }

    public int? FindingsCount { get; set; }
}

/// <summary>A file the agent produced (<c>file</c> output).</summary>
public sealed class FileArtifactItem : ChatItem
{
    public required string FileName { get; init; }

    public string? FilePath { get; init; }

    public string? MimeType { get; init; }
}

/// <summary>A failed turn (<c>error</c> output) with daemon diagnostics.</summary>
public sealed class ErrorItem : ChatItem
{
    public required string Message { get; init; }

    public string? Detail { get; init; }

    public string? Category { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>Category of an inline notice line.</summary>
public enum NoticeKind
{
    Session,
    Compaction,
    Approval,
}

/// <summary>An inline system notice (session markers, compaction, approval outcomes).</summary>
public sealed class NoticeItem : ChatItem
{
    public required NoticeKind Kind { get; init; }

    public required string Text { get; init; }

    /// <summary>Secondary line (e.g. the approved command) rendered in monospace.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// A pending tool-approval prompt awaiting an operator decision. Held in the
/// approval queue, not the transcript; the outcome lands as a <see cref="NoticeItem"/>.
/// </summary>
public sealed record PendingApproval(
    string CallId,
    string ToolName,
    string DisplayText,
    IReadOnlyList<string> Patterns,
    string? Cwd,
    IReadOnlyList<ToolInteractionOption> Options,
    bool IsMessy = false,
    bool HasThirdPartyAdoptedContext = false);
