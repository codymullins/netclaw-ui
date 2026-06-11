// -----------------------------------------------------------------------
// <copyright file="ChatTranscriptState.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
namespace Netclaw.Web.Services.Chat;

/// <summary>
/// Folds the daemon's <see cref="SessionOutputDto"/> event stream into a
/// renderable transcript. Pure state, no SignalR or Blazor dependencies, so
/// the streaming logic is unit-testable. Not thread-safe: the chat page
/// marshals every mutation onto the renderer's sync context.
/// </summary>
/// <remarks>
/// Streaming semantics mirror the TUI (<c>ChatPage.HandleOutput</c> in the
/// netclaw repo): <c>text_delta</c> appends to one open assistant message that
/// stays open across tool calls; the final <c>text</c> snapshot (and
/// <c>turn_completed</c>) close it without duplicating the streamed content.
/// Reasoning streams the same way into a <see cref="ThinkingItem"/>, which
/// closes (and collapses) as soon as answer text or a tool call begins.
/// </remarks>
public sealed class ChatTranscriptState
{
    private readonly TimeProvider _time;
    private readonly List<ChatItem> _items = [];
    private readonly Queue<PendingApproval> _approvals = new();
    private AssistantMessageItem? _openAssistant;
    private ThinkingItem? _openThinking;

