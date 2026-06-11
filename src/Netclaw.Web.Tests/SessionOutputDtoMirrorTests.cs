// -----------------------------------------------------------------------
// <copyright file="SessionOutputDtoMirrorTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Reflection;
using System.Text.Json;
using Xunit;
using Upstream = Netclaw.Actors.Protocol;
using Wire = Netclaw.Web.Services.Chat;

namespace Netclaw.Web.Tests;

/// <summary>
/// Guards the mirrored chat wire DTOs in Netclaw.Web against drift from the
/// daemon's originals in Netclaw.Actors. JSON deserialization is tolerant —
/// an added or renamed upstream field would not throw, it would silently
/// vanish from the chat UI. These tests turn a contract change in the
/// submodule into a test failure instead.
/// </summary>
public class SessionOutputDtoMirrorTests
{
    /// <summary>
    /// Upstream wrapped types whose JSON converters write bare primitives;
    /// the mirror declares the primitive directly.
    /// </summary>
    private static readonly Dictionary<Type, Type> WireEquivalents = new()
    {
        [typeof(Upstream.TurnNumber?)] = typeof(int?),
        [typeof(Upstream.ApprovalOptionKey)] = typeof(string),
        [typeof(Upstream.ChatMessageDto)] = typeof(Wire.ChatMessageDto),
        [typeof(Upstream.ToolInteractionOption)] = typeof(Wire.ToolInteractionOption),
        [typeof(List<Upstream.ChatMessageDto>)] = typeof(List<Wire.ChatMessageDto>),
        [typeof(List<Upstream.ToolInteractionOption>)] = typeof(List<Wire.ToolInteractionOption>),
    };

    [Theory]
    [InlineData(typeof(Upstream.SessionOutputDto), typeof(Wire.SessionOutputDto))]
    [InlineData(typeof(Upstream.ChatMessageDto), typeof(Wire.ChatMessageDto))]
    [InlineData(typeof(Upstream.ToolInteractionOption), typeof(Wire.ToolInteractionOption))]
    [InlineData(typeof(Upstream.SessionEnsureResultDto), typeof(Wire.SessionEnsureResultDto))]
    public void Mirror_matches_upstream_property_for_property(Type upstream, Type mirror)
    {
        var upstreamProps = PublicProperties(upstream);
        var mirrorProps = PublicProperties(mirror);

        var missing = upstreamProps.Keys.Except(mirrorProps.Keys).ToList();
        var extra = mirrorProps.Keys.Except(upstreamProps.Keys).ToList();

        Assert.True(missing.Count == 0,
            $"{mirror.Name} is missing properties present upstream: {string.Join(", ", missing)}. " +
            "The submodule's wire contract changed — update the mirror in Netclaw.Web/Services/Chat.");
        Assert.True(extra.Count == 0,
            $"{mirror.Name} has properties absent upstream: {string.Join(", ", extra)}.");

        foreach (var (name, upstreamProp) in upstreamProps)
        {
            var expected = WireEquivalents.GetValueOrDefault(
                upstreamProp.PropertyType, upstreamProp.PropertyType);
            Assert.True(expected == mirrorProps[name].PropertyType,
                $"{mirror.Name}.{name}: expected wire-equivalent type {expected}, " +
                $"found {mirrorProps[name].PropertyType} (upstream is {upstreamProp.PropertyType}).");
        }
    }

    [Fact]
    public void Fully_populated_upstream_dto_round_trips_into_mirror()
    {
        var upstream = new Upstream.SessionOutputDto
        {
            Type = "tool_interaction",
            SessionId = "session-1",
            TimestampMs = 1718000000000,
            Text = "hello",
            CallId = "call-7",
            ToolName = "shell",
            ArgumentsJson = """{"cmd":"ls"}""",
            Result = "ok",
            InputTokens = 100,
            OutputTokens = 20,
            TotalTokens = 120,
            CachedInputTokens = 50,
            ReasoningTokens = 5,
            ContextWindowTokens = 8000,
            UsagePercent = 0.0125,
            PromptMs = 312.5,
            PredictedPerSecond = 42.0,
            TurnNumber = new Upstream.TurnNumber(3),
            TurnOutcome = "completed",
            SourceReminderId = "rem-1",
            ErrorMessage = "boom",
            ErrorDetail = "stack",
            ErrorCorrelationId = "corr-1",
            ErrorCategory = "transient",
            MessagesBefore = 40,
            MessagesAfter = 12,
            PreCompactionInputTokens = 7600,
            KeepCountUsed = 20,
            Title = "My session",
            TurnCount = 9,
            RecentMessages = [new Upstream.ChatMessageDto("user", "hi")],
            InteractionKind = "approval",
            InteractionDisplayText = "rm -rf /tmp/x",
            RequesterSenderId = "sender-1",
            InteractionPatterns = ["rm -rf"],
            InteractionCandidateVerbs = ["rm"],
            InteractionCwd = "/tmp",
            InteractionIsMessy = true,
            InteractionOptions =
            [
                new Upstream.ToolInteractionOption(new Upstream.ApprovalOptionKey("allow_once"), "Allow once")
            ],
            InteractionHasAdoptedContext = true,
            InteractionHasThirdPartyAdoptedContext = false,
            InteractionAdoptedSpeakerIds = ["spk-1"],
            AgentName = "researcher",
            Phase = "completed",
            ToolCountSub = 4,
            SubAgentSuccess = true,
            DurationMs = 1234.5,
            MemoryDecision = "kept",
            MemoryDecisionReason = "useful",
            FindingsCount = 2,
            FilePath = "/tmp/out.png",
            FileName = "out.png",
            MimeType = "image/png",
        };

        // SignalR's JSON hub protocol serializes camelCase and deserializes
        // case-insensitively on both ends; reproduce that here.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        var json = JsonSerializer.Serialize(upstream, options);
        var mirror = JsonSerializer.Deserialize<Wire.SessionOutputDto>(json, options);

        Assert.NotNull(mirror);
        Assert.Equal("tool_interaction", mirror.Type);
        Assert.Equal("session-1", mirror.SessionId);
        Assert.Equal(1718000000000, mirror.TimestampMs);
        Assert.Equal("hello", mirror.Text);
        Assert.Equal("call-7", mirror.CallId);
        Assert.Equal("shell", mirror.ToolName);
        Assert.Equal(100, mirror.InputTokens);
        Assert.Equal(8000, mirror.ContextWindowTokens);
        Assert.Equal(3, mirror.TurnNumber);
        Assert.Equal("boom", mirror.ErrorMessage);
        Assert.Equal(40, mirror.MessagesBefore);
        Assert.Equal("My session", mirror.Title);
        Assert.Equal([new Wire.ChatMessageDto("user", "hi")], mirror.RecentMessages);
        Assert.Equal("approval", mirror.InteractionKind);
        Assert.Equal(["rm -rf"], mirror.InteractionPatterns);
        Assert.Equal(
            [new Wire.ToolInteractionOption("allow_once", "Allow once")],
            mirror.InteractionOptions);
        Assert.Equal("researcher", mirror.AgentName);
        Assert.Equal("out.png", mirror.FileName);
    }

    [Fact]
    public void Session_output_type_constants_match_upstream()
    {
        var upstream = Constants(typeof(Upstream.SessionOutputTypes));
        var mirror = Constants(typeof(Wire.SessionOutputTypes));
        Assert.Equal(upstream, mirror);
    }

    private static Dictionary<string, PropertyInfo> PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name);

    private static Dictionary<string, string?> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string?)f.GetRawConstantValue());
}
