/* ========================================================================
   Shared site-wide theme switcher (dark / light)
   Loaded by every page. Applies the saved theme immediately (before paint
   to avoid a flash) and injects a floating toggle button on every page.
   The choice is persisted in localStorage and shared across all pages.
   ===================================================================== */
(function () {
  "use strict";

  var STORAGE_KEY = "siteTheme";
  var DEFAULT_THEME = "dark";

  function getTheme() {
    try {
      var t = localStorage.getItem(STORAGE_KEY);
      return (t === "light" || t === "dark") ? t : DEFAULT_THEME;
    } catch (e) { return DEFAULT_THEME; }
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
    try { document.documentElement.style.colorScheme = theme; } catch (e) {}
  }

  // Apply ASAP (script is loaded in <head>, so this runs before body paints).
  applyTheme(getTheme());

  function setTheme(theme) {
    applyTheme(theme);
    try { localStorage.setItem(STORAGE_KEY, theme); } catch (e) {}
    updateButton(theme);
  }

  function toggle() {
    setTheme(getTheme() === "dark" ? "light" : "dark");
  }
  // Expose for optional manual hooks.
  window.toggleSiteTheme = toggle;
  window.setSiteTheme = setTheme;

  var btn;
  function updateButton(theme) {
    if (!btn) return;
    var dark = theme === "dark";
    btn.textContent = dark ? "☀️" : "🌙";
    btn.title = dark ? "Switch to light theme" : "Switch to dark theme";
    btn.setAttribute("aria-label", btn.title);
  }

  function injectButton() {
    if (document.querySelector("#fileNestHeader, .fn-login, #themeToggleBtn")) return;
    btn = document.createElement("button");
    btn.id = "themeToggleBtn";
    btn.type = "button";
    btn.addEventListener("click", toggle);

    var css = document.createElement("style");
    css.textContent =
      "#themeToggleBtn{position:fixed;left:18px;bottom:18px;z-index:40;width:44px;height:44px;" +
      "border-radius:50%;border:1px solid rgba(128,128,128,.35);cursor:pointer;font-size:19px;" +
      "line-height:1;display:flex;align-items:center;justify-content:center;" +
      "background:rgba(127,127,127,.18);backdrop-filter:blur(6px);-webkit-backdrop-filter:blur(6px);" +
      "color:inherit;box-shadow:0 4px 14px rgba(0,0,0,.25);transition:transform .15s,background .15s}" +
      "#themeToggleBtn:hover{transform:scale(1.08);background:rgba(127,127,127,.30)}";
    document.head.appendChild(css);

    document.body.appendChild(btn);
    updateButton(getTheme());
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", injectButton);
  } else {
    injectButton();
  }
})();
