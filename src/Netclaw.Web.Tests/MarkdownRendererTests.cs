// -----------------------------------------------------------------------
// <copyright file="MarkdownRendererTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Netclaw.Web.Services.Chat;
using Xunit;

namespace Netclaw.Web.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void Fenced_code_renders_as_code_card_with_language_and_copy_button()
    {
        var html = MarkdownRenderer.Render("```bash\necho hi\n```").Value;

        Assert.Contains("class=\"nc-code\"", html);
        Assert.Contains("class=\"nc-code__lang\">bash<", html);
        Assert.Contains("data-nc-copy", html);
        Assert.Contains("echo hi", html);
    }

    [Fact]
    public void Fenced_code_without_language_is_tagged_text()
    {
        var html = MarkdownRenderer.Render("```\nplain\n```").Value;

        Assert.Contains("class=\"nc-code__lang\">text<", html);
    }

    [Fact]
    public void Code_card_language_tag_is_escaped()
    {
        var html = MarkdownRenderer.Render("``` <script>\nx\n```").Value;

        Assert.DoesNotContain("nc-code__lang\"><script>", html);
    }

    [Fact]
    public void Raw_html_in_markdown_stays_escaped()
    {
        var html = MarkdownRenderer.Render("<img src=x onerror=alert(1)>").Value;

        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Inline_code_does_not_get_card_chrome()
    {
        var html = MarkdownRenderer.Render("use `ls -la` here").Value;

        Assert.DoesNotContain("nc-code__bar", html);
        Assert.Contains("<code>ls -la</code>", html);
    }
}
