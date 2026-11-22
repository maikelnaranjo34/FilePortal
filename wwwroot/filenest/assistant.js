(() => {
  "use strict";
  if (window.__fileNestAssistantLoaded) return;
  window.__fileNestAssistantLoaded = true;
  const launcher = document.querySelector("#assistantLauncher");
  const panel = document.querySelector("#assistantPanel");
  if (!panel) return;
  const availability = panel.querySelector("#assistantAvailability");
  const conversation = panel.querySelector("#assistantConversation");
  const form = panel.querySelector("#assistantForm");
  const input = panel.querySelector("#assistantMessage");
  const send = panel.querySelector("#assistantSend");
  const mic = panel.querySelector("#assistantMic");
  const status = panel.querySelector("#assistantStatus");
  const companion = document.querySelector("#nestiCompanion");
  const voiceToggle = document.querySelector("#nestiVoiceToggle");
  const floatOpen = document.querySelector("#nestiFloatOpen");
  const navigationStateKey = "filenest:nesti-navigation";
  let restoredState = null;
  try {
    const savedState = sessionStorage.getItem(navigationStateKey);
    if (savedState) restoredState = JSON.parse(savedState);
    sessionStorage.removeItem(navigationStateKey);
  } catch (error) {
    console.error("Could not restore Nesti navigation state.", error);
  }
  const history = [];
  const restoredHistory = Array.isArray(restoredState?.history) ? restoredState.history : [];
  restoredHistory
    .filter(item => item && (item.role === "user" || item.role === "assistant") &&
      typeof item.content === "string" && item.content.length <= 4_000)
    .slice(-12)
    .forEach(item => history.push({ role: item.role, content: item.content }));
  let available = false;
  let voiceAvailable = false;
  let listening = false;
  let sttSocket;
  let captureContext;
  let captureNode;
  let microphoneStream;
  let pendingTtsToken;
  let voiceConfiguration;
  let continuousVoice = typeof restoredState?.continuousVoice === "boolean"
    ? restoredState.continuousVoice
    : localStorage.getItem("filenest:nesti-voice") !== "off";
  let voiceResumeRequired = false;
  let requestingMicrophone = false;
  let nestiNavigation = false;
  let processingTurn = false;
  let activeTtsSocket;
  let playbackContext;
  let playbackTimer;
  let speechGeneration = 0;
  const playbackSources = new Set();
  const setNesti = state => window.Nesti?.setState(state);

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

  function addActionButton(label, className, action) {
    const row = document.createElement("div");
    row.className = "fn-assistant-action";
    const button = document.createElement("button");
    button.type = "button";
    button.className = "fn-button " + (className || "");
    button.textContent = label;
    button.addEventListener("click", action, { once: true });
    row.append(button);
    conversation.append(row);
    conversation.scrollTop = conversation.scrollHeight;
  }

  function restoreNavigationState() {
    for (const item of history) addMessage(item.role, item.content);
    if (restoredState?.panelOpen) {
      panel.returnValue = "";
      panel.showModal();
    }
  }

  function saveNavigationState() {
    try {
      sessionStorage.setItem(navigationStateKey, JSON.stringify({
        history: history.slice(-12),
        continuousVoice,
        panelOpen: panel.open
      }));
    } catch (error) {
      console.error("Could not preserve Nesti state for navigation.", error);
    }
    nestiNavigation = true;
  }

  function handleActions(actions) {
    for (const action of actions || []) {
      if (action.type === "navigate" && typeof action.url === "string") {
        setNesti("moving");
        status.textContent = "Opening…";
        saveNavigationState();
        setTimeout(() => location.assign(action.url), 650);
      } else if (action.type === "open_file" && typeof action.path === "string") {
        setNesti("moving");
        setTimeout(() => location.assign("/api/files/file?" + new URLSearchParams({ path: action.path })), 650);
      } else if (action.type === "confirm_download" && Array.isArray(action.paths)) {
        addActionButton(`Download ${action.paths.length} item${action.paths.length === 1 ? "" : "s"}`, "fn-button-primary", () => {
          const form = document.createElement("form");
          form.method = "post";
          form.action = "/api/files/download-zip-form";
          for (const path of action.paths) {
            const field = document.createElement("input");
            field.type = "hidden"; field.name = "paths"; field.value = path; form.append(field);
          }
          document.body.append(form); form.submit(); form.remove();
        });
      } else if (action.type === "confirm_delete" && Array.isArray(action.paths)) {
        addActionButton(`Review deletion of ${action.paths.length} item${action.paths.length === 1 ? "" : "s"}`, "fn-danger", async event => {
          const confirmed = confirm(`Permanently delete ${action.paths.length} selected item${action.paths.length === 1 ? "" : "s"}? This cannot be undone.`);
          if (!confirmed) { addMessage("assistant", "Deletion cancelled. No files were changed."); return; }
          event.currentTarget.disabled = true;
          const response = await fetch("/api/files/batch/delete", {
            method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ paths: action.paths })
          });
          const result = await response.json().catch(() => null);
          if (!response.ok) addMessage("assistant", result?.error || "The selected files could not be deleted.");
          else addMessage("assistant", `${result.deleted?.length || 0} item(s) deleted; ${result.failures?.length || 0} failed.`);
        });
      }
    }
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

  launcher?.addEventListener("click", () => {
    setNesti("idle");
    panel.returnValue = "";
    panel.showModal();
    checkAvailability().then(() => available && input.focus());
  });
  panel.addEventListener("close", () => {
    if (!continuousVoice) stopListening();
    if (panel.returnValue === "search") {
      const dashboardSearch = document.querySelector("#dashboardSearch");
      if (dashboardSearch) dashboardSearch.focus();
      else location.assign("/files.html");
    } else (launcher || floatOpen)?.focus();
  });
  panel.querySelector("[data-assistant-search]").addEventListener("click", () => panel.close("search"));

  async function startListening() {
    if (listening || !voiceAvailable) return;
    stopSpeaking();
    status.textContent = "Connecting to AssemblyAI…";
    setNesti("moving");
    try {
      const tokenResponse = await fetch("/api/ai/voice/tokens", { credentials: "include" });
      const tokens = await tokenResponse.json().catch(() => null);
      if (!tokenResponse.ok) throw new Error(tokens?.error || "Voice could not start.");
      pendingTtsToken = tokens.ttsToken;
      voiceConfiguration = tokens;
      requestingMicrophone = true;
      microphoneStream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }
      });
      requestingMicrophone = false;
      captureContext = new AudioContext();
      if (captureContext.state === "suspended") {
        await captureContext.resume();
        if (captureContext.state !== "running") {
          const error = new Error("The browser requires an interaction to resume voice.");
          error.name = "NotAllowedError";
          throw error;
        }
      }
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
        if (continuousVoice && !processingTurn && !voiceResumeRequired)
          setTimeout(startListening, 900);
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
      voiceResumeRequired = false;
      updateVoiceToggle();
      mic.setAttribute("aria-pressed", "true");
      mic.setAttribute("aria-label", "Stop voice input");
      mic.textContent = "⏹️";
      status.textContent = "Listening…";
      setNesti("listening");
    } catch (error) {
      stopListening();
      const browserRequiresGesture = error?.name === "NotAllowedError" || error?.name === "PermissionDeniedError";
      const microphoneDenied = browserRequiresGesture && requestingMicrophone;
      status.textContent = microphoneDenied
        ? "Microphone access was denied. Allow microphone access for this site, then choose Resume voice."
        : browserRequiresGesture
          ? "Your browser needs an interaction to resume voice. Choose Resume voice."
          : error.message || "Voice could not start.";
      setNesti("error");
      voiceResumeRequired = browserRequiresGesture;
      updateVoiceToggle();
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

  function stopSpeaking() {
    speechGeneration++;
    clearTimeout(playbackTimer);
    playbackTimer = undefined;
    if (activeTtsSocket?.readyState === WebSocket.OPEN) {
      activeTtsSocket.send(JSON.stringify({ type: "Cancel" }));
      activeTtsSocket.send(JSON.stringify({ type: "Terminate" }));
      activeTtsSocket.close(1000);
    }
    activeTtsSocket = undefined;
    for (const source of playbackSources) { try { source.stop(); } catch { } }
    playbackSources.clear();
    playbackContext?.close().catch(() => {});
    playbackContext = undefined;
  }

  async function speak(text) {
    const token = pendingTtsToken;
    pendingTtsToken = undefined;
    if (!token || !voiceConfiguration || !text) return;
    stopSpeaking();
    const generation = speechGeneration;
    const audioContext = new AudioContext();
    playbackContext = audioContext;
    let nextTime = audioContext.currentTime;
    const url = new URL("wss://tts.us.assemblyai.com/v1/ws");
    url.searchParams.set("token", token);
    url.searchParams.set("voice_id", voiceConfiguration.voiceId);
    url.searchParams.set("model_id", voiceConfiguration.ttsModel);
    url.searchParams.set("language", voiceConfiguration.language);
    url.searchParams.set("sample_rate", "24000");
    url.searchParams.set("encoding", "pcm_s16le");
    const socket = new WebSocket(url);
    activeTtsSocket = socket;
    socket.onopen = () => {
      if (generation !== speechGeneration) { socket.close(1000); return; }
      socket.send(JSON.stringify({ type: "Generate", text: text.slice(0, 2000) }));
      socket.send(JSON.stringify({ type: "Flush" }));
      status.textContent = "Nesti is speaking…";
      setNesti("speaking");
    };
    socket.onmessage = event => {
      if (generation !== speechGeneration) return;
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
        playbackSources.add(source);
        source.onended = () => playbackSources.delete(source);
        nextTime += buffer.duration;
      } else if (message.type === "FlushDone") {
        status.textContent = "";
        setNesti("success");
        socket.send(JSON.stringify({ type: "Terminate" }));
      }
    };
    socket.onerror = () => { if (generation === speechGeneration) { status.textContent = "Nesti could not play the spoken response."; setNesti("error"); } };
    socket.onclose = () => {
      if (generation !== speechGeneration) return;
      activeTtsSocket = undefined;
      playbackTimer = setTimeout(() => {
        if (generation !== speechGeneration) return;
      audioContext.close().catch(() => {});
      playbackContext = undefined;
      setNesti("idle");
      processingTurn = false;
      if (continuousVoice) setTimeout(startListening, 650);
      }, Math.max(0, (nextTime - audioContext.currentTime) * 1000 + 150));
    };
  }

  mic.addEventListener("click", () => listening ? stopListening() : startListening());

  function updateVoiceToggle() {
    if (voiceToggle) {
      voiceToggle.setAttribute("aria-pressed", String(continuousVoice));
      voiceToggle.classList.toggle("needs-permission", voiceResumeRequired);
      voiceToggle.textContent = voiceResumeRequired && continuousVoice
        ? "Resume voice"
        : continuousVoice ? "Voice On" : "Voice Off";
    }
    if (!listening)
      mic.setAttribute("aria-label", voiceResumeRequired && continuousVoice ? "Resume voice input" : "Start voice input");
  }

  voiceToggle?.addEventListener("click", event => {
    event.stopPropagation();
    if (voiceResumeRequired && continuousVoice) {
      voiceResumeRequired = false;
      updateVoiceToggle();
      startListening();
      return;
    }
    continuousVoice = !continuousVoice;
    localStorage.setItem("filenest:nesti-voice", continuousVoice ? "on" : "off");
    voiceResumeRequired = false;
    updateVoiceToggle();
    if (continuousVoice) checkAvailability().then(startListening);
    else { processingTurn = false; pendingTtsToken = undefined; stopListening(); stopSpeaking(); setNesti("idle"); }
  });

  function openAssistant() {
    if (!panel.open) {
      panel.returnValue = "";
      panel.showModal();
      checkAvailability().then(() => available && input.focus());
    }
  }
  if (companion && floatOpen) {
    const positionKey = "filenest:nesti-position";
    let savedPosition;
    let drag;
    let suppressPointerClick = false;
    try {
      const stored = JSON.parse(localStorage.getItem(positionKey));
      if (Number.isFinite(stored?.x) && Number.isFinite(stored?.y)) savedPosition = stored;
    } catch {}

    function placeCompanion(x, y) {
      const maxX = Math.max(0, document.documentElement.clientWidth - companion.offsetWidth);
      const maxY = Math.max(0, innerHeight - companion.offsetHeight);
      const position = {
        x: Math.max(0, Math.min(maxX, x)),
        y: Math.max(0, Math.min(maxY, y))
      };
      companion.style.left = position.x + "px";
      companion.style.top = position.y + "px";
      return position;
    }

    if (savedPosition) {
      companion.classList.add("user-positioned");
      placeCompanion(savedPosition.x, savedPosition.y);
    }

    addEventListener("resize", () => {
      if (drag) return;
      if (savedPosition) {
        placeCompanion(savedPosition.x, savedPosition.y);
      } else if (companion.style.left) {
        const box = companion.getBoundingClientRect();
        placeCompanion(box.left, box.top);
      }
    });

    floatOpen.addEventListener("click", event => {
      if (suppressPointerClick && event.detail !== 0) {
        suppressPointerClick = false;
        return;
      }
      openAssistant();
    });
    floatOpen.addEventListener("dragstart", event => event.preventDefault());
    floatOpen.addEventListener("pointerdown", event => {
      if (drag || !event.isPrimary || event.button !== 0) return;
      suppressPointerClick = false;
      const box = companion.getBoundingClientRect();
      drag = {
        id: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        offsetX: event.clientX - box.left,
        offsetY: event.clientY - box.top
      };
      floatOpen.setPointerCapture(event.pointerId);
    });
    floatOpen.addEventListener("pointermove", event => {
      if (event.pointerId !== drag?.id) return;
      if (!drag.position && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) < 5) return;
      companion.classList.add("dragging");
      drag.position = placeCompanion(event.clientX - drag.offsetX, event.clientY - drag.offsetY);
    });

    function finishDrag(event) {
      if (event.pointerId !== drag?.id) return;
      if (drag.position) {
        suppressPointerClick = true;
        savedPosition = drag.position;
        companion.classList.add("user-positioned");
        try { localStorage.setItem(positionKey, JSON.stringify(savedPosition)); } catch {}
      }
      drag = undefined;
      companion.classList.remove("dragging");
    }
    floatOpen.addEventListener("pointerup", finishDrag);
    floatOpen.addEventListener("pointercancel", finishDrag);
    floatOpen.addEventListener("lostpointercapture", finishDrag);

    function moveCompanion() {
      if (savedPosition || drag || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
      const margin = 12;
      const x = margin + Math.random() * Math.max(0, innerWidth - companion.offsetWidth - margin * 2);
      const y = margin + Math.random() * Math.max(0, innerHeight - companion.offsetHeight - margin * 2);
      placeCompanion(Math.round(x), Math.round(Math.max(72, y)));
    }
    setInterval(moveCompanion, 35000);
    setTimeout(moveCompanion, 8000);
  }

  restoreNavigationState();
  updateVoiceToggle();
  checkAvailability().then(() => { if (continuousVoice) startListening(); });
  addEventListener("pagehide", () => {
    if (!nestiNavigation) {
      stopListening();
      stopSpeaking();
    }
  }, { once: true });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    const message = input.value.trim();
    if (!message || !available || send.disabled) return;
    addMessage("user", message);
    processingTurn = true;
    if (listening) stopListening();
    input.value = "";
    input.disabled = send.disabled = true;
    status.textContent = "Nesti is thinking…";
    setNesti("thinking");
    try {
      const locationParams = new URLSearchParams(location.search);
      const response = await fetch("/api/ai/chat", {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          message,
          history: history.slice(-12),
          currentPath: locationParams.get("path"),
          currentCategory: locationParams.get("category"),
          currentDuplicateMode: locationParams.get("duplicates"),
          currentPage: location.pathname.split("/").pop() || "index.html"
        })
      });
      const result = await response.json().catch(() => null);
      if (!response.ok) throw new Error(result?.error || "Nesti could not answer.");
      history.push({ role: "user", content: message }, { role: "assistant", content: result.reply });
      if (history.length > 12) history.splice(0, history.length - 12);
      addMessage("assistant", result.reply);
      handleActions(result.actions);
      const willSpeak = !!pendingTtsToken;
      speak(result.reply);
      if (!willSpeak) {
        setNesti("success");
        processingTurn = false;
        if (continuousVoice) setTimeout(startListening, 700);
      }
      status.textContent = "";
    } catch (error) {
      status.textContent = error.message;
      setNesti("error");
      processingTurn = false;
      if (continuousVoice) setTimeout(startListening, 1200);
    } finally {
      input.disabled = send.disabled = !available;
      if (available) input.focus();
    }
  });
})();
