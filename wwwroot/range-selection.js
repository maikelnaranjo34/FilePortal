(function () {
  "use strict";

  window.RangeSelection = class {
    active = false;
    first = null;
    anchor = null;

    toggle() {
      this.active = !this.active;
      this.first = null;
    }

    reset() {
      this.active = false;
      this.first = null;
      this.anchor = null;
    }

    get hint() {
      if (!this.active) return "";
      return this.first === null ? "Tap the first item." : "Tap the last item.";
    }

    select(items, item, extend) {
      if (this.active && this.first === null) {
        this.first = item;
        this.anchor = item;
        return [item];
      }

      const start = this.active ? this.first : extend ? this.anchor : null;
      this.anchor = item;
      if (start === null) return null;
      this.active = false;
      this.first = null;

      const first = items.indexOf(start);
      const last = items.indexOf(item);
      return first < 0 || last < 0 ? [item] : items.slice(Math.min(first, last), Math.max(first, last) + 1);
    }
  };
})();
