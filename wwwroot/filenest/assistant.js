(() => {
  "use strict";
  const launcher = document.querySelector("#assistantLauncher");
  const panel = document.querySelector("#assistantPanel");
  if (!launcher || !panel) return;
  const availability = panel.querySelector("#assistantAvailability");
  const conversation = panel.querySelector("#assistantConversation");
  const form = panel.querySelector("#assistantForm");
  const input = panel.querySelector("#assistantMessage");
  const send = panel.querySelector("#assistantSend");
  const status = panel.querySelector("#assistantStatus");
  const history = [];
  let available = false;

  function addMessage(role, content) {
    const message = document.createElement("div");
    message.className = "fn-assistant-message " + role;
    const label = document.createElement("strong");
    label.textContent = role === "user" ? "You" : "Nesti";
    const text = document.createElement("p");
    text.textContent = content;
    message.append(label, text);
    conversation.append(message);
    conversation.scrollTop = conversation.scrollHeight;
  }

  async function checkAvailability() {
    try {
      const response = await fetch("/api/ai/status", { credentials: "include" });
      if (!response.ok) throw new Error();
      const result = await response.json();
      available = result.available;
      availability.textContent = available
        ? `Ready with ${result.openAiModel}. Voice setup is ${result.voiceAvailable ? "available" : "not configured"}.`
        : "AI is not enabled yet. An administrator can configure it in Settings.";
    } catch {
      availability.textContent = "Assistant status could not be loaded.";
    }
    input.disabled = !available;
    send.disabled = !available;
  }

  launcher.addEventListener("click", () => {
    panel.returnValue = "";
    panel.showModal();
    checkAvailability().then(() => available && input.focus());
  });
  panel.addEventListener("close", () => {
    if (panel.returnValue === "search") document.querySelector("#dashboardSearch").focus();
    else launcher.focus();
  });
  panel.querySelector("[data-assistant-search]").addEventListener("click", () => panel.close("search"));

  form.addEventListener("submit", async event => {
    event.preventDefault();
    const message = input.value.trim();
    if (!message || !available || send.disabled) return;
    addMessage("user", message);
    input.value = "";
    input.disabled = send.disabled = true;
    status.textContent = "Nesti is thinking…";
    try {
      const response = await fetch("/api/ai/chat", {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message, history: history.slice(-12) })
      });
      const result = await response.json().catch(() => null);
      if (!response.ok) throw new Error(result?.error || "Nesti could not answer.");
      history.push({ role: "user", content: message }, { role: "assistant", content: result.reply });
      if (history.length > 12) history.splice(0, history.length - 12);
      addMessage("assistant", result.reply);
      status.textContent = "";
    } catch (error) {
      status.textContent = error.message;
    } finally {
      input.disabled = send.disabled = !available;
      if (available) input.focus();
    }
  });
})();
