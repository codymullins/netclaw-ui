// -----------------------------------------------------------------------
// <copyright file="StatusToneTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Web.Components;
using Xunit;

namespace Netclaw.Web.Tests;

public sealed class StatusToneTests
{
    [Theory]
    [InlineData("healthy", StatusToneKind.Good)]
    [InlineData("OK", StatusToneKind.Good)]
    [InlineData("running", StatusToneKind.Good)]
    [InlineData("degraded", StatusToneKind.Warn)]
    [InlineData("Restarting", StatusToneKind.Warn)]
    [InlineData("unhealthy", StatusToneKind.Bad)]
    [InlineData("stopped", StatusToneKind.Bad)]
    [InlineData("failed", StatusToneKind.Bad)]
    [InlineData("disabled", StatusToneKind.Neutral)]
    [InlineData("anything-unknown", StatusToneKind.Neutral)]
    [InlineData("", StatusToneKind.Neutral)]
    [InlineData(null, StatusToneKind.Neutral)]
    public void From_maps_status_strings_to_expected_tone(string? input, StatusToneKind expected)
    {
        Assert.Equal(expected, StatusTone.From(input));
    }

    [Fact]
    public void Class_helpers_never_use_accent_token_for_status()
    {
        // Constitution: --nc-accent is interactive-only, never status.
        // The class names returned must not contain "accent".
        foreach (var tone in new[] { StatusToneKind.Good, StatusToneKind.Warn, StatusToneKind.Bad, StatusToneKind.Neutral })
        {
            Assert.DoesNotContain("accent", StatusTone.PillClass(tone));
            Assert.DoesNotContain("accent", StatusTone.CardStatusClass(tone));
            Assert.DoesNotContain("accent", StatusTone.DotClass(tone));
        }
    }
}
