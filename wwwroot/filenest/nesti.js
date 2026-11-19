(() => {
  "use strict";
  const validStates = new Set(["idle", "listening", "thinking", "speaking", "moving", "success", "error"]);
  let state = "idle";

  function apply(nextState) {
    if (!validStates.has(nextState)) nextState = "idle";
    state = nextState;
    document.querySelectorAll("[data-nesti]").forEach(element => {
      for (const name of validStates) element.classList.toggle("nesti-" + name, name === state);
      element.dataset.nestiState = state;
    });
    document.dispatchEvent(new CustomEvent("nesti:changed", { detail: { state } }));
  }

  window.Nesti = Object.freeze({
    setState: apply,
    getState: () => state,
    states: Object.freeze([...validStates])
  });
  document.addEventListener("DOMContentLoaded", () => apply(state), { once: true });
})();
