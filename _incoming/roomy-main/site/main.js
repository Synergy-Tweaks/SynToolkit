(() => {
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;

  // ---------- video ----------
  const video = document.getElementById("demoVideo");
  const toggle = document.getElementById("videoToggle");
  if (video && toggle) {
    if (reduce) { video.removeAttribute("autoplay"); video.pause(); }
    const sync = () => {
      toggle.classList.toggle("paused", video.paused);
      toggle.setAttribute("aria-label", video.paused ? "Play video" : "Pause video");
    };
    toggle.addEventListener("click", () => (video.paused ? video.play() : video.pause()));
    video.addEventListener("play", sync);
    video.addEventListener("pause", sync);
    sync();
  }

  // ---------- gentle fade-in ----------
  const targets = document.querySelectorAll(".head, .try, .card, .split, .checks li, details, .end");
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
  }, { threshold: 0.12 });
  targets.forEach((el) => { el.classList.add("fade"); io.observe(el); });

  // ---------- try it ----------
  const TOTAL = 237.9;
  const START_FREE = 9.6;
  const $ = (id) => document.getElementById(id);
  const boxes = [...document.querySelectorAll("#tList input")];
  const bar = $("tBar"), status = $("tStatus"), freeEl = $("tFree"), usedEl = $("tUsed"), pctEl = $("tPct");
  const selEl = $("tSel"), cleanBtn = $("tClean"), resetBtn = $("tReset"), doneEl = $("tDone"), freedEl = $("tFreed");
  let free = START_FREE;

  const gb = (n) => `${n.toFixed(1)} GB`;
  const selected = () => boxes.filter((b) => b.checked && !b.disabled).reduce((s, b) => s + parseFloat(b.dataset.gb), 0);

  function paintDrive(f) {
    const used = TOTAL - f;
    const pct = used / TOTAL;
    bar.style.width = `${(pct * 100).toFixed(1)}%`;
    const tone = pct >= 0.9 ? "" : pct >= 0.75 ? "amber" : "green";
    bar.className = tone;
    status.className = `status ${tone}`;
    status.textContent = pct >= 0.9 ? "Almost full" : pct >= 0.75 ? "Getting full" : "Looking good";
    usedEl.textContent = `${Math.round(used)} GB used`;
    pctEl.textContent = `${Math.round(pct * 100)}% full`;
  }

  function paintSelection() {
    const s = selected();
    selEl.textContent = gb(s);
    cleanBtn.textContent = s > 0 ? `Clean up ${gb(s)}` : "Pick something";
    cleanBtn.disabled = s <= 0;
  }

  function countTo(el, from, to, ms) {
    if (reduce) { el.textContent = gb(to); return; }
    const t0 = performance.now();
    const step = (t) => {
      const p = Math.min(1, (t - t0) / ms);
      el.textContent = gb(from + (to - from) * (1 - Math.pow(1 - p, 3)));
      if (p < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  boxes.forEach((b) => b.addEventListener("change", paintSelection));

  cleanBtn.addEventListener("click", async () => {
    const s = selected();
    if (s <= 0) return;
    cleanBtn.disabled = true;
    cleanBtn.textContent = "Cleaning…";
    const chosen = boxes.filter((b) => b.checked && !b.disabled);
    for (const b of chosen) {
      await new Promise((r) => setTimeout(r, reduce ? 0 : 160));
      b.disabled = true;
      b.closest("li").classList.add("gone");
    }
    const before = free;
    free = Math.min(TOTAL, free + s);
    paintDrive(free);
    countTo(freeEl, before, free, 1400);
    freedEl.textContent = gb(free - START_FREE);
    doneEl.hidden = false;
    resetBtn.hidden = false;
    paintSelection();
  });

  resetBtn.addEventListener("click", () => {
    free = START_FREE;
    boxes.forEach((b, i) => {
      b.disabled = false;
      b.checked = i < 5;
      b.closest("li").classList.remove("gone");
    });
    freeEl.textContent = gb(free);
    paintDrive(free);
    paintSelection();
    doneEl.hidden = true;
    resetBtn.hidden = true;
  });

  paintDrive(free);
  paintSelection();
})();

// ---------- live GitHub star count ----------
(() => {
  const els = document.querySelectorAll(".star-count");
  if (!els.length) return;
  const KEY = "roomy-stars";
  const show = (n) => {
    if (typeof n !== "number" || n < 1) return;
    const text = n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
    els.forEach((el) => { el.textContent = text; el.hidden = false; });
  };
  try {
    const cached = JSON.parse(sessionStorage.getItem(KEY) || "null");
    if (cached && Date.now() - cached.t < 10 * 60 * 1000) return show(cached.n);
  } catch {}
  fetch("https://api.github.com/repos/krezio/roomy", { headers: { Accept: "application/vnd.github+json" } })
    .then((r) => (r.ok ? r.json() : null))
    .then((d) => {
      if (!d) return;
      show(d.stargazers_count);
      try { sessionStorage.setItem(KEY, JSON.stringify({ n: d.stargazers_count, t: Date.now() })); } catch {}
    })
    .catch(() => {});
})();
