// -----------------------------------------------------------------------
// <copyright file="SessionOutputDto.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
namespace Netclaw.Web.Services.Chat;

// MIRROR TYPES: these records duplicate the wire shape of
// netclaw/src/Netclaw.Actors/Protocol/SessionOutputDto.cs rather than
// referencing Netclaw.Actors, which would drag the whole agent runtime
// (Akka, MCP, protobuf, ...) into the web app for four small records.
// SignalR carries plain JSON, so only property names and shapes matter.
// SessionOutputDtoMirrorTests asserts these stay in sync with the
// submodule's originals — a submodule bump that changes the contract
// fails tests instead of silently dropping fields at runtime.

/// <summary>
/// Wire discriminator strings for <see cref="SessionOutputDto.Type"/>.
/// </summary>
public static class SessionOutputTypes
{
    public const string Text = "text";
    public const string TextDelta = "text_delta";
    public const string Thinking = "thinking";
    public const string ThinkingDelta = "thinking_delta";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Usage = "usage";
    public const string TurnCompleted = "turn_completed";
    public const string SessionTitle = "session_title";
    public const string Error = "error";
    public const string File = "file";
    public const string SubAgent = "subagent";
    public const string BufferFlush = "buffer_flush";
    public const string Compaction = "compaction";
    public const string SessionJoined = "session_joined";
    public const string ToolInteraction = "tool_interaction";
    public const string Unknown = "unknown";
}

/// <summary>
/// A chat message carried on the wire (role + content only). Used to replay
/// recent history when resuming a session.
/// </summary>
public sealed record ChatMessageDto(string Role, string Content);

/// <summary>
/// A selectable option on a tool-interaction (approval) prompt. The upstream
/// <c>Key</c> is a strongly-typed <c>ApprovalOptionKey</c> whose JSON converter
/// writes a bare string, so a plain <see cref="string"/> here is wire-identical.
/// </summary>
public sealed record ToolInteractionOption(string Key, string Label);

/// <summary>
/// Wire-safe DTO for session output: the daemon's flattened discriminated
/// union, delivered via the <c>ReceiveOutput</c> SignalR callback.
/// </summary>
public sealed record SessionOutputDto
{
    /// <summary>Output type discriminator — one of <see cref="SessionOutputTypes"/>.</summary>
    public required string Type { get; init; }

    public required string SessionId { get; init; }

    public long TimestampMs { get; init; }

    // Text / Thinking
    public string? Text { get; init; }

    // Tool Call / Tool Result
    public string? CallId { get; init; }
    public string? ToolName { get; init; }
    public string? ArgumentsJson { get; init; }
    public string? Result { get; init; }

    // Usage
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long? ReasoningTokens { get; init; }
    public int? ContextWindowTokens { get; init; }
    public double? UsagePercent { get; init; }
    public double? PromptMs { get; init; }
    public double? PredictedPerSecond { get; init; }

    // Turn Completed — upstream is TurnNumber? whose converter writes a bare int.
    public int? TurnNumber { get; init; }
    public string? TurnOutcome { get; init; }
    public string? SourceReminderId { get; init; }

    // Error
    public string? ErrorMessage { get; init; }
    public string? ErrorDetail { get; init; }
    public string? ErrorCorrelationId { get; init; }
    public string? ErrorCategory { get; init; }

    // Compaction
    public int? MessagesBefore { get; init; }
    public int? MessagesAfter { get; init; }
    public long? PreCompactionInputTokens { get; init; }
    public int? KeepCountUsed { get; init; }

    // Session Joined
    public string? Title { get; init; }
    public int? TurnCount { get; init; }
    public List<ChatMessageDto>? RecentMessages { get; init; }

    // Tool Interaction
    public string? InteractionKind { get; init; }
    public string? InteractionDisplayText { get; init; }
    public string? RequesterSenderId { get; init; }
    public List<string>? InteractionPatterns { get; init; }
    public List<string>? InteractionCandidateVerbs { get; init; }
    public string? InteractionCwd { get; init; }
    public bool? InteractionIsMessy { get; init; }
    public List<ToolInteractionOption>? InteractionOptions { get; init; }
    public bool? InteractionHasAdoptedContext { get; init; }
    public bool? InteractionHasThirdPartyAdoptedContext { get; init; }
    public List<string>? InteractionAdoptedSpeakerIds { get; init; }

    // SubAgent
    public string? AgentName { get; init; }
    public string? Phase { get; init; }
    public int? ToolCountSub { get; init; }
    public bool? SubAgentSuccess { get; init; }
    public double? DurationMs { get; init; }
    public string? MemoryDecision { get; init; }
    public string? MemoryDecisionReason { get; init; }
    public int? FindingsCount { get; init; }

    // File Output
    public string? FilePath { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
}

/// <summary>
/// Wire-safe response for ensuring a SignalR session binding. Mirrors
/// <c>Netclaw.Actors.Protocol.SessionEnsureResultDto</c>.
/// </summary>
/// <param name="Created">
/// True when a new session was created; false when an existing session was reattached.
/// </param>
public sealed record SessionEnsureResultDto(string SessionId, bool Created);
