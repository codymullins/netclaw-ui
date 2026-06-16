// -----------------------------------------------------------------------
// <copyright file="MarkdownRenderer.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.AspNetCore.Components;

namespace Netclaw.Web.Services.Chat;

/// <summary>
/// Renders assistant markdown to HTML for the chat transcript. Fenced code
/// blocks become "code cards": a header bar with the language tag and a copy
/// button (handled by a delegated listener in <c>chat.js</c>) above the
/// usual escaped <c>pre/code</c> body.
/// </summary>
public static class MarkdownRenderer
{
    // DisableHtml is the safety boundary: model output is untrusted, so any
    // raw HTML in it is escaped rather than parsed. Everything rendered comes
    // from markdown constructs only; the code-card chrome is emitted by our
    // own renderer with the language tag escaped.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    public static MarkupString Render(string markdown)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);

        renderer.ObjectRenderers.RemoveAll(r => r is CodeBlockRenderer);
        renderer.ObjectRenderers.Insert(0, new CodeCardRenderer());

        renderer.Render(Markdown.Parse(markdown, Pipeline));
        writer.Flush();
        return new(writer.ToString());
    }

    private sealed class CodeCardRenderer : HtmlObjectRenderer<CodeBlock>
    {
        private readonly CodeBlockRenderer _body = new();

        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            var language = (block as FencedCodeBlock)?.Info;
            renderer.Write("<div class=\"nc-code\"><div class=\"nc-code__bar\"><span class=\"nc-code__lang\">");
            renderer.WriteEscape(string.IsNullOrWhiteSpace(language) ? "text" : language);
            renderer.Write("</span><button type=\"button\" class=\"nc-code__copy\" data-nc-copy>copy</button></div>");
            _body.Write(renderer, block);
            renderer.Write("</div>");
        }
    }
}
