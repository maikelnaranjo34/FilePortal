/* ========================================================================
   Photos — Google Photos-style gallery front-end
   ===================================================================== */
(function () {
  "use strict";

  // ---------- state ----------
  const state = {
    me: null,
    view: "photos",
    timeline: { sections: [], skip: 0, take: 120, total: 0, hasMore: true, loading: false, favorites: false },
    flat: [],          // flat ordered list of photo dtos for the current view (for lightbox nav)
    selection: new Set(),
    selectMode: false,
    deleting: false,
    lb: { index: -1, slideshow: null, infoOpen: false },
    albumsCache: null,
  };

  const main = document.getElementById("main");
  const toastEl = document.getElementById("toast");

  // ---------- helpers ----------
  function api(url, opts) {
    return fetch(url, Object.assign({ credentials: "include" }, opts || {}));
  }
  async function apiJson(url, opts) {
    const r = await api(url, opts);
    if (r.status === 401) { window.location.replace("/login.html"); throw new Error("unauth"); }
    if (!r.ok) { let e = {}; try { e = await r.json(); } catch {} throw new Error(e.error || ("HTTP " + r.status)); }
    return r.json();
  }
  function el(tag, cls, html) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    return e;
  }
  function fmtSize(b) {
    if (b == null) return "";
    const u = ["B", "KB", "MB", "GB"]; let i = 0, n = b;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return n.toFixed(n < 10 && i > 0 ? 1 : 0) + " " + u[i];
  }
  function fmtDate(d) { try { return new Date(d).toLocaleString(); } catch { return d; } }
  let toastTimer;
  function toast(msg) {
    toastEl.textContent = msg; toastEl.classList.add("show");
    clearTimeout(toastTimer); toastTimer = setTimeout(() => toastEl.classList.remove("show"), 2600);
  }
  function thumbUrl(id) { return "/api/photos/" + id + "/thumbnail"; }
  function fileUrl(id, dl) { return "/api/photos/" + id + "/file" + (dl ? "?download=true" : ""); }

  // Lazy image loading via IntersectionObserver
  const imgObserver = new IntersectionObserver((entries) => {
    for (const en of entries) {
      if (en.isIntersecting) {
        const img = en.target;
        img.src = img.dataset.src;
        img.onload = () => img.classList.add("loaded");
        imgObserver.unobserve(img);
      }
    }
  }, { rootMargin: "300px" });

  // ---------- navigation ----------
  let timelineObserver;
  function setView(view, keyword) {
    timelineObserver?.disconnect();
    state.flat = [];
    state.view = view;
    if (window.location.hash !== "#" + view) window.history.replaceState(null, "", "#" + view);
    document.querySelectorAll(".nav a[data-view]").forEach(a =>
      a.classList.toggle("active", a.dataset.view === view));
    clearSelection();
    if (view === "photos") renderTimeline(false);
    else if (view === "favorites") renderTimeline(true);
    else if (view === "albums") renderAlbums();
    else if (view === "search") renderSearch(keyword);
  }

  document.querySelectorAll(".nav a[data-view]").forEach(a =>
    a.addEventListener("click", event => {
      event.preventDefault();
      setView(a.dataset.view);
    }));

  document.getElementById("navLogout").addEventListener("click", async event => {
    event.preventDefault();
    try { await api("/api/auth/logout", { method: "POST" }); } catch {}
    window.location.replace("/login.html");
  });

  // ---------- selection ----------
  function clearSelection() {
    state.selection.clear();
    state.selectMode = false;
    syncSelectionUI();
  }

  function toggleSelect(id) {
    if (state.deleting) return;
    state.selectMode = true;
    if (state.selection.has(id)) state.selection.delete(id);
    else state.selection.add(id);
    syncSelectionUI();
  }

  function syncSelectionUI() {
    main.classList.toggle("is-selecting", state.selectMode);
    main.querySelectorAll(".tile").forEach(tile => {
      const selected = state.selection.has(tile.dataset.id);
      tile.classList.toggle("selected", selected);
      tile.querySelector(".check")?.setAttribute("aria-pressed", selected);
    });
    const bar = document.getElementById("selbar");
    if (!bar) return;
    const select = bar.querySelector(".select-toggle");
    select.textContent = state.selectMode ? "Done" : "Select";
    select.setAttribute("aria-pressed", state.selectMode);
    select.disabled = state.deleting;
    bar.querySelector(".cnt").textContent = state.selectMode ? state.selection.size + " selected" : "";
    const actions = bar.querySelector(".actions");
    actions.hidden = !state.selectMode;
    actions.querySelectorAll("button").forEach(button => {
      button.disabled = state.deleting || state.selection.size === 0;
    });
  }

  // ---------- tile factory ----------
  function makeTile(p) {
    const tile = el("div", "tile" + (p.isFavorite ? " fav" : ""));
    tile.dataset.id = p.id;
    const img = el("img");
    img.dataset.src = thumbUrl(p.id);
    img.alt = p.fileName;
    imgObserver.observe(img);
    tile.appendChild(img);
    if (p.isVideo) tile.appendChild(el("div", "vbadge", "▶ video"));
    tile.appendChild(el("div", "star", "★"));
    const check = el("div", "check", "✓");
    tile.appendChild(check);

    check.addEventListener("click", (e) => { e.stopPropagation(); toggleSelect(p.id); });
    tile.addEventListener("click", () => {
      if (state.selectMode) { toggleSelect(p.id); return; }
      openLightbox(p.id);
    });
    return tile;
  }

  function selectionBar() {
    const bar = el("div", "selbar");
    bar.id = "selbar";
    const select = el("button", "btn select-toggle", "Select");
    select.type = "button";
    select.setAttribute("aria-pressed", "false");
    select.onclick = () => {
      if (state.selectMode) clearSelection();
      else {
        state.selectMode = true;
        syncSelectionUI();
      }
    };
    const count = el("span", "cnt");
    count.setAttribute("role", "status");
    const actions = el("div", "actions");
    actions.hidden = true;
    const add = (label, action, style = "") => {
      const button = el("button", "btn " + style, label);
      button.type = "button";
      button.disabled = true;
      button.onclick = () => action([...state.selection]);
      actions.appendChild(button);
    };
    if (state.me?.isAdmin) add("Delete selected", batchDelete, "danger");
    add("Download", downloadZip);
    add("Favorite", batchFavorite);
    add("Add to album", openAddToAlbum);
    bar.append(select, count, actions);
    return bar;
  }

  // ====================================================================
  //  TIMELINE (Photos / Favorites)
  // ====================================================================
  async function renderTimeline(favorites) {
    state.timeline = { sections: [], skip: 0, take: 120, total: 0, hasMore: true, loading: false, favorites };
    state.flat = [];
    main.innerHTML = "";
    main.scrollTop = 0;

    const head = el("div", "viewhead");
    head.appendChild(el("div", null,
      `<h2>${favorites ? "Favorites" : "Photos"}</h2><div class="sub" id="tlSub">Loading…</div>`));
    main.appendChild(head);
    main.appendChild(selectionBar());

    const container = el("div");
    container.id = "tlContainer";
    main.appendChild(container);

    const sentinel = el("div");
    sentinel.id = "tlSentinel";
    sentinel.style.height = "1px";
    main.appendChild(sentinel);

    await loadTimelinePage();

    if (!sentinel.isConnected) return;

    timelineObserver = new IntersectionObserver(async (ents) => {
      if (ents[0].isIntersecting && state.timeline.hasMore && !state.timeline.loading) {
        await loadTimelinePage();
      }
    }, { root: main, rootMargin: "400px" });
    timelineObserver.observe(sentinel);
  }

  async function loadTimelinePage() {
    const t = state.timeline;
    const container = document.getElementById("tlContainer");
    if (!container || t.loading || !t.hasMore) return;
    t.loading = true;

    let sp;
    if (t.skip === 0) { sp = el("div", "spinner"); container.appendChild(sp); }

    try {
      const data = await apiJson(`/api/photos/timeline?skip=${t.skip}&take=${t.take}&favorites=${t.favorites}`);
      if (!container.isConnected || t !== state.timeline) return;
      if (sp) sp.remove();
      t.total = data.total; t.hasMore = data.hasMore; t.skip += data.returned;

      const subEl = document.getElementById("tlSub");
      if (subEl) subEl.textContent = data.total + (t.favorites ? " favorites" : " items");

      if (data.total === 0 && t.skip === 0) {
        container.appendChild(el("div", "empty",
          t.favorites ? "No favorites yet. Tap the star on any photo." :
          "No photos found.<br><br>Make sure your <b>Roots</b> in appsettings.json point to folders with images, then click 🔄 to rescan."));
        t.loading = false; return;
      }

      for (const sec of data.sections) {
        // merge into last section if same day already rendered
        let secEl = container.querySelector(`.section[data-key="${sec.key}"]`);
        let grid;
        if (!secEl) {
          secEl = el("div", "section"); secEl.dataset.key = sec.key;
          secEl.appendChild(el("h3", null, sec.title));
          grid = el("div", "grid"); secEl.appendChild(grid);
          container.appendChild(secEl);
        } else {
          grid = secEl.querySelector(".grid");
        }
        for (const p of sec.photos) {
          state.flat.push(p);
          grid.appendChild(makeTile(p));
        }
      }
    } catch (e) {
      if (sp) sp.remove();
      if (e.message !== "unauth") toast("Error: " + e.message);
    } finally {
      t.loading = false;
    }
  }

  // ====================================================================
  //  ALBUMS
  // ====================================================================
  async function renderAlbums() {
    main.innerHTML = "";
    const head = el("div", "viewhead");
    head.appendChild(el("div", null, `<h2>Albums</h2><div class="sub">Organize your photos into collections</div>`));
    const newBtn = el("button", "btn primary", "➕ New album");
    newBtn.onclick = openCreateAlbum;
    head.appendChild(newBtn);
    main.appendChild(head);

    const sp = el("div", "spinner"); main.appendChild(sp);
    try {
      const albums = await apiJson("/api/albums");
      if (!sp.isConnected) return;
      state.albumsCache = albums;
      sp.remove();
      if (!albums.length) { main.appendChild(el("div", "empty", "No albums yet. Create one to get started.")); return; }
      const grid = el("div", "albumgrid");
      for (const a of albums) {
        const card = el("div", "albumcard");
        const cover = el("div", "cover");
        if (a.coverPhotoId) {
          const img = el("img"); img.src = thumbUrl(a.coverPhotoId); cover.appendChild(img);
        } else { cover.textContent = "📚"; cover.style.fontSize = "40px"; }
        card.appendChild(cover);
        const name = el("div", "nm");
        name.textContent = a.name;
        card.appendChild(name);
        card.appendChild(el("div", "ct", a.count + " item" + (a.count === 1 ? "" : "s")));
        card.onclick = () => openAlbum(a.id);
        grid.appendChild(card);
      }
      main.appendChild(grid);
    } catch (e) { sp.remove(); if (e.message !== "unauth") toast("Error: " + e.message); }
  }

  async function openAlbum(id) {
    main.innerHTML = "";
    const sp = el("div", "spinner"); main.appendChild(sp);
    try {
      const a = await apiJson("/api/albums/" + encodeURIComponent(id));
      if (!sp.isConnected) return;
      sp.remove();
      const head = el("div", "viewhead");
      const left = el("div", null, `<h2>${escapeHtml(a.name)}</h2><div class="sub">${escapeHtml(a.description || "")} · ${a.photos.length} items</div>`);
      head.appendChild(left);
      const actions = el("div", null, "");
      actions.style.display = "flex"; actions.style.gap = "8px";
      const back = el("button", "btn", "← Albums"); back.onclick = () => renderAlbums();
      const share = el("button", "btn", "🔗 Share"); share.onclick = () => createShare("album", id);
      const ren = el("button", "btn", "✏️ Rename"); ren.onclick = () => openRenameAlbum(a);
      const del = el("button", "btn danger", "🗑️ Delete"); del.onclick = () => deleteAlbum(id);
      actions.appendChild(back); actions.appendChild(share); actions.appendChild(ren); actions.appendChild(del);
      head.appendChild(actions);
      main.appendChild(head);
      main.appendChild(selectionBar());

      state.flat = a.photos.map(p => ({ id: p.id, fileName: p.fileName, isVideo: p.isVideo }));
      if (!a.photos.length) { main.appendChild(el("div", "empty", "This album is empty. Add photos from the Photos tab.")); return; }
      const grid = el("div", "grid");
      for (const p of a.photos) grid.appendChild(makeTile(p));
      main.appendChild(grid);
    } catch (e) { sp.remove(); if (e.message !== "unauth") toast("Error: " + e.message); }
  }

  function openCreateAlbum() {
    showModal(`
      <h3>New album</h3>
      <label>Name</label><input id="alName" placeholder="My album" />
      <label>Description (optional)</label><textarea id="alDesc" rows="2"></textarea>
      <div class="actions">
        <button class="btn" id="alCancel">Cancel</button>
        <button class="btn primary" id="alCreate">Create</button>
      </div>`);
    document.getElementById("alCancel").onclick = hideModal;
    document.getElementById("alCreate").onclick = async () => {
      const name = document.getElementById("alName").value.trim();
      if (!name) { toast("Name required"); return; }
      try {
        await apiJson("/api/albums", { method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ name, description: document.getElementById("alDesc").value.trim() }) });
        hideModal(); toast("Album created"); renderAlbums();
      } catch (e) { toast("Error: " + e.message); }
    };
  }

  function openRenameAlbum(a) {
    showModal(`
      <h3>Edit album</h3>
      <label>Name</label><input id="alName" value="${escapeAttr(a.name)}" />
      <label>Description</label><textarea id="alDesc" rows="2">${escapeHtml(a.description || "")}</textarea>
      <div class="actions"><button class="btn" id="alCancel">Cancel</button><button class="btn primary" id="alSave">Save</button></div>`);
    document.getElementById("alCancel").onclick = hideModal;
    document.getElementById("alSave").onclick = async () => {
      try {
        await apiJson("/api/albums/" + a.id, { method: "PUT", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ name: document.getElementById("alName").value.trim(), description: document.getElementById("alDesc").value.trim() }) });
        hideModal(); openAlbum(a.id);
      } catch (e) { toast("Error: " + e.message); }
    };
  }

  async function deleteAlbum(id) {
    if (!confirm("Delete this album? Photos are not deleted.")) return;
    try { await apiJson("/api/albums/" + id, { method: "DELETE" }); toast("Album deleted"); renderAlbums(); }
    catch (e) { toast("Error: " + e.message); }
  }

  async function openAddToAlbum(ids) {
    if (!ids.length) return;
    let albums = state.albumsCache;
    try { albums = await apiJson("/api/albums"); state.albumsCache = albums; } catch {}
    let rows = (albums || []).map(a =>
      `<div class="it" data-id="${escapeAttr(a.id)}"><span>📚</span><span>${escapeHtml(a.name)}</span><span style="margin-left:auto;color:#9aa4b2">${a.count}</span></div>`).join("");
    showModal(`
      <h3>Add ${ids.length} item(s) to album</h3>
      <button class="btn primary" id="newAlbumInline" style="width:100%">➕ Create new album</button>
      <div class="albumlist">${rows || '<div class="empty">No albums yet</div>'}</div>
      <div class="actions"><button class="btn" id="alCancel">Cancel</button></div>`);
    document.getElementById("alCancel").onclick = hideModal;
    document.getElementById("newAlbumInline").onclick = async () => {
      const name = prompt("New album name:"); if (!name) return;
      try {
        const a = await apiJson("/api/albums", { method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ name, photoIds: ids }) });
        hideModal(); clearSelection(); toast("Added to " + a.name); state.albumsCache = null;
      } catch (e) { toast("Error: " + e.message); }
    };
    document.querySelectorAll(".albumlist .it").forEach(it => it.onclick = async () => {
      try {
        await apiJson("/api/albums/" + it.dataset.id + "/photos", { method: "POST",
          headers: { "Content-Type": "application/json" }, body: JSON.stringify({ photoIds: ids }) });
        hideModal(); clearSelection(); toast("Added to album"); state.albumsCache = null;
      } catch (e) { toast("Error: " + e.message); }
    });
  }

  // ====================================================================
  //  SEARCH
  // ====================================================================
  async function renderSearch(prefill) {
    main.innerHTML = "";
    const head = el("div", "viewhead");
    head.appendChild(el("div", null, `<h2>Search</h2><div class="sub">Find photos by name, date, type, or camera</div>`));
    main.appendChild(head);

    const filters = el("div", "filters");
    filters.innerHTML = `
      <div class="f"><label>Keyword</label><input id="sQ" placeholder="filename / camera / folder" value="${escapeAttr(prefill||'')}"/></div>
      <div class="f"><label>From</label><input id="sFrom" type="date"/></div>
      <div class="f"><label>To</label><input id="sTo" type="date"/></div>
      <div class="f"><label>Type</label><select id="sType"><option value="all">All</option><option value="photos">Photos</option><option value="videos">Videos</option></select></div>
      <div class="f"><label>Camera</label><select id="sCam"><option value="">Any</option></select></div>
      <button class="btn primary" id="sGo">Search</button>`;
    main.appendChild(filters);
    main.appendChild(selectionBar());
    const results = el("div"); results.id = "sResults"; main.appendChild(results);

    // populate cameras
    try {
      const cams = await apiJson("/api/search/cameras");
      const sel = filters.querySelector("#sCam");
      cams.forEach(c => { const o = el("option"); o.value = c; o.textContent = c; sel.appendChild(o); });
    } catch {}

    if (!filters.isConnected) return;
    let latestSearch = 0;
    const doSearch = async () => {
      const request = ++latestSearch;
      const q = document.getElementById("sQ").value.trim();
      const from = document.getElementById("sFrom").value;
      const to = document.getElementById("sTo").value;
      const type = document.getElementById("sType").value;
      const cam = document.getElementById("sCam").value;
      const params = new URLSearchParams();
      if (q) params.set("q", q);
      if (from) params.set("from", from);
      if (to) params.set("to", to);
      if (type) params.set("type", type);
      if (cam) params.set("camera", cam);
      params.set("take", "300");
      results.innerHTML = ""; const sp = el("div", "spinner"); results.appendChild(sp);
      try {
        const data = await apiJson("/api/search?" + params.toString());
        if (request !== latestSearch || !results.isConnected) return;
        sp.remove();
        results.innerHTML = `<div class="sub" style="margin-bottom:10px">${data.total} result(s)</div>`;
        state.flat = data.results;
        if (!data.results.length) { results.appendChild(el("div", "empty", "No matches.")); return; }
        const grid = el("div", "grid");
        data.results.forEach(p => grid.appendChild(makeTile(p)));
        results.appendChild(grid);
      } catch (e) { sp.remove(); toast("Error: " + e.message); }
    };
    document.getElementById("sGo").onclick = doSearch;
    document.getElementById("sQ").addEventListener("keydown", e => { if (e.key === "Enter") doSearch(); });
    if (prefill) doSearch();
  }

  // ====================================================================
  //  BATCH OPERATIONS
  // ====================================================================
  function downloadZip(ids) {
    if (!ids.length) return;

    const form = document.createElement("form");
    form.method = "POST";
    form.action = "/api/photos/batch/download-form";
    form.target = "galleryDownload";
    form.hidden = true;
    for (const id of ids) {
      const input = document.createElement("input");
      input.type = "hidden";
      input.name = "ids";
      input.value = id;
      form.appendChild(input);
    }

    document.body.appendChild(form);
    form.submit();
    form.remove();
    toast("Preparing download?");
    clearSelection();
  }

  document.getElementById("galleryDownload").addEventListener("load", event => {
    const text = event.currentTarget.contentDocument?.body?.textContent;
    if (!text) return;
    try {
      const error = JSON.parse(text)?.error;
      if (error) toast(error);
    } catch {}
  });

  async function batchFavorite(ids) {
    try {
      for (const id of ids) {
        await apiJson("/api/photos/" + id + "/favorite", { method: "POST",
          headers: { "Content-Type": "application/json" }, body: JSON.stringify({ favorite: true }) });
        const p = state.flat.find(x => x.id === id); if (p) p.isFavorite = true;
        document.querySelector(`.tile[data-id="${id}"]`)?.classList.add("fav");
      }
      toast("Added to favorites"); clearSelection();
    } catch (e) { toast("Error: " + e.message); }
  }
  async function batchDelete(ids) {
    if (state.deleting || !ids.length || !state.me?.isAdmin) return;
    const item = ids.length === 1 && state.flat.find(photo => photo.id === ids[0]);
    const subject = item ? '"' + item.fileName + '"' : ids.length + " selected files";
    if (!confirm("Permanently delete " + subject + " from disk?")) return;

    state.deleting = true;
    lbDelete.disabled = true;
    syncSelectionUI();
    try {
      const result = await apiJson("/api/photos/batch/delete", { method: "POST",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ids }) });
      const deleted = new Set(result.deletedIds);
      if (deleted.has(state.flat[state.lb.index]?.id)) closeLightbox();
      state.flat = state.flat.filter(photo => !deleted.has(photo.id));
      main.querySelectorAll(".tile").forEach(tile => {
        if (deleted.has(tile.dataset.id)) tile.remove();
      });
      for (const id of deleted) state.selection.delete(id);
      const heading = document.getElementById("tlSub");
      if (heading && deleted.size) {
        state.timeline.total = Math.max(0, state.timeline.total - deleted.size);
        state.timeline.skip = Math.max(0, state.timeline.skip - deleted.size);
        heading.textContent = state.timeline.total + (state.timeline.favorites ? " favorites" : " items");
      }
      if (!state.selection.size) clearSelection();
      toast(result.deleted + (result.deleted === 1 ? " file deleted." : " files deleted.") +
        (result.errors.length ? " " + result.errors.length + " could not be deleted." : ""));
    } catch (error) {
      if (error.message !== "unauth") toast(error.message);
    } finally {
      state.deleting = false;
      lbDelete.disabled = false;
      syncSelectionUI();
    }
  }

  // ====================================================================
  //  SHARING
  // ====================================================================
  async function createShare(type, targetId) {
    try {
      const data = await apiJson("/api/sharing", { method: "POST",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify({ type, targetId }) });
      showModal(`
        <h3>Share link</h3>
        <p style="color:#9aa4b2;font-size:13px">Anyone with this link can view the ${type}.</p>
        <input id="shareUrl" value="${escapeAttr(data.url)}" readonly />
        <div class="actions">
          <button class="btn" id="alCancel">Close</button>
          <button class="btn primary" id="copyShare">📋 Copy link</button>
        </div>`);
      document.getElementById("alCancel").onclick = hideModal;
      document.getElementById("copyShare").onclick = () => {
        const inp = document.getElementById("shareUrl"); inp.select();
        navigator.clipboard?.writeText(inp.value); toast("Link copied");
      };
    } catch (e) { toast("Error: " + e.message); }
  }

  // ====================================================================
  //  LIGHTBOX / VIEWER
  // ====================================================================
  const lb = document.getElementById("lightbox");
  const lbMedia = document.getElementById("lbMedia");
  const lbTitle = document.getElementById("lbTitle");
  const lbInfo = document.getElementById("lbInfo");
  const lbInfoBody = document.getElementById("lbInfoBody");
  const lbFav = document.getElementById("lbFav");
  const lbDelete = document.getElementById("lbDelete");

  function openLightbox(id) {
    const idx = state.flat.findIndex(p => p.id === id);
    if (idx < 0) return;
    state.lb.index = idx;
    lb.classList.add("show");
    showLbItem();
  }
  function closeLightbox() {
    lb.classList.remove("show");
    stopSlideshow();
    lbMedia.replaceChildren();
    lbMedia.classList.remove("quarter-turn");
  }
  async function showLbItem() {
    const p = state.flat[state.lb.index];
    if (!p) return;
    lbTitle.textContent = p.fileName || "";
    lbDelete.hidden = !state.me?.isAdmin;
    lbMedia.replaceChildren();
    lbMedia.classList.remove("quarter-turn");
    if (p.isVideo) {
      const v = document.createElement("video");
      v.src = fileUrl(p.id); v.controls = true; v.autoplay = true; v.playsInline = true;
      lbMedia.appendChild(v);
    } else {
      const img = document.createElement("img");
      img.src = fileUrl(p.id);
      lbMedia.appendChild(img);
    }
    lbFav.textContent = p.isFavorite ? "★" : "☆";
    if (state.lb.infoOpen) loadInfo(p.id);
  }
  function lbNav(delta) {
    let i = state.lb.index + delta;
    if (i < 0) i = state.flat.length - 1;
    if (i >= state.flat.length) i = 0;
    state.lb.index = i; showLbItem();
  }
  async function loadInfo(id) {
    lbInfoBody.innerHTML = '<div class="spinner"></div>';
    try {
      const d = await apiJson("/api/photos/" + id);
      const rows = [];
      const add = (k, v) => { if (v != null && v !== "") rows.push(`<div class="row"><div class="k">${k}</div><div class="v">${escapeHtml(String(v))}</div></div>`); };
      add("Name", d.fileName);
      add("Date taken", fmtDate(d.dateTaken));
      add("Size", fmtSize(d.size));
      if (d.width && d.height) add("Dimensions", d.width + " × " + d.height);
      add("Type", d.isVideo ? "Video" : "Image");
      if (d.cameraMake || d.cameraModel) add("Camera", [d.cameraMake, d.cameraModel].filter(Boolean).join(" "));
      if (d.latitude != null && d.longitude != null) {
        rows.push(`<div class="row"><div class="k">Location</div><div class="v"><a target="_blank" href="https://www.openstreetmap.org/?mlat=${d.latitude}&mlon=${d.longitude}#map=15/${d.latitude}/${d.longitude}">${d.latitude.toFixed(5)}, ${d.longitude.toFixed(5)}</a></div></div>`);
      }
      lbInfoBody.innerHTML = rows.join("");
    } catch (e) { lbInfoBody.innerHTML = '<div class="empty">No metadata</div>'; }
  }
  function toggleInfo() {
    state.lb.infoOpen = !state.lb.infoOpen;
    lbInfo.classList.toggle("show", state.lb.infoOpen);
    if (state.lb.infoOpen) loadInfo(state.flat[state.lb.index].id);
  }
  async function toggleLbFav() {
    const p = state.flat[state.lb.index];
    const next = !p.isFavorite;
    try {
      await apiJson("/api/photos/" + p.id + "/favorite", { method: "POST",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify({ favorite: next }) });
      p.isFavorite = next;
      lbFav.textContent = next ? "★" : "☆";
      document.querySelector(`.tile[data-id="${p.id}"]`)?.classList.toggle("fav", next);
      toast(next ? "Added to favorites" : "Removed from favorites");
    } catch (e) { toast("Error: " + e.message); }
  }
  function startSlideshow() {
    if (state.lb.slideshow) { stopSlideshow(); return; }
    document.getElementById("lbSlideshow").textContent = "⏸️";
    state.lb.slideshow = setInterval(() => lbNav(1), 3000);
  }
  function stopSlideshow() {
    if (state.lb.slideshow) { clearInterval(state.lb.slideshow); state.lb.slideshow = null; }
    document.getElementById("lbSlideshow").textContent = "▶️";
  }
  function toggleFullscreen() {
    if (!document.fullscreenElement) lb.requestFullscreen?.();
    else document.exitFullscreen?.();
  }

  document.getElementById("lbClose").onclick = closeLightbox;
  document.getElementById("lbPrev").onclick = () => lbNav(-1);
  document.getElementById("lbNext").onclick = () => lbNav(1);
  document.getElementById("lbInfoBtn").onclick = toggleInfo;
  document.getElementById("lbFav").onclick = toggleLbFav;
  document.getElementById("lbRotateLeft").onclick = () => previewRotation.turn(lbMedia.firstElementChild, -90);
  document.getElementById("lbRotateRight").onclick = () => previewRotation.turn(lbMedia.firstElementChild, 90);
  lbDelete.onclick = () => {
    const item = state.flat[state.lb.index];
    if (item) batchDelete([item.id]);
  };
  document.getElementById("lbSlideshow").onclick = startSlideshow;
  document.getElementById("lbFull").onclick = toggleFullscreen;
  document.getElementById("lbDownload").onclick = () => {
    const p = state.flat[state.lb.index];
    const a = document.createElement("a"); a.href = fileUrl(p.id, true); a.download = p.fileName; a.click();
  };
  document.getElementById("lbShare").onclick = () => createShare("photo", state.flat[state.lb.index].id);

  document.addEventListener("keydown", (e) => {
    if (!lb.classList.contains("show")) return;
    if (e.key === "Escape") closeLightbox();
    else if (e.key === "ArrowLeft") lbNav(-1);
    else if (e.key === "ArrowRight") lbNav(1);
    else if (e.key === "i") toggleInfo();
    else if (e.key === "f") toggleLbFav();
    else if (e.key === " ") { e.preventDefault(); startSlideshow(); }
  });

  // ---------- modal ----------
  const modal = document.getElementById("modal");
  const modalBox = document.getElementById("modalBox");
  function showModal(html) { modalBox.innerHTML = html; modal.classList.add("show"); }
  function hideModal() { modal.classList.remove("show"); modalBox.innerHTML = ""; }
  modal.addEventListener("click", e => { if (e.target === modal) hideModal(); });

  // ---------- global search ----------
  document.getElementById("globalSearch").addEventListener("keydown", e => {
    if (e.key === "Enter") setView("search", e.target.value.trim());
  });

  // ---------- escape helpers ----------
  function escapeHtml(s) { return String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }
  function escapeAttr(s) { return escapeHtml(s); }

  // ---------- boot ----------
  (async () => {
    try {
      state.me = await apiJson("/api/auth/me");
    } catch { return; }
    document.getElementById("avatar").textContent = (state.me.user || "U").charAt(0).toUpperCase();
    document.getElementById("avatar").title = state.me.user + (state.me.isAdmin ? " (admin)" : "");
    if (state.me.isAdmin) {
      document.getElementById("navUsers").textContent = "Manage users";
      document.getElementById("scanBtn").style.display = "inline-block";
    }
    document.getElementById("scanBtn").onclick = async () => {
      toast("Scanning library…");
      try { const r = await apiJson("/api/photos/scan", { method: "POST" }); toast("Found " + r.count + " media files"); setView(state.view); }
      catch (e) { toast("Error: " + e.message); }
    };
    const requestedView = window.location.hash.slice(1);
    setView(["photos", "albums", "favorites", "search"].includes(requestedView) ? requestedView : "photos");
  })();

})();
