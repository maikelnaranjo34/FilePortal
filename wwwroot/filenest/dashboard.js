(() => {
  "use strict";
  const { account, activate, activity, bytes, fileLink, icon, request, signInUrl } = window.FileNest;
  const byId = id => document.getElementById(id);
  const status = byId("dashboardStatus");
  const dashboard = byId("dashboard");
  const dialog = byId("uploadDialog");
  const destination = byId("uploadDestination");
  const views = { "#shared": "Shared", "#trash": "Trash", "#recent": "Recent Files" };
  let roots = [], user, currentRoute, uploadMode, droppedFiles = [], uploading = false;

  function node(tag, text, className) {
    const element = document.createElement(tag);
    if (text) element.textContent = text;
    if (className) element.className = className;
    return element;
  }
  function empty(message) { return node("p", message, "fn-empty"); }
  function sizedLabel(file, detail) {
    return [detail, Number.isFinite(file.size) ? bytes(file.size) : ""].filter(Boolean).join(" · ");
  }
  function tileIcon(file) {
    const extension = (file.extension || file.name.split(".").at(-1)).replace(/^\./, "").toLowerCase();
    const color = ({ pdf: "pdf", txt: "notes", md: "notes", png: "images", jpg: "images", jpeg: "images", mp4: "video" })[extension] || "";
    const tile = node("span", "", "fn-file-icon " + color);
    tile.innerHTML = icon(color === "images" ? "gallery" : "file");
    return tile;
  }
  function rows(files, noItems) {
    if (!files.length) return empty(noItems);
    const list = node("ul", "", "fn-list");
    for (const file of files) {
      const row = node("li");
      const link = node("a", "", "fn-row");
      link.href = file.href;
      const label = node("span", "", "fn-file-label");
      label.append(node("strong", file.name), node("small", sizedLabel(file, file.detail)));
      link.append(tileIcon(file), label);
      row.append(link);
      list.append(row);
    }
    return list;
  }
  function samePath(a, b) {
    return a.replace(/[\\/]+$/, "").toLowerCase() === b.replace(/[\\/]+$/, "").toLowerCase();
  }
  function renderRoots() {
    const pins = activity(user, "pins");
    const pinned = roots.filter(root => pins.some(pin => samePath(pin.path, root.path)));
    function cards(target, folders, fallback) {
      const content = folders.map(root => {
        const card = node("div", "", "fn-root");
        const link = node("a");
        link.href = fileLink(root.path);
        const glyph = node("span");
        glyph.innerHTML = icon("folder");
        link.append(glyph, node("span", root.name));
        const pin = node("button");
        pin.type = "button";
        pin.innerHTML = icon("pin");
        const isPinned = pinned.some(folder => samePath(root.path, folder.path));
        pin.setAttribute("aria-pressed", isPinned);
        pin.setAttribute("aria-label", (isPinned ? "Unpin " : "Pin ") + root.name);
        pin.addEventListener("click", () => {
          activity(user, "pins", root);
          renderRoots();
          const next = [...byId(target).querySelectorAll("button")].find(button =>
            button.getAttribute("aria-label") === (isPinned ? "Pin " : "Unpin ") + root.name);
          (next || byId("quickRoots").querySelector("button"))?.focus();
        });
        card.append(link, pin);
        return card;
      });
      byId(target).replaceChildren(...(content.length ? content : [empty(fallback)]));
    }
    cards("quickRoots", roots, "No folders are assigned to your account.");
    cards("pinnedFolders", pinned, "Pin a folder from Quick Access.");
  }

  async function verifiedActivities(type, limit) {
    const entries = activity(user, type).slice(0, limit);
    const folders = new Map();
    const found = await Promise.all(entries.map(async entry => {
      if (!entry.folder || !roots.some(root => {
        const base = root.path.replace(/[\\/]+$/, "").toLowerCase();
        const folder = entry.folder.replace(/\//g, "\\").toLowerCase();
        return folder === base || folder.startsWith(base + "\\");
      })) return null;
      if (!folders.has(entry.folder)) folders.set(entry.folder,
        request("/api/files/folder?" + new URLSearchParams({ path: entry.folder })).catch(() => []));
      const items = await folders.get(entry.folder);
      const file = items.find(item => !item.isDirectory && samePath(item.path, entry.path));
      return file ? { ...file, detail: (type === "uploads" ? "Uploaded " : "Viewed ") + new Date(entry.at).toLocaleDateString(),
        href: fileLink(entry.folder, file.path) } : null;
    }));
    return found.filter(Boolean);
  }

  async function showActivity(target, type, limit) {
    target.replaceChildren(empty("Loading…"));
    const items = await verifiedActivities(type, limit);
    target.replaceChildren(rows(items, type === "uploads" ? "Uploads made here will appear here." : "Files you open on this browser will appear here."));
  }

  async function showFavorites() {
    const section = byId("favoriteFiles");
    try {
      const data = await request("/api/photos/timeline?favorites=true&take=4");
      section.replaceChildren(rows(data.sections.flatMap(group => group.photos.map(file => ({
        name: file.fileName, size: file.size, detail: file.isVideo ? "Video" : "Photo",
        href: "/gallery.html?item=" + encodeURIComponent(file.id) + "#favorites"
      }))), "Star photos and videos in Gallery to see them here."));
    } catch (error) { section.replaceChildren(empty(error.message)); }
  }

  async function showShares(target, route) {
    target.replaceChildren(empty("Loading share links…"));
    try {
      const shares = await request("/api/sharing/mine");
      if (currentRoute !== route) return;
      if (!shares.length) {
        const link = node("a", "Open Gallery", "fn-inline-link");
        link.href = "/gallery.html";
        target.replaceChildren(empty("Links you create for photos and albums will appear here."), link);
        return;
      }
      const list = node("ul", "", "fn-list");
      for (const share of shares) {
        const item = node("li");
        const label = node("a", share.type === "album" ? "Shared album" : "Shared media", "fn-row");
        label.href = "/share.html?" + new URLSearchParams({ token: share.token });
        const date = share.expiresUtc ? "Expires " + new Date(share.expiresUtc).toLocaleDateString() : "No expiry";
        label.append(node("small", date, "fn-muted"));
        const button = node("button", "Revoke", "fn-button");
        button.type = "button";
        button.addEventListener("click", async () => {
          if (!confirm("Revoke this share link?")) return;
          button.disabled = true;
          try {
            await request("/api/sharing/" + encodeURIComponent(share.token), { method: "DELETE" });
            if (currentRoute === route) showShares(target, route);
          } catch (error) {
            if (currentRoute === route) status.textContent = error.message;
            button.disabled = false;
          }
        });
        item.append(label, button);
        list.append(item);
      }
      target.replaceChildren(list);
    } catch (error) {
      if (currentRoute === route) target.replaceChildren(empty(error.message));
    }
  }

  function renderRoute() {
    const hash = location.hash;
    if (["#photos", "#videos", "#albums", "#favorites", "#search"].includes(hash)) {
      location.replace("/gallery.html" + location.search + hash);
      return;
    }
    currentRoute = views[hash] ? hash : "";
    const view = byId("subview");
    dashboard.hidden = Boolean(currentRoute);
    view.hidden = !currentRoute;
    status.textContent = "";
    if (!currentRoute) {
      document.title = "FileNest";
      activate("home");
      return;
    }

    activate(hash.slice(1));
    const heading = byId("subviewTitle");
    heading.textContent = views[hash];
    document.title = views[hash] + " · FileNest";
    heading.focus({ preventScroll: true });
    const target = byId("subviewContent");
    if (hash === "#shared") showShares(target, hash);
    if (hash === "#recent") {
      const recent = node("div");
      target.replaceChildren(recent);
      showActivity(recent, "recent", 15);
    }
    if (hash === "#trash") {
      const disabled = node("button", "Restore", "fn-button");
      disabled.disabled = true;
      target.replaceChildren(empty("This FilePortal version deletes files permanently. Trash and recovery are unavailable."), disabled);
    }
  }

  function setOptions(select) {
    select.replaceChildren(...roots.map(root => new Option(root.name, root.path)));
    select.disabled = !roots.length;
  }
  function configureFolders() {
    setOptions(destination);
    setOptions(byId("storageFolder"));
    for (const id of ["uploadFiles", "uploadFolder", "storageButton"])
      byId(id).disabled = !roots.length;
    if (!("webkitdirectory" in byId("dashboardFolder"))) byId("uploadFolder").disabled = true;
    renderRoots();
  }

  function openUpload(mode, files = []) {
    if (!roots.length || uploading) return;
    droppedFiles = files;
    uploadMode = mode;
    byId("uploadSummary").textContent = files.length ? files.length + " files selected." : "Choose a root here or upload inside a subfolder from My Files.";
    byId("uploadConfirm").textContent = files.length ? "Upload" : "Choose " + (mode === "folder" ? "folder" : "files");
    dialog.showModal();
  }

  async function upload(files, folder) {
    if (!files.length || uploading) return;
    uploading = true;
    byId("uploadFiles").disabled = true;
    byId("uploadFolder").disabled = true;
    status.textContent = "Uploading " + files.length + (files.length === 1 ? " file…" : " files…");
    const form = new FormData();
    for (const file of files) form.append("files", file, file.webkitRelativePath || file.name);
    try {
      await request("/api/files/upload?" + new URLSearchParams({ targetPath: folder }), { method: "POST", body: form });
      const separator = folder.includes("\\") ? "\\" : "/";
      const base = folder.replace(/[\\/]+$/, "");
      for (const file of files.slice(-15)) {
        const parts = (file.webkitRelativePath || file.name).split("/");
        const parent = parts.length === 1 ? folder : [base, ...parts.slice(0, -1)].join(separator);
        activity(user, "uploads", { folder: parent, path: parent + separator + file.name });
      }
      status.textContent = "Upload complete.";
      await showActivity(byId("recentUploads"), "uploads", 4);
    } catch (error) { status.textContent = error.message; }
    finally {
      uploading = false;
      byId("uploadFiles").disabled = false;
      byId("uploadFolder").disabled = !("webkitdirectory" in byId("dashboardFolder"));
    }
  }

  byId("uploadFiles").addEventListener("click", () => openUpload("files"));
  byId("uploadFolder").addEventListener("click", () => openUpload("folder"));
  byId("uploadConfirm").addEventListener("click", () => {
    const folder = destination.value;
    dialog.close();
    if (uploadMode === "drop") { upload(droppedFiles, folder); droppedFiles = []; return; }
    const input = byId(uploadMode === "folder" ? "dashboardFolder" : "dashboardFiles");
    input.dataset.folder = folder;
    input.click();
  });
  for (const id of ["dashboardFiles", "dashboardFolder"]) {
    byId(id).addEventListener("change", event => {
      const input = event.currentTarget;
      const files = [...input.files];
      input.value = "";
      upload(files, input.dataset.folder);
    });
  }
  const dropArea = byId("dropArea");
  dropArea.addEventListener("dragover", event => {
    if (!event.dataTransfer.types.includes("Files")) return;
    event.preventDefault();
    dropArea.classList.toggle("fn-drag", roots.length > 0 && !uploading);
  });
  dropArea.addEventListener("dragleave", event => {
    if (!dropArea.contains(event.relatedTarget)) dropArea.classList.remove("fn-drag");
  });
  dropArea.addEventListener("drop", event => {
    event.preventDefault();
    dropArea.classList.remove("fn-drag");
    const directory = [...event.dataTransfer.items].some(item => item.webkitGetAsEntry?.()?.isDirectory);
    if (directory) { status.textContent = "Use Upload Folder to upload a directory."; return; }
    if (event.dataTransfer.files.length) openUpload("drop", [...event.dataTransfer.files]);
  });

  byId("dashboardSearchForm").addEventListener("submit", event => {
    event.preventDefault();
    const query = byId("dashboardSearch").value.trim();
    if (query) location.href = "/files.html?" + new URLSearchParams({ q: query });
  });
  document.addEventListener("keydown", event => {
    if (dashboard.hidden || document.querySelector("dialog[open]")) return;
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
      event.preventDefault();
      byId("dashboardSearch").focus();
    }
  });

  byId("storageForm").addEventListener("submit", async event => {
    event.preventDefault();
    const select = byId("storageFolder");
    const detail = byId("storageDetail");
    const summary = byId("storageSummary");
    const button = byId("storageButton");
    button.disabled = true;
    select.disabled = true;
    detail.textContent = "Loading folder size…";
    try {
      const files = (await request("/api/files/folder?" + new URLSearchParams({ path: select.value })))
        .filter(item => !item.isDirectory);
      const total = files.reduce((size, file) => size + (Number.isFinite(file.size) ? file.size : 0), 0);
      const sizeLabel = files.every(file => Number.isFinite(file.size)) ? bytes(total) : bytes(total) + " of known sizes";
      const info = files.length + (files.length === 1 ? " file" : " files") + " · " + sizeLabel;
      detail.textContent = info + " directly in " + select.selectedOptions[0].text + ".";
      summary.textContent = detail.textContent;
    } catch (error) { detail.textContent = error.message; }
    finally { button.disabled = false; select.disabled = false; }
  });

  for (const element of document.querySelectorAll("[data-icon]")) element.innerHTML = icon(element.dataset.icon);
  window.addEventListener("pageshow", event => { if (event.persisted) location.reload(); });

  (async () => {
    try {
      const session = await account();
      if (!session) { location.replace(signInUrl()); return; }
      user = session.user;
      roots = await request("/api/files/roots");
      configureFolders();
      renderRoute();
      window.addEventListener("hashchange", renderRoute);
      showFavorites();
      showActivity(byId("recentFiles"), "recent", 4);
      showActivity(byId("recentUploads"), "uploads", 4);
    } catch (error) { status.textContent = error.message; }
  })();
})();
