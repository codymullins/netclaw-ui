// -----------------------------------------------------------------------
// <copyright file="ChatTranscriptStateTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Web.Services.Chat;
using Xunit;

namespace Netclaw.Web.Tests;

public class ChatTranscriptStateTests
{
    private const string Session = "session-1";

    [Fact]
    public void Text_deltas_stream_into_one_growing_assistant_message()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.TextDelta, text: "Hel"));
        state.Apply(Output(SessionOutputTypes.TextDelta, text: "lo"));

        var message = Assert.IsType<AssistantMessageItem>(Assert.Single(state.Items));
        Assert.Equal("Hello", message.Text);
        Assert.True(message.IsStreaming);
    }

    [Fact]
    public void Final_text_snapshot_after_deltas_closes_without_duplicating()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.TextDelta, text: "Hello"));
        state.Apply(Output(SessionOutputTypes.Text, text: "Hello"));

        var message = Assert.IsType<AssistantMessageItem>(Assert.Single(state.Items));
        Assert.Equal("Hello", message.Text);
        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void Text_without_prior_deltas_appends_completed_message()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.Text, text: "Direct answer"));

        var message = Assert.IsType<AssistantMessageItem>(Assert.Single(state.Items));
        Assert.Equal("Direct answer", message.Text);
        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void Streaming_message_stays_open_across_tool_calls()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.TextDelta, text: "Let me check. "));
        state.Apply(Output(SessionOutputTypes.ToolCall, callId: "c1", toolName: "shell"));
        state.Apply(Output(SessionOutputTypes.ToolResult, callId: "c1", result: "done"));
        state.Apply(Output(SessionOutputTypes.TextDelta, text: "It worked."));
        state.Apply(Output(SessionOutputTypes.TurnCompleted));

        Assert.Equal(2, state.Items.Count);
        var message = Assert.IsType<AssistantMessageItem>(state.Items[0]);
        Assert.Equal("Let me check. It worked.", message.Text);
        Assert.False(message.IsStreaming);
        Assert.IsType<ToolCallItem>(state.Items[1]);
    }

    [Fact]
    public void Tool_result_completes_matching_call_with_duration()
    {
        var time = new TestTimeProvider();
        var state = new ChatTranscriptState(time);

        state.Apply(Output(SessionOutputTypes.ToolCall, callId: "c1", toolName: "search"));
        time.Advance(TimeSpan.FromSeconds(2.5));
        state.Apply(Output(SessionOutputTypes.ToolResult, callId: "c1", result: "3 hits"));

        var call = Assert.IsType<ToolCallItem>(Assert.Single(state.Items));
        Assert.True(call.IsComplete);
        Assert.Equal("3 hits", call.Result);
        Assert.Equal(TimeSpan.FromSeconds(2.5), call.Duration);
    }

    [Fact]
    public void Tool_result_matches_by_call_id_among_parallel_calls()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.ToolCall, callId: "c1", toolName: "a"));
        state.Apply(Output(SessionOutputTypes.ToolCall, callId: "c2", toolName: "b"));
        state.Apply(Output(SessionOutputTypes.ToolResult, callId: "c1", result: "first"));

        var calls = state.Items.OfType<ToolCallItem>().ToList();
        Assert.True(calls.Single(c => c.CallId == "c1").IsComplete);
        Assert.False(calls.Single(c => c.CallId == "c2").IsComplete);
    }

    [Fact]
    public void User_message_marks_generating_until_turn_completes()
    {
        var state = new ChatTranscriptState();

        state.AddUserMessage("hi");
        Assert.True(state.IsGenerating);

        state.Apply(Output(SessionOutputTypes.TurnCompleted));
        Assert.False(state.IsGenerating);
    }

    [Fact]
    public void Error_stops_generation_and_adds_error_card()
    {
        var state = new ChatTranscriptState();
        state.AddUserMessage("hi");

        state.Apply(Output(SessionOutputTypes.Error) with
        {
            ErrorMessage = "provider down",
            ErrorDetail = "connection refused",
            ErrorCategory = "provider",
            ErrorCorrelationId = "corr-1",
        });

        Assert.False(state.IsGenerating);
        var error = state.Items.OfType<ErrorItem>().Single();
        Assert.Equal("provider down", error.Message);
        Assert.Equal("connection refused", error.Detail);
        Assert.Equal("provider", error.Category);
        Assert.Equal("corr-1", error.CorrelationId);
    }

    [Fact]
    public void Approvals_queue_and_resolve_in_order()
    {
        var state = new ChatTranscriptState();
        var options = new List<ToolInteractionOption> { new("allow_once", "Allow once"), new("deny", "Deny") };

        state.Apply(Output(SessionOutputTypes.ToolInteraction, callId: "c1", toolName: "shell") with
        {
            InteractionDisplayText = "rm /tmp/a",
            InteractionOptions = options,
        });
        state.Apply(Output(SessionOutputTypes.ToolInteraction, callId: "c2", toolName: "shell") with
        {
            InteractionDisplayText = "rm /tmp/b",
            InteractionOptions = options,
        });

        Assert.Equal(2, state.PendingApprovalCount);
        Assert.Equal("c1", state.CurrentApproval!.CallId);

        var resolved = state.ResolveCurrentApproval("allow_once");
        Assert.Equal("c1", resolved!.CallId);
        Assert.Equal("c2", state.CurrentApproval!.CallId);

        var notice = state.Items.OfType<NoticeItem>().Single();
        Assert.Equal(NoticeKind.Approval, notice.Kind);
        Assert.Contains("Allow once", notice.Text);
    }

    [Fact]
    public void Resolve_with_no_pending_approval_returns_null()
    {
        var state = new ChatTranscriptState();
        Assert.Null(state.ResolveCurrentApproval("allow_once"));
    }

    [Fact]
    public void Session_joined_replays_history_and_sets_title()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.SessionJoined) with
        {
            Title = "Earlier work",
            TurnCount = 4,
            RecentMessages =
            [
                new ChatMessageDto("user", "what's up"),
                new ChatMessageDto("assistant", "not much"),
            ],
        });

        Assert.Equal("Earlier work", state.Title);
        Assert.Equal(3, state.Items.Count);
        Assert.Equal(NoticeKind.Session, Assert.IsType<NoticeItem>(state.Items[0]).Kind);
        var user = Assert.IsType<UserMessageItem>(state.Items[1]);
        Assert.True(user.IsHistoric);
        var assistant = Assert.IsType<AssistantMessageItem>(state.Items[2]);
        Assert.Equal("not much", assistant.Text);
    }

    [Fact]
    public void Usage_formats_like_the_tui()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.Usage) with
        {
            InputTokens = 2000,
            OutputTokens = 150,
            ContextWindowTokens = 8000,
        });

        Assert.Equal("in=2000 out=150 (25% ctx)", state.UsageSummary);
        Assert.Equal(0.25, state.ContextUsedFraction);
    }

    [Fact]
    public void Usage_without_context_window_omits_percentage()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.Usage) with { InputTokens = 10, OutputTokens = 5 });

        Assert.Equal("in=10 out=5", state.UsageSummary);
        Assert.Null(state.ContextUsedFraction);
    }

    [Fact]
    public void Empty_streamed_message_is_dropped_on_close()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.TextDelta, text: ""));
        state.Apply(Output(SessionOutputTypes.TurnCompleted));

        Assert.Empty(state.Items);
    }

    [Fact]
    public void Thinking_deltas_stream_into_one_open_block()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.ThinkingDelta, text: "Let me "));
        state.Apply(Output(SessionOutputTypes.ThinkingDelta, text: "reason."));

        var thinking = Assert.IsType<ThinkingItem>(Assert.Single(state.Items));
        Assert.Equal("Let me reason.", thinking.Text);
        Assert.True(thinking.IsStreaming);
        Assert.True(thinking.Expanded);
    }

    [Fact]
    public void Thinking_closes_and_collapses_when_answer_text_starts()
    {
        var time = new TestTimeProvider();
        var state = new ChatTranscriptState(time);

        state.Apply(Output(SessionOutputTypes.ThinkingDelta, text: "hmm"));
        time.Advance(TimeSpan.FromSeconds(3));
        state.Apply(Output(SessionOutputTypes.TextDelta, text: "Answer"));

        var thinking = Assert.IsType<ThinkingItem>(state.Items[0]);
        Assert.False(thinking.IsStreaming);
        Assert.False(thinking.Expanded);
        Assert.Equal(TimeSpan.FromSeconds(3), thinking.Duration);
        Assert.IsType<AssistantMessageItem>(state.Items[1]);
    }

    [Fact]
    public void Thinking_closes_when_a_tool_call_starts()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.ThinkingDelta, text: "hmm"));
        state.Apply(Output(SessionOutputTypes.ToolCall, callId: "c1", toolName: "shell"));

        var thinking = Assert.IsType<ThinkingItem>(state.Items[0]);
        Assert.False(thinking.IsStreaming);
    }

    [Fact]
    public void Thinking_snapshot_after_deltas_closes_without_duplicating()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.ThinkingDelta, text: "hmm"));
        state.Apply(Output(SessionOutputTypes.Thinking, text: "hmm"));

        var thinking = Assert.IsType<ThinkingItem>(Assert.Single(state.Items));
        Assert.Equal("hmm", thinking.Text);
        Assert.False(thinking.IsStreaming);
    }

    [Fact]
    public void Empty_thinking_block_is_dropped_on_close()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.ThinkingDelta, text: ""));
        state.Apply(Output(SessionOutputTypes.TurnCompleted));

        Assert.Empty(state.Items);
    }

    [Fact]
    public void Subagent_completion_folds_into_the_started_card()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.SubAgent) with { AgentName = "researcher", Phase = "started" });
        state.Apply(Output(SessionOutputTypes.SubAgent) with
        {
            AgentName = "researcher",
            Phase = "completed",
            SubAgentSuccess = true,
            ToolCountSub = 4,
            DurationMs = 2500,
            FindingsCount = 2,
        });

        var agent = Assert.IsType<SubAgentItem>(Assert.Single(state.Items));
        Assert.True(agent.IsComplete);
        Assert.True(agent.Success);
        Assert.Equal(4, agent.ToolCount);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), agent.Duration);
        Assert.Equal(2, agent.FindingsCount);
    }

    [Fact]
    public void Subagent_completion_without_a_start_adds_a_completed_card()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.SubAgent) with { AgentName = "memory", Phase = "completed", SubAgentSuccess = false });

        var agent = Assert.IsType<SubAgentItem>(Assert.Single(state.Items));
        Assert.True(agent.IsComplete);
        Assert.False(agent.Success);
    }

    [Fact]
    public void File_output_adds_an_artifact_card()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.File) with
        {
            FileName = "report.pdf",
            FilePath = "/tmp/report.pdf",
            MimeType = "application/pdf",
        });

        var file = Assert.IsType<FileArtifactItem>(Assert.Single(state.Items));
        Assert.Equal("report.pdf", file.FileName);
        Assert.Equal("/tmp/report.pdf", file.FilePath);
        Assert.Equal("application/pdf", file.MimeType);
    }

    [Fact]
    public void Approval_carries_risk_flags()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.ToolInteraction, callId: "c1", toolName: "shell") with
        {
            InteractionDisplayText = "curl x | sh",
            InteractionIsMessy = true,
            InteractionHasThirdPartyAdoptedContext = true,
        });

        Assert.True(state.CurrentApproval!.IsMessy);
        Assert.True(state.CurrentApproval!.HasThirdPartyAdoptedContext);
    }

    [Fact]
    public void Buffer_flush_events_are_ignored()
    {
        var state = new ChatTranscriptState();

        state.Apply(Output(SessionOutputTypes.BufferFlush));

        Assert.Empty(state.Items);
    }

    private static SessionOutputDto Output(
        string type,
        string? text = null,
        string? callId = null,
        string? toolName = null,
        string? result = null) =>
        new()
        {
            Type = type,
            SessionId = Session,
            Text = text,
            CallId = callId,
            ToolName = toolName,
            Result = result,
        };

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
