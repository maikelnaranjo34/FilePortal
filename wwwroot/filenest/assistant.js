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
  const mic = panel.querySelector("#assistantMic");
  const status = panel.querySelector("#assistantStatus");
  const history = [];
  let available = false;
  let voiceAvailable = false;
  let listening = false;
  let sttSocket;
  let captureContext;
  let captureNode;
  let microphoneStream;
  let pendingTtsToken;
  let voiceConfiguration;

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
      voiceAvailable = result.voiceAvailable && !!navigator.mediaDevices?.getUserMedia && !!window.AudioWorkletNode;
      availability.textContent = available
        ? `Ready with ${result.openAiModel}. Voice setup is ${result.voiceAvailable ? "available" : "not configured"}.`
        : "AI is not enabled yet. An administrator can configure it in Settings.";
    } catch {
      availability.textContent = "Assistant status could not be loaded.";
    }
    input.disabled = !available;
    send.disabled = !available;
    mic.disabled = !voiceAvailable;
  }

  launcher.addEventListener("click", () => {
    panel.returnValue = "";
    panel.showModal();
    checkAvailability().then(() => available && input.focus());
  });
  panel.addEventListener("close", () => {
    stopListening();
    if (panel.returnValue === "search") document.querySelector("#dashboardSearch").focus();
    else launcher.focus();
  });
  panel.querySelector("[data-assistant-search]").addEventListener("click", () => panel.close("search"));

  async function startListening() {
    if (listening || !voiceAvailable) return;
    status.textContent = "Connecting to AssemblyAI…";
    try {
      const tokenResponse = await fetch("/api/ai/voice/tokens", { credentials: "include" });
      const tokens = await tokenResponse.json().catch(() => null);
      if (!tokenResponse.ok) throw new Error(tokens?.error || "Voice could not start.");
      pendingTtsToken = tokens.ttsToken;
      voiceConfiguration = tokens;
      microphoneStream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }
      });
      captureContext = new AudioContext();
      await captureContext.audioWorklet.addModule("/filenest/pcm-worklet.js");
      const url = new URL("wss://streaming.assemblyai.com/v3/ws");
      url.searchParams.set("token", tokens.sttToken);
      url.searchParams.set("speech_model", tokens.speechModel);
      url.searchParams.set("sample_rate", String(captureContext.sampleRate));
      url.searchParams.set("encoding", "pcm_s16le");
      sttSocket = new WebSocket(url);
      sttSocket.binaryType = "arraybuffer";
      sttSocket.onmessage = event => {
        let message;
        try { message = JSON.parse(event.data); } catch { return; }
        if (message.type === "Turn") {
          input.value = message.transcript || "";
          status.textContent = message.end_of_turn ? "Voice captured." : "Listening…";
          if (message.end_of_turn && input.value.trim()) {
            stopListening();
            form.requestSubmit();
          }
        } else if (message.type === "SpeechStarted") status.textContent = "Listening…";
      };
      sttSocket.onerror = () => { status.textContent = "The AssemblyAI listening connection failed."; stopListening(); };
      sttSocket.onclose = event => {
        if (listening && event.code !== 1000) status.textContent = "Voice input ended unexpectedly.";
        stopCapture();
      };
      await new Promise((resolve, reject) => {
        sttSocket.onopen = resolve;
        const previousError = sttSocket.onerror;
        sttSocket.onerror = event => { previousError(event); reject(new Error("AssemblyAI could not open voice input.")); };
      });
      const source = captureContext.createMediaStreamSource(microphoneStream);
      captureNode = new AudioWorkletNode(captureContext, "filenest-pcm-capture");
      const silent = captureContext.createGain();
      silent.gain.value = 0;
      source.connect(captureNode).connect(silent).connect(captureContext.destination);
      captureNode.port.onmessage = event => {
        if (sttSocket?.readyState === WebSocket.OPEN) sttSocket.send(event.data);
      };
      listening = true;
      mic.setAttribute("aria-pressed", "true");
      mic.setAttribute("aria-label", "Stop voice input");
      mic.textContent = "⏹️";
      status.textContent = "Listening…";
    } catch (error) {
      stopListening();
      status.textContent = error.message || "Voice could not start.";
    }
  }

  function stopCapture() {
    listening = false;
    captureNode?.disconnect();
    captureNode = undefined;
    microphoneStream?.getTracks().forEach(track => track.stop());
    microphoneStream = undefined;
    captureContext?.close().catch(() => {});
    captureContext = undefined;
    mic.setAttribute("aria-pressed", "false");
    mic.setAttribute("aria-label", "Start voice input");
    mic.textContent = "🎙️";
  }

  function stopListening() {
    if (sttSocket?.readyState === WebSocket.OPEN) {
      sttSocket.send(JSON.stringify({ type: "Terminate" }));
      sttSocket.close(1000);
    }
    sttSocket = undefined;
    stopCapture();
  }

  async function speak(text) {
    const token = pendingTtsToken;
    pendingTtsToken = undefined;
    if (!token || !voiceConfiguration || !text) return;
    const audioContext = new AudioContext();
    let nextTime = audioContext.currentTime;
    const url = new URL("wss://tts.us.assemblyai.com/v1/ws");
    url.searchParams.set("token", token);
    url.searchParams.set("voice_id", voiceConfiguration.voiceId);
    url.searchParams.set("model_id", voiceConfiguration.ttsModel);
    url.searchParams.set("language", voiceConfiguration.language);
    url.searchParams.set("sample_rate", "24000");
    url.searchParams.set("encoding", "pcm_s16le");
    const socket = new WebSocket(url);
    socket.onopen = () => {
      socket.send(JSON.stringify({ type: "Generate", text: text.slice(0, 2000) }));
      socket.send(JSON.stringify({ type: "Flush" }));
      status.textContent = "Nesti is speaking…";
    };
    socket.onmessage = event => {
      let message;
      try { message = JSON.parse(event.data); } catch { return; }
      if (message.type === "Audio" && message.audio) {
        const bytes = Uint8Array.from(atob(message.audio), character => character.charCodeAt(0));
        const samples = new Int16Array(bytes.buffer);
        const buffer = audioContext.createBuffer(1, samples.length, 24000);
        const channel = buffer.getChannelData(0);
        for (let index = 0; index < samples.length; index++) channel[index] = samples[index] / 32768;
        const source = audioContext.createBufferSource();
        source.buffer = buffer;
        source.connect(audioContext.destination);
        nextTime = Math.max(nextTime, audioContext.currentTime + 0.03);
        source.start(nextTime);
        nextTime += buffer.duration;
      } else if (message.type === "FlushDone") {
        status.textContent = "";
        socket.send(JSON.stringify({ type: "Terminate" }));
      }
    };
    socket.onerror = () => { status.textContent = "Nesti could not play the spoken response."; };
    socket.onclose = () => setTimeout(() => audioContext.close().catch(() => {}), Math.max(0, (nextTime - audioContext.currentTime) * 1000 + 100));
  }

  mic.addEventListener("click", () => listening ? stopListening() : startListening());

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
      speak(result.reply);
      status.textContent = "";
    } catch (error) {
      status.textContent = error.message;
    } finally {
      input.disabled = send.disabled = !available;
      if (available) input.focus();
    }
  });
})();