    public ChatTranscriptState(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<ChatItem> Items => _items;

    public string? Title { get; private set; }

    /// <summary>True between sending a message and the turn completing (or failing).</summary>
    public bool IsGenerating { get; private set; }

    /// <summary>The approval prompt currently awaiting a decision, if any.</summary>
    public PendingApproval? CurrentApproval => _approvals.TryPeek(out var a) ? a : null;

    public int PendingApprovalCount => _approvals.Count;

    /// <summary>Latest usage line, formatted like the TUI: <c>in=N out=N (P% ctx)</c>.</summary>
    public string? UsageSummary { get; private set; }

    /// <summary>Context window utilization in [0,1], when the daemon reports enough to compute it.</summary>
    public double? ContextUsedFraction { get; private set; }

    /// <summary>Records a message the operator just sent and flips to generating.</summary>
    public void AddUserMessage(string text)
    {
        // A new user turn starts a new assistant response; close any leftovers.
        CloseOpenThinking();
        CloseOpenAssistant();
        _items.Add(new UserMessageItem { Text = text });
        IsGenerating = true;
    }

    /// <summary>
    /// Resolves the current approval prompt with the operator's choice and
    /// returns it so the caller can relay the decision to the daemon.
    /// Returns null when no approval is pending.
    /// </summary>
    public PendingApproval? ResolveCurrentApproval(string selectedKey)
    {
        if (!_approvals.TryDequeue(out var approval))
            return null;

        var label = approval.Options.FirstOrDefault(o => o.Key == selectedKey)?.Label ?? selectedKey;
        _items.Add(new NoticeItem
        {
            Kind = NoticeKind.Approval,
            Text = $"{approval.ToolName}: {label}",
            Detail = approval.DisplayText,
        });
        return approval;
    }

    /// <summary>Applies one daemon output event to the transcript.</summary>
    public void Apply(SessionOutputDto dto)
    {
        switch (dto.Type)
        {
            case SessionOutputTypes.SessionJoined:
                ApplySessionJoined(dto);
                break;

            case SessionOutputTypes.SessionTitle:
                Title = dto.Title ?? dto.Text;
                break;

            case SessionOutputTypes.ThinkingDelta:
                if (_openThinking is null)
                {
                    _openThinking = new ThinkingItem
                    {
                        IsStreaming = true,
                        Expanded = true,
                        StartedAt = _time.GetUtcNow(),
                    };
                    _items.Add(_openThinking);
                }

                _openThinking.Text += dto.Text;
                break;

            case SessionOutputTypes.Thinking:
                // After deltas this is the final full snapshot — close without
                // duplicating. Without deltas it is the whole reasoning block.
                if (_openThinking is not null)
                    CloseOpenThinking();
                else if (!string.IsNullOrEmpty(dto.Text))
                    _items.Add(new ThinkingItem { Text = dto.Text, StartedAt = _time.GetUtcNow() });
                break;

            case SessionOutputTypes.TextDelta:
                // Answer text starting means reasoning is done.
                CloseOpenThinking();
                if (_openAssistant is null)
                {
                    _openAssistant = new AssistantMessageItem { IsStreaming = true };
                    _items.Add(_openAssistant);
                }

                _openAssistant.Text += dto.Text;
                break;

            case SessionOutputTypes.Text:
                // When deltas already streamed, this is the final full snapshot
                // for compatibility — close the bubble without duplicating.
                CloseOpenThinking();
                if (_openAssistant is not null)
                    CloseOpenAssistant();
                else if (!string.IsNullOrEmpty(dto.Text))
                    _items.Add(new AssistantMessageItem { Text = dto.Text });
                break;

            case SessionOutputTypes.ToolCall:
                CloseOpenThinking();
                _items.Add(new ToolCallItem
                {
                    CallId = dto.CallId ?? string.Empty,
                    ToolName = dto.ToolName ?? "tool",
                    ArgumentsJson = dto.ArgumentsJson,
                    StartedAt = _time.GetUtcNow(),
                });
                break;

            case SessionOutputTypes.ToolResult:
                if (FindRunningToolCall(dto.CallId) is { } call)
                {
                    call.Result = dto.Result;
                    call.IsComplete = true;
                    call.Duration = _time.GetUtcNow() - call.StartedAt;
                }

                break;

            case SessionOutputTypes.ToolInteraction:
                _approvals.Enqueue(new PendingApproval(
                    dto.CallId ?? string.Empty,
                    dto.ToolName ?? "tool",
                    dto.InteractionDisplayText ?? string.Empty,
                    dto.InteractionPatterns ?? [],
                    dto.InteractionCwd,
                    dto.InteractionOptions ?? [],
                    dto.InteractionIsMessy ?? false,
                    dto.InteractionHasThirdPartyAdoptedContext ?? false));
                break;

            case SessionOutputTypes.SubAgent:
                ApplySubAgent(dto);
                break;

            case SessionOutputTypes.Usage:
                ApplyUsage(dto);
                break;

            case SessionOutputTypes.TurnCompleted:
                CloseOpenThinking();
                CloseOpenAssistant();
                IsGenerating = false;
                break;

            case SessionOutputTypes.Error:
                CloseOpenThinking();
                CloseOpenAssistant();
                _items.Add(new ErrorItem
                {
                    Message = dto.ErrorMessage ?? "Unknown error",
                    Detail = dto.ErrorDetail,
                    Category = dto.ErrorCategory,
                    CorrelationId = dto.ErrorCorrelationId,
                });
                IsGenerating = false;
                break;

            case SessionOutputTypes.File:
                _items.Add(new FileArtifactItem
                {
                    FileName = dto.FileName ?? "file",
                    FilePath = dto.FilePath,
                    MimeType = dto.MimeType,
                });
                break;

            case SessionOutputTypes.Compaction:
                _items.Add(new NoticeItem
                {
                    Kind = NoticeKind.Compaction,
                    Text = $"Context compacted: {dto.MessagesBefore} → {dto.MessagesAfter} messages " +
                           $"(keep={dto.KeepCountUsed}, {dto.PreCompactionInputTokens}/{dto.ContextWindowTokens} tokens)",
                });
                break;

            // Buffer events carry nothing the transcript shows.
            case SessionOutputTypes.BufferFlush:
            default:
                break;
        }
    }

    private void ApplySessionJoined(SessionOutputDto dto)
    {
        Title = dto.Title;
        _items.Add(new NoticeItem
        {
            Kind = NoticeKind.Session,
            Text = dto.TurnCount is > 0 ? "Resumed session" : "New session",
        });

        if (dto.RecentMessages is not { Count: > 0 })
            return;

        foreach (var message in dto.RecentMessages)
        {
            _items.Add(message.Role == "user"
                ? new UserMessageItem { Text = message.Content, IsHistoric = true }
                : new AssistantMessageItem { Text = message.Content, IsHistoric = true });
        }
    }

    private void ApplySubAgent(SessionOutputDto dto)
    {
        var name = dto.AgentName ?? "subagent";
        if (!string.Equals(dto.Phase, "completed", StringComparison.OrdinalIgnoreCase))
        {
            _items.Add(new SubAgentItem { AgentName = name });
            return;
        }

        var item = FindRunningSubAgent(name);
        if (item is null)
        {
            item = new SubAgentItem { AgentName = name };
            _items.Add(item);
        }

        item.IsComplete = true;
        item.Success = dto.SubAgentSuccess;
        item.ToolCount = dto.ToolCountSub;
        item.Duration = dto.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
        item.MemoryDecision = dto.MemoryDecision;
        item.FindingsCount = dto.FindingsCount;
    }

    private void ApplyUsage(SessionOutputDto dto)
    {
        // Like the TUI, derive utilization from the daemon-reported context
        // window; without one there is no denominator worth showing.
        double? fraction = dto is { InputTokens: { } input, ContextWindowTokens: > 0 and var window }
            ? (double)input / window
            : null;
        ContextUsedFraction = fraction;
        var ctxPart = fraction is { } f ? $" ({f:P0} ctx)" : string.Empty;
        UsageSummary = $"in={dto.InputTokens ?? 0} out={dto.OutputTokens ?? 0}{ctxPart}";
    }

    private void CloseOpenAssistant()
    {
        if (_openAssistant is null)
            return;

        _openAssistant.IsStreaming = false;
        if (_openAssistant.Text.Length == 0)
            _items.Remove(_openAssistant);

        _openAssistant = null;
    }

    private void CloseOpenThinking()
    {
        if (_openThinking is null)
            return;

        _openThinking.IsStreaming = false;
        _openThinking.Expanded = false;
        _openThinking.Duration = _time.GetUtcNow() - _openThinking.StartedAt;
        if (_openThinking.Text.Length == 0)
            _items.Remove(_openThinking);

        _openThinking = null;
    }

    private ToolCallItem? FindRunningToolCall(string? callId)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is ToolCallItem { IsComplete: false } call
                && (callId is null || call.CallId == callId))
            {
                return call;
            }
        }

        return null;
    }

    private SubAgentItem? FindRunningSubAgent(string agentName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is SubAgentItem { IsComplete: false } agent
                && agent.AgentName == agentName)
            {
                return agent;
            }
        }

        return null;
    }
}
