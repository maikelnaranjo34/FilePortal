if (["#photos", "#videos", "#albums", "#favorites", "#search"].includes(location.hash)) {
  location.replace("/gallery.html" + location.search + location.hash);
}
