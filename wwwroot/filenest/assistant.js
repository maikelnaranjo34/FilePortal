(() => {
  "use strict";

  const launcher = document.querySelector("#assistantLauncher");
  const panel = document.querySelector("#assistantPanel");
  if (!launcher || !panel) return;

  launcher.addEventListener("click", () => {
    panel.returnValue = "";
    panel.showModal();
  });
  panel.addEventListener("close", () => {
    if (panel.returnValue === "search") document.querySelector("#dashboardSearch").focus();
    else launcher.focus();
  });
  panel.querySelector("[data-assistant-search]").addEventListener("click", () => panel.close("search"));

  // A future provider belongs behind a same-origin, authenticated server endpoint.
  // It must resolve every requested file using the caller's current folder permissions.
})();
