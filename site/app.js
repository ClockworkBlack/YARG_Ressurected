// Fills the download buttons with the newest APKs from this repo's GitHub Releases.
// Every CI build is published as a pre-release, so /releases/latest (which skips
// pre-releases) can't be used: list recent releases and pick the newest of each kind.
(function () {
  "use strict";

  // On GitHub Pages the address is https://<owner>.github.io/<repo>/, so read the
  // repo from it. Anywhere else (a local preview) fall back to the main repo.
  var FALLBACK = "ClockworkBlack/YARG_Resurrected";
  function repoFromLocation() {
    var host = location.hostname.toLowerCase();
    var m = host.match(/^([a-z0-9-]+)\.github\.io$/);
    var first = location.pathname.split("/").filter(Boolean)[0];
    if (m && first) return m[1] + "/" + first;
    return FALLBACK;
  }
  var REPO = repoFromLocation();
  var RELEASES_PAGE = "https://github.com/" + REPO + "/releases";

  document.querySelectorAll("[data-repo-link]").forEach(function (a) {
    a.href = "https://github.com/" + REPO + (a.getAttribute("data-repo-link") || "");
  });

  var cards = document.querySelectorAll("[data-release]");
  if (!cards.length) return;

  function fmtSize(bytes) {
    return (bytes / (1024 * 1024)).toFixed(0) + " MB";
  }
  function fmtDate(iso) {
    try {
      return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
    } catch (e) {
      return iso.slice(0, 10);
    }
  }
  function fallback(card, msg) {
    var btn = card.querySelector(".btn.primary");
    btn.href = RELEASES_PAGE;
    btn.removeAttribute("aria-disabled");
    btn.textContent = "Open Releases";
    card.querySelector(".meta").textContent = msg;
  }

  fetch("https://api.github.com/repos/" + REPO + "/releases?per_page=30", {
    headers: { Accept: "application/vnd.github+json" }
  })
    .then(function (r) {
      if (!r.ok) throw new Error("HTTP " + r.status);
      return r.json();
    })
    .then(function (releases) {
      cards.forEach(function (card) {
        var prefix = card.getAttribute("data-release"); // tag prefix, e.g. "build-"
        var rel = releases.find(function (x) {
          return !x.draft && x.tag_name.indexOf(prefix) === 0 &&
            x.assets.some(function (a) { return /\.apk$/i.test(a.name); });
        });
        if (!rel) {
          fallback(card, "No build published yet.");
          return;
        }
        var apk = rel.assets.find(function (a) { return /\.apk$/i.test(a.name); });
        var btn = card.querySelector(".btn.primary");
        btn.href = apk.browser_download_url;
        btn.removeAttribute("aria-disabled");
        btn.textContent = "Download APK (" + fmtSize(apk.size) + ")";
        card.querySelector(".meta").textContent =
          rel.name + " · " + fmtDate(rel.published_at || rel.created_at);
        var notes = card.querySelector("[data-notes]");
        if (notes) notes.href = rel.html_url;

        var extra = card.querySelector("[data-extra]");
        if (extra) {
          var want = extra.getAttribute("data-extra");
          var asset = rel.assets.find(function (a) { return a.name === want; });
          if (asset) {
            extra.href = asset.browser_download_url;
            extra.hidden = false;
          }
        }
      });
    })
    .catch(function () {
      cards.forEach(function (card) {
        fallback(card, "Couldn't load the newest build here. The Releases page has every build.");
      });
    });
})();
