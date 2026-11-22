(function () {
  "use strict";

  function apply(media, angle) {
    media.dataset.rotation = angle;
    media.style.rotate = angle + "deg";
    media.parentElement.classList.toggle("quarter-turn", Math.abs(angle % 180) === 90);
  }

  window.previewRotation = {
    turn(media, amount) {
      if (media) apply(media, (Number(media.dataset.rotation || 0) + amount) % 360);
    },
    reset(media) {
      if (media) apply(media, 0);
    }
  };
})();
