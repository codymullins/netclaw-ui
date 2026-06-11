// Chat page interop: composer key handling, stick-to-bottom scrolling, and
// delegated clipboard copy for code cards / artifact paths.
window.ncChat = {
  // Enter sends (by clicking the Blazor-bound send button); Shift+Enter inserts
  // a newline. Blazor dispatches queued circuit events in order, so the
  // textarea's oninput has already synced the bound value when the click lands.
  wireComposer(textarea, sendButton) {
    if (!textarea || textarea.dataset.ncWired) return;
    textarea.dataset.ncWired = "1";
    textarea.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
        e.preventDefault();
        sendButton.click();
      }
    });
  },

  // One delegated listener on the transcript container handles every copy
  // affordance, including ones inside markdown rendered later: code cards
  // ([data-nc-copy] copies the sibling <pre> text) and literal payloads
  // ([data-nc-copy-text="..."] copies the attribute value).
  wireTranscript(container) {
    if (!container || container.dataset.ncWired) return;
    container.dataset.ncWired = "1";
    container.addEventListener("click", (e) => {
      const btn = e.target.closest("[data-nc-copy], [data-nc-copy-text]");
      if (!btn || !container.contains(btn)) return;

      let text = btn.getAttribute("data-nc-copy-text");
      if (text === null) {
        const code = btn.closest(".nc-code")?.querySelector("pre");
        text = code ? code.textContent : "";
      }
      if (!text) return;

      this._copy(text).then((ok) => {
        const original = btn.textContent;
        btn.textContent = ok ? "copied" : "copy failed";
        btn.classList.add("nc-code__copy--done");
        setTimeout(() => {
          btn.textContent = original;
          btn.classList.remove("nc-code__copy--done");
        }, 1200);
      });
    });
  },

  async _copy(text) {
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return true;
      }
      // Non-secure contexts (e.g. a LAN-addressed daemon UI) fall back to the
      // legacy path via a throwaway textarea.
      const scratch = document.createElement("textarea");
      scratch.value = text;
      scratch.style.position = "fixed";
      scratch.style.opacity = "0";
      document.body.appendChild(scratch);
      scratch.select();
      const ok = document.execCommand("copy");
      scratch.remove();
      return ok;
    } catch {
      return false;
    }
  },

  // Keep the transcript pinned to the newest message, but never fight the
  // user: once they scroll up more than a screen-margin, leave them there.
  scrollToBottom(container, force) {
    if (!container) return;
    const nearBottom =
      container.scrollHeight - container.scrollTop - container.clientHeight < 120;
    if (force || nearBottom) {
      container.scrollTop = container.scrollHeight;
    }
  },
};
