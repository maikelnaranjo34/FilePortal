(() => {
  "use strict";
  const mascot = "/filenest/mascot.png";
  const shapes = {
    home: '<path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1z"/>',
    folder: '<path d="M3 5h6l2 3h9a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z"/>',
    gallery: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8" cy="9" r="1.5"/><path d="m4 17 5-4 3 2 4-5 5 5"/>',
    video: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m10 9 5 3-5 3z"/>',
    more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
    share: '<circle cx="7" cy="12" r="3"/><circle cx="18" cy="5" r="3"/><circle cx="18" cy="19" r="3"/><path d="m9.5 10.5 6-4m-6 7 6 4"/>',
    star: '<path d="m12 3 2.8 5.8 6.4.9-4.6 4.5 1.1 6.3-5.7-3-5.7 3 1.1-6.3-4.6-4.5 6.4-.9z"/>',
    trash: '<path d="M3 6h18M9 6V3h6v3m4 0-1 15H6L5 6m5 4v7m4-7v7"/>',
    gear: '<circle cx="12" cy="12" r="4"/><path d="M12 2v4m0 12v4M2 12h4m12 0h4M5 5l3 3m8 8 3 3M5 19l3-3m8-8 3-3"/>',
    bell: '<path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9m-8 12a2 2 0 0 0 4 0"/>',
    search: '<circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/>',
    upload: '<path d="M12 15V3m-5 5 5-5 5 5M4 15v5h16v-5"/>',
    file: '<path d="M6 3h8l4 4v14H6zM14 3v5h4M9 12h6m-6 4h6"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 6v7l4 2"/>',
    pin: '<path d="m15 3 6 6-4 1-3 3v4l-7-7h4l3-3zM7 17l-4 4"/>',
    storage: '<ellipse cx="12" cy="6" rx="8" ry="3"/><path d="M4 6v12c0 4 16 4 16 0V6M4 12c0 4 16 4 16 0"/>',
    sparkle: '<path d="m12 2 2.5 7.5L22 12l-7.5 2.5L12 22l-2.5-7.5L2 12l7.5-2.5z"/>',
    back: '<path d="M19 12H5m6-6-6 6 6 6"/>',
    arrow: '<path d="M5 12h14m-6-6 6 6-6 6"/>',
    chevron: '<path d="m9 5 7 7-7 7"/>'
  };

  function icon(name) {
    return '<svg class="fn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + shapes[name] + '</svg>';
  }

  const destinations = {
    home: ["/", "Home", "home"],
    files: ["/files.html", "My Files", "folder"],
    gallery: ["/gallery.html", "Gallery", "gallery"],
    shared: ["/#shared", "Shared", "share"],
    favorites: ["/gallery.html#favorites", "Favorites", "star"],
    trash: ["/#trash", "Trash", "trash"],
    settings: ["/users.html", "Settings", "gear"]
  };
  const header = document.querySelector("#fileNestHeader");
  const profile = header?.dataset.public === undefined;
  const page = header?.dataset.page;
  const link = key => {
    const [url, label, glyph] = destinations[key];
    return '<a data-destination="' + key + '" href="' + url + '">' + icon(glyph) + '<span>' + label + '</span></a>';
  };

  let accountRequest;
  function account() {
    return accountRequest ??= fetch("/api/auth/me", { credentials: "same-origin", cache: "no-store" }).then(response => {
      if (response.status === 401) return null;
      if (!response.ok) throw new Error("Could not load your account.");
      return response.json();
    }).catch(error => {
      accountRequest = null;
      throw error;
    });
  }

  function signInUrl() {
    const next = location.pathname + location.search + location.hash;
    return "/login.html?next=" + encodeURIComponent(next);
  }

  function goBack(fallback = "/") {
    if (history.length > 1) {
      history.back();
      return;
    }
    if (location.pathname + location.search + location.hash !== fallback)
      location.assign(fallback);
  }

  function activate(key) {
    if (!header) return;
    header.querySelectorAll("[data-destination]").forEach(item => {
      if (item.dataset.destination === key) item.setAttribute("aria-current", "page");
      else item.removeAttribute("aria-current");
    });
    header.querySelector(".fn-more")?.classList.toggle("fn-active", ["shared", "favorites", "trash", "settings"].includes(key));
  }

  if (header) {
    header.className = "fn-header";
    header.innerHTML = '<div class="fn-header-inner"><div class="fn-brand-group"><button class="fn-back" type="button" aria-label="Go back" title="Back">' + icon("back") + '</button>' +
      '<a class="fn-brand" href="/"><img src="' + mascot + '" alt="" width="44" height="44"><span><b>File</b>Nest</span></a></div>' +
      (profile ? '<nav class="fn-nav" aria-label="Main navigation">' + ["home", "files", "gallery"].map(link).join("") +
        '<span class="fn-desktop-links">' + ["shared", "favorites", "trash"].map(link).join("") + '</span>' +
        '<details class="fn-menu fn-more"><summary>' + icon("more") + 'More</summary><div class="fn-popover">' +
        ["shared", "favorites", "trash", "settings"].map(link).join("") + '</div></details></nav>' +
        '<div class="fn-account"><button class="fn-bell" type="button" disabled aria-label="Notifications unavailable" title="Notifications unavailable">' +
        icon("bell") + '</button><details class="fn-menu fn-profile"><summary aria-label="Account"><span id="fn-initial">•</span></summary>' +
        '<div class="fn-popover"><span id="fn-username"></span>' + link("settings") +
        '<button id="fn-theme" type="button">Switch theme</button><button id="fn-logout" type="button">Sign out</button><p id="fn-account-status" role="status"></p></div></details></div>' :
        '<button id="fn-public-theme" class="fn-button fn-public-theme" type="button">Switch theme</button>') + '</div>';
    header.querySelector(".fn-back").addEventListener("click", () => goBack("/"));
    header.querySelector("#fn-public-theme")?.addEventListener("click", () => window.toggleSiteTheme());

    if (profile) {
      const updateAccount = () => account().then(user => {
        if (!user) return;
        header.querySelector("#fn-initial").textContent = (user.user || "?").slice(0, 1).toUpperCase();
        header.querySelector("#fn-username").textContent = user.user;
      }).catch(() => {});
      updateAccount();
      header.querySelector("#fn-theme").addEventListener("click", () => window.toggleSiteTheme());
      header.querySelector("#fn-logout").addEventListener("click", async event => {
        event.currentTarget.disabled = true;
        try {
          const response = await fetch("/api/auth/logout", { method: "POST", credentials: "same-origin" });
          if (!response.ok && response.status !== 401) throw new Error("Sign-out failed.");
          location.replace("/login.html");
        } catch {
          header.querySelector("#fn-account-status").textContent = "Could not sign out. Try again.";
          header.querySelector("#fn-logout").disabled = false;
        }
      });
      const closeMenus = except => {
        for (const menu of header.querySelectorAll("details[open]"))
          if (menu !== except) menu.open = false;
      };
      header.addEventListener("toggle", event => {
        if (event.target.matches("details") && event.target.open) closeMenus(event.target);
      }, true);
      document.addEventListener("click", event => {
        closeMenus(event.target.closest(".fn-menu"));
        if (event.target.closest("[data-destination]")) closeMenus();
      });
      header.addEventListener("keydown", event => {
        const menu = event.target.closest("details[open]");
        if (event.key === "Escape" && menu) {
          event.preventDefault();
          menu.open = false;
          menu.querySelector("summary").focus();
        }
      });
      const reflectPage = () => activate(page === "gallery" && location.hash === "#favorites" ? "favorites" : page);
      window.addEventListener("hashchange", reflectPage);
      reflectPage();
    }
  }

  async function request(route, options) {
    const response = await fetch(route, { credentials: "same-origin", cache: "no-store", ...options });
    if (response.status === 401) {
      location.replace(signInUrl());
      throw new Error("Sign in again to continue.");
    }
    if (!response.ok) {
      const payload = await response.json().catch(() => null);
      throw new Error(payload?.error || "Request failed (" + response.status + ").");
    }
    return response.status === 204 ? null : response.json();
  }

  function bytes(value) {
    if (!Number.isFinite(value)) return "";
    const power = value > 0 ? Math.min(4, Math.floor(Math.log(value) / Math.log(1024))) : 0;
    return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value / 1024 ** power) +
      " " + ["B", "KB", "MB", "GB", "TB"][power];
  }

  function activity(user, type, item) {
    const key = "filenest:" + encodeURIComponent(user) + ":" + type;
    try {
      const data = JSON.parse(localStorage.getItem(key) || "[]");
      if (!Array.isArray(data)) return [];
      if (!item) return data;
      const index = data.findIndex(entry => entry.path.toLowerCase() === item.path.toLowerCase());
      if (index !== -1) data.splice(index, 1);
      if (type !== "pins" || index === -1) data.unshift({ ...item, at: Date.now() });
      localStorage.setItem(key, JSON.stringify(data.slice(0, 15)));
      return data;
    } catch { return []; }
  }

  function fileLink(path, preview) {
    const query = new URLSearchParams({ path });
    if (preview) query.set("preview", preview);
    return "/files.html?" + query;
  }

  window.FileNest = { account, activate, activity, bytes, fileLink, goBack, icon, request, signInUrl };
})();
