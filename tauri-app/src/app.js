"use strict";

const { invoke } = window.__TAURI__.core;

const state = {
  programs: [],
  nav: "all",
  status: "all",
  query: "",
  view: "cards",
  iconSize: 80,
  theme: localStorage.getItem("wpi-theme") || "dark",
  selected: new Set(),
};

/* ── Elements ───────────────────────────────────────────── */
const $ = (id) => document.getElementById(id);
const navButtons = document.querySelectorAll(".nav-item[data-nav]");
const results = $("results");
const emptyBox = $("empty");
const loadingBox = $("loading");
const searchInput = $("search");
const resultsStyle = {};

const GRADIENTS = [
  ["#6366f1", "#8b5cf6"],
  ["#0ea5e9", "#6366f1"],
  ["#10b981", "#14b8a6"],
  ["#f59e0b", "#f97316"],
  ["#ec4899", "#8b5cf6"],
  ["#22d3ee", "#0ea5e9"],
  ["#84cc16", "#22c55e"],
  ["#ef4444", "#f59e0b"],
];

function paletteFor(name) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
  return GRADIENTS[h % GRADIENTS.length];
}

const iconCache = new Map();

async function hydrateIcons(scope) {
  const nodes = (scope || document).querySelectorAll("[data-icon]");
  const missing = [];
  for (const node of nodes) {
    const id = node.dataset.icon;
    if (id && !iconCache.has(id)) missing.push(id);
  }
  await Promise.all(
    missing.map((id) =>
      invoke("get_icon", { id })
        .then((b64) => iconCache.set(id, b64 || ""))
        .catch(() => iconCache.set(id, ""))
    )
  );
  for (const node of (scope || document).querySelectorAll("[data-icon]")) {
    const b64 = iconCache.get(node.dataset.icon);
    if (b64)
      node.innerHTML = `<img class="avatar-img" src="data:image/png;base64,${b64}" alt="" draggable="false" />`;
  }
}

const NAV_TITLES = {
  all: "All Programs",
  installed: "Installed",
  shortcuts: "Shortcuts",
  broken: "Broken",
  store: "Store Apps",
};

const NAV_STATUS = {
  all: "all",
  installed: "installed",
  shortcuts: "shortcut",
  broken: "broken",
  store: "store",
};

/* ── Filtering ─────────────────────────────────────────── */
function filtered() {
  const q = state.query.trim().toLowerCase();
  const status = state.status === "all" ? NAV_STATUS[state.nav] || "all" : state.status;
  let list = state.programs;
  if (status !== "all") list = list.filter((p) => p.status === status);
  if (q) list = list.filter((p) =>
    (p.name || "").toLowerCase().includes(q) ||
    (p.publisher || "").toLowerCase().includes(q) ||
    (p.install_location || "").toLowerCase().includes(q));
  return list;
}

function counts() {
  const c = { all: state.programs.length, installed: 0, shortcuts: 0, broken: 0, store: 0 };
  for (const p of state.programs) {
    if (p.status === "installed") c.installed++;
    else if (p.status === "shortcut") c.shortcuts++;
    else if (p.status === "broken") c.broken++;
    else if (p.status === "store") c.store++;
  }
  return c;
}

/* ── Rendering ─────────────────────────────────────────── */
function iconWrapHtml(p, g1, g2) {
  const letter = (p.name || "?")[0].toUpperCase();
  const avatar = p.has_icon
    ? `<div class="avatar avatar-ph" data-icon="${p.id}">${letter}</div>`
    : `<div class="avatar" style="background:linear-gradient(135deg,${g1},${g2})">${letter}</div>`;
  return `<div class="icon-wrap">
    ${avatar}
    <div class="icon-actions">
      <button class="mini-btn" data-act="info" title="Info" aria-label="Info">&#9432;</button>
      <button class="mini-btn" data-act="copy" title="Copy data" aria-label="Copy data">&#10697;</button>
    </div>
  </div>`;
}

function cardHtml(p, style) {
  const [g1, g2] = style;
  const runnable = pickExe(p);
  const wrap = iconWrapHtml(p, g1, g2);
  const status = p.status || "installed";
  const extra = status !== "installed"
    ? `<span class="badge ${status}">${status}</span>` : "";
  if (state.view === "list") {
    const isSel = state.selected.has(p.id);
    return `<div class="card ${isSel ? "selected" : ""}" data-id="${p.id}" data-path="${esc(runnable)}" tabindex="0">
      <label class="card-check" title="Select for custom report">
        <input type="checkbox" data-check="${p.id}" ${state.selected.has(p.id) ? "checked" : ""} />
        <span class="checkmark"></span>
      </label>
      ${wrap}
      <div class="name">${esc(p.name)}</div>
      <div class="publisher">${esc(p.publisher || "")}</div>
      <span class="version">${esc(p.version || "—")}</span>
      ${extra || `<span class="status-dot ${status}" title="${status}"></span>`}
    </div>`;
  }
  return `<div class="card" data-id="${p.id}" data-path="${esc(runnable)}" tabindex="0">
    <label class="card-check card-check-overlay" title="Select for custom report">
      <input type="checkbox" data-check="${p.id}" ${state.selected.has(p.id) ? "checked" : ""} />
      <span class="checkmark"></span>
    </label>
    ${wrap}
    <div class="name">${esc(p.name)}</div>
    <div class="publisher">${esc(p.publisher || "")}</div>
    <div class="foot">
      <span class="version">${esc(p.version || "—")}</span>
      ${extra || `<span class="status-dot ${status}" title="${status}"></span>`}
    </div>
  </div>`;
}

function render() {
  const list = filtered();
  const c = counts();
  for (const k of ["all", "installed", "shortcuts", "broken", "store"]) {
    const el = $("count-" + k);
    if (el) el.textContent = c[k];
  }
  $("count-text").textContent = `${list.length} program${list.length === 1 ? "" : "s"}` + (state.query && list.length ? ` · "${state.query}"` : "");
  $("page-title").textContent = NAV_TITLES[state.nav] || NAV_TITLES.all;

  const hasAny = state.programs.length > 0;
  $("sel-all-wrap")?.classList.toggle("hidden", !hasAny);

  emptyBox.classList.add("hidden");
  results.classList.remove("hidden");

  if (list.length === 0) {
    results.classList.add("hidden");
    if (state.programs.length === 0) {
      emptyBox.innerHTML =
        `<div class="empty-glyph">&#128269;</div>
         <div class="empty-title">Nothing here yet</div>
         <p class="empty-sub">Press <b>Scan Now</b> to discover the programs on this device.</p>`;
    } else {
      emptyBox.innerHTML =
        `<div class="empty-glyph">&#128269;</div>
         <div class="empty-title">No matches</div>
         <p class="empty-sub">Try a different search or filter.</p>`;
    }
    emptyBox.classList.remove("hidden");
    return;
  }
  results.innerHTML = list.map((p, i) => cardHtml(p, paletteFor(p.name))).join("");
  hydrateIcons(results);
  updateSelBar();
}

function esc(s) {
  return String(s).replace(/[&<>"']/g, (ch) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  }[ch]));
}

/* ── Copy to clipboard ────────────────────────────────── */
function programSummary(p) {
  return [
    ["Name", p.name],
    ["Publisher", p.publisher],
    ["Version", p.version],
    ["Status", p.status],
    ["Source", p.source],
    ["Architecture", p.architecture],
    ["Location", p.install_location],
    ["Executable", p.display_icon],
    ["Installed", p.install_date],
  ]
    .filter(([, v]) => v)
    .map(([k, v]) => `${k}: ${v}`)
    .join("\n");
}

async function copyText(text) {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch (_) { /* fall through to execCommand */ }
  }
  const ta = document.createElement("textarea");
  ta.value = text;
  ta.style.position = "fixed";
  ta.style.opacity = "0";
  document.body.appendChild(ta);
  ta.select();
  let ok = false;
  try { ok = document.execCommand("copy"); } catch (_) { /* ignore */ }
  document.body.removeChild(ta);
  return ok;
}

/* ── Detail panel ──────────────────────────────────────── */
function openPanel(id) {
  const p = state.programs.find((x) => x.id === id);
  if (!p) return;
  const [g1, g2] = paletteFor(p.name);
  const letter = (p.name || "?")[0].toUpperCase();
  const headAvatar = p.has_icon
    ? `<div class="avatar avatar-ph" data-icon="${p.id}">${letter}</div>`
    : `<div class="avatar" style="background:linear-gradient(135deg,${g1},${g2})">${letter}</div>`;
  const runnable = pickExe(p);
  const hint = runnable ? { path: runnable } : null;
  $("panel").innerHTML = `
    <div class="panel-head">
      ${headAvatar}
      <div>
        <h2>${esc(p.name)}</h2>
        <div class="pub">${esc(p.publisher || "Unknown publisher")} <span class="badge ${p.status}">${p.status}</span></div>
      </div>
    </div>
    <dl class="kv">
      <dt>Version</dt><dd>${esc(p.version || "N/A")}</dd>
      <dt>Source</dt><dd>${esc(p.source)}</dd>
      <dt>Arch</dt><dd>${esc(p.architecture || "N/A")}</dd>
      <dt>Location</dt><dd>${esc(p.install_location || "N/A")}</dd>
      <dt>Icon</dt><dd>${esc(p.display_icon || "N/A")}</dd>
      <dt>Installed</dt><dd>${esc(p.install_date || "N/A")}</dd>
    </dl>
    ${hint ? `<div class="launch-hint">Tip: double-click the program card to launch it.</div>` : ""}
    <div class="panel-actions">
      <button class="btn btn-ghost" id="copy-panel-btn">Copy data</button>
      <button class="btn btn-ghost" id="close-panel">Close</button>
    </div>`;

  $("overlay").classList.remove("hidden");
  hydrateIcons($("panel"));
  const cl = $("close-panel");
  if (cl) cl.onclick = closePanel;
  const cb = $("copy-panel-btn");
  if (cb) cb.onclick = async () => {
    const ok = await copyText(programSummary(p));
    toast(ok ? "Program data copied" : "Copy failed", !ok);
  };
  $("overlay").onclick = (e) => { if (e.target === $("overlay")) closePanel();
    return;
  };
}

function pickExe(p) {
  const icon = p.display_icon || "";
  const loc = p.install_location || "";
  let base = icon.trim();
  if (base.includes(",")) base = base.split(",")[0];
  if (/\.(ico|dll|png|jpe?g|bmp|gif|txt|chm|url|html?)$/i.test(base)) base = "";
  if (base) return base;
  if (loc) return loc;
  return "";
}

function closePanel() { $("overlay").classList.add("hidden"); }

/* ── Scan ──────────────────────────────────────────────── */
async function runScan() {
  const btn = $("scan-btn");
  btn.classList.add("scanning");
  $("scan-label").textContent = "Scanning…";
  loadingBox.classList.remove("hidden");
  results.classList.add("hidden");
  emptyBox.classList.add("hidden");
  let ok = true;
  try {
    const res = await invoke("scan");
    state.programs = res.programs || [];
    render();
    toast(`Scan complete · ${state.programs.length} programs found`);
  } catch (e) {
    ok = false;
    emptyBox.classList.remove("hidden");
    emptyBox.innerHTML =
      `<div class="empty-glyph">&#9888;&#65039;</div>
       <div class="empty-title">Scan failed</div>
       <p class="empty-sub">${esc(String(e))}</p>`;
  } finally {
    btn.classList.remove("scanning");
    $("scan-label").textContent = "Scan Now";
    loadingBox.classList.add("hidden");
  }
  return ok;
}

/* ── Sound + button feedback ─────────────────────────── */
let _audioCtx = null;
function playClick() {
  try {
    _audioCtx = _audioCtx || new (window.AudioContext || window.webkitAudioContext)();
    if (_audioCtx.state === "suspended") _audioCtx.resume();
    const t = _audioCtx.currentTime;
    const osc = _audioCtx.createOscillator();
    const gain = _audioCtx.createGain();
    osc.type = "sine";
    osc.frequency.setValueAtTime(720, t);
    osc.frequency.exponentialRampToValueAtTime(240, t + 0.06);
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(0.16, t + 0.012);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.09);
    osc.connect(gain);
    gain.connect(_audioCtx.destination);
    osc.start(t);
    osc.stop(t + 0.1);
  } catch (_) { /* audio unavailable */ }
}

function playLaunchSound() {
  try {
    _audioCtx = _audioCtx || new (window.AudioContext || window.webkitAudioContext)();
    if (_audioCtx.state === "suspended") _audioCtx.resume();
    const t = _audioCtx.currentTime;
    const notes = [523.25, 659.25, 783.99];
    notes.forEach((freq, i) => {
      const osc = _audioCtx.createOscillator();
      const gain = _audioCtx.createGain();
      osc.type = "sine";
      const start = t + i * 0.07;
      osc.frequency.setValueAtTime(freq, start);
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.14, start + 0.01);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.12);
      osc.connect(gain);
      gain.connect(_audioCtx.destination);
      osc.start(start);
      osc.stop(start + 0.13);
    });
  } catch (_) { /* audio unavailable */ }
}

function attachFx() {
  document.addEventListener(
    "click",
    (e) => {
      // No ripple on selection checkboxes, and none in list view — the user
      // finds the expanding "flash" distracting there.
      if (e.target.closest(".card-check") || (state.view === "list" && e.target.closest(".card"))) {
        return;
      }
      const hit = e.target.closest("button, .card, .status-chip");
      if (!hit) return;
      playClick();
      const rect = hit.getBoundingClientRect();
      const size = Math.max(rect.width, rect.height);
      const ripple = document.createElement("span");
      ripple.className = "ripple";
      ripple.style.width = ripple.style.height = size + "px";
      ripple.style.left = e.clientX - rect.left - size / 2 + "px";
      ripple.style.top = e.clientY - rect.top - size / 2 + "px";
      hit.appendChild(ripple);
      setTimeout(() => ripple.remove(), 600);
    },
    true
  );
}

/* ── Toast ─────────────────────────────────────────────── */
let toastTimer;
function toast(msg, error) {
  const t = $("toast");
  t.textContent = msg;
  t.style.background = error ? "var(--bad)" : "var(--surface)";
  t.style.color = error ? "#fff" : "var(--text)";
  t.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove("show"), 2200);
}

/* ── Theme ─────────────────────────────────────────────── */
function applyTheme(t) {
  document.documentElement.dataset.theme = t;
  localStorage.setItem("wpi-theme", t);
}
function toggleTheme() {
  state.theme = state.theme === "dark" ? "light" : "dark";
  applyTheme(state.theme);
}

/* ── Events ────────────────────────────────────────────── */
navButtons.forEach((b) =>
  b.addEventListener("click", () => {
    navButtons.forEach((x) => x.classList.remove("active"));
    b.classList.add("active");
    state.nav = b.dataset.nav;
    state.status = "all";
    document.querySelectorAll(".status-chip").forEach((c) => c.classList.toggle("active", c.dataset.status === "all"));
    render();
  }));

document.querySelectorAll(".seg-btn").forEach((b) =>
  b.addEventListener("click", () => {
    document.querySelectorAll(".seg-btn").forEach((x) => x.classList.remove("active"));
    b.classList.add("active");
    state.view = b.dataset.view;
    results.classList.toggle("view-cards", state.view === "cards");
    results.classList.toggle("view-list", state.view === "list");
    results.style.setProperty("--icon-size", state.iconSize + "px");
    render();
  }));

$("icon-size").addEventListener("input", (e) => {
  state.iconSize = +e.target.value;
  $("size-label").textContent = state.iconSize;
  results.style.setProperty("--icon-size", state.iconSize + "px");
});

$("theme-btn").addEventListener("click", toggleTheme);

$("scan-btn").addEventListener("click", runScan);

$("report-btn").addEventListener("click", () => exportKind("report-btn", "export_report", "Report saved"));

async function exportKind(btnId, cmd, okMsg, payload) {
  const btn = $(btnId);
  if (btn) btn.classList.add("busy");
  try {
    const path = await invoke(cmd, payload || {});
    toast(`${okMsg}: ${path}`);
  } catch (e) {
    toast(`${okMsg} failed: ${e}`, true);
  } finally {
    if (btn) btn.classList.remove("busy");
  }
}

/* ── Select All ──────────────────────────────────────── */
function syncSelectAll() {
  const cb = $("sel-all");
  if (!cb) return;
  const vis = filtered();
  if (vis.length === 0) {
    cb.checked = false;
    cb.indeterminate = false;
    return;
  }
  const allSel = vis.every((p) => state.selected.has(p.id));
  const someSel = vis.some((p) => state.selected.has(p.id));
  cb.checked = allSel;
  cb.indeterminate = someSel && !allSel;
}

$("sel-all")?.addEventListener("change", () => {
  const checked = $("sel-all").checked;
  const vis = filtered();
  for (const p of vis) {
    if (checked) state.selected.add(p.id);
    else state.selected.delete(p.id);
  }
  render();
});

function updateSelBar() {
  const n = state.selected.size;
  const bar = $("sel-bar");
  if (!bar) return;
  bar.classList.toggle("hidden", n === 0);
  $("sel-count").textContent = `${n} selected`;
  syncSelectAll();
}

$("sel-clear")?.addEventListener("click", () => {
  state.selected.clear();
  render();
  updateSelBar();
});

function selectedPrograms() {
  return state.programs.filter((p) => state.selected.has(p.id));
}

function exportCustom(kind) {
  const sel = selectedPrograms();
  if (!sel.length) {
    toast("Select at least one program", true);
    return;
  }
  const map = {
    html: ["sel-report", "export_custom_report", "Custom HTML report saved"],
    md: ["sel-report-md", "export_custom_report_md", "Custom Markdown saved"],
    pdf: ["sel-report-pdf", "export_custom_report_pdf", "Custom PDF opened"],
  };
  const [btnId, cmd, okMsg] = map[kind];
  exportKind(btnId, cmd, okMsg, { programs: sel });
}

$("sel-report")?.addEventListener("click", () => exportCustom("html"));
$("sel-report-md")?.addEventListener("click", () => exportCustom("md"));
$("sel-report-pdf")?.addEventListener("click", () => exportCustom("pdf"));

$("report-md-btn").addEventListener("click", () => exportKind("report-md-btn", "export_report_md", "Markdown saved"));
$("report-pdf-btn").addEventListener("click", () => exportKind("report-pdf-btn", "export_report_pdf", "Print/PDF opened"));

searchInput.addEventListener("input", () => {
  state.query = searchInput.value;
  render();
});

document.addEventListener("keydown", (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    searchInput.focus();
  }
  if (e.key === "Escape") closePanel();
});

let _clickTimer = null;
let _pendingCardId = null;

results.addEventListener("click", (e) => {
  const check = e.target.closest(".card-check");
  if (check) {
    e.stopPropagation();
    const box = check.querySelector("input");
    const id = box.dataset.check;
    if (box.checked) state.selected.add(id);
    else state.selected.delete(id);
    check.closest(".card").classList.toggle("selected", box.checked);
    updateSelBar();
    return;
  }
  const card = e.target.closest(".card");
  if (!card) return;
  const btn = e.target.closest(".mini-btn");
  if (btn) {
    e.stopPropagation();
    const p = state.programs.find((x) => x.id === card.dataset.id);
    if (!p) return;
    if (btn.dataset.act === "copy") {
      copyText(programSummary(p)).then((ok) =>
        toast(ok ? "Program data copied" : "Copy failed", !ok)
      );
    } else {
      openPanel(card.dataset.id);
    }
    return;
  }
  if (_clickTimer) {
    clearTimeout(_clickTimer);
    _clickTimer = null;
    _pendingCardId = null;
  }
  _pendingCardId = card.dataset.id;
  _clickTimer = setTimeout(() => {
    if (_pendingCardId) openPanel(_pendingCardId);
    _clickTimer = null;
    _pendingCardId = null;
  }, 260);
});

results.addEventListener("dblclick", async (e) => {
  const card = e.target.closest(".card");
  if (!card) return;
  if (_clickTimer) {
    clearTimeout(_clickTimer);
    _clickTimer = null;
    _pendingCardId = null;
  }
  const p = state.programs.find((x) => x.id === card.dataset.id);
  const path = card.dataset.path;
  if (!path) {
    toast(`No runnable file for "${p ? p.name : ""}"`, true);
    return;
  }
  try {
    await invoke("launch_program", { exePath: path });
    playLaunchSound();
    toast(`Launched ${p ? p.name : ""}`);
  } catch (err) {
    toast(`Cannot launch: ${err}`, true);
  }
});

results.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && e.target.classList.contains("card")) openPanel(e.target.dataset.id);
});

/* ── Init ──────────────────────────────────────────────── */
applyTheme(state.theme);
attachFx();

const splash = $("splash");
const splashBar = $("splash-bar");
const splashStage = $("splash-stage");
const splashPct = $("splash-pct");
const splashSub = $("splash-sub");
const splashSkip = $("splash-skip");
const SPLASH_STAGES = [
  [8, "Warming up"],
  [22, "Reading the installed registry…"],
  [40, "Collecting uninstall entries…"],
  [58, "Scanning your Start Menu…"],
  [76, "Enumerating Store apps…"],
  [92, "Preparing the inventory…"],
];

function spawnParticles() {
  const host = $("splash-particles");
  if (!host || host._spawned) return;
  host._spawned = true;
  for (let i = 0; i < 26; i++) {
    const p = document.createElement("i");
    p.className = "splash-particle";
    const size = 3 + Math.random() * 5;
    p.style.width = p.style.height = size + "px";
    p.style.left = Math.random() * 100 + "%";
    p.style.setProperty("--drift", (Math.random() * 120 - 60).toFixed(1) + "px");
    p.style.animationDuration = 6 + Math.random() * 8 + "s";
    p.style.animationDelay = Math.random() * 10 + "s";
    host.appendChild(p);
  }
}

function splashDone() {
  splashBar.style.width = "100%";
  if (splashPct) splashPct.textContent = "100%";
  splashStage.textContent = "All set!";
  setTimeout(() => {
    splash.classList.add("gone");
    splashSkip.hidden = true;
  }, 550);
}

function danceSplash() {
  let i = 0;
  const tick = () => {
    if (splash.classList.contains("gone")) return;
    if (i < SPLASH_STAGES.length) {
      const [pct, msg] = SPLASH_STAGES[i];
      splashBar.style.width = pct + "%";
      if (splashPct) splashPct.textContent = pct + "%";
      splashStage.textContent = msg;
      i++;
    }
  };
  tick();
  return setInterval(tick, 850);
}

async function boot() {
  // Reveal the window only after the page has painted its first frame, so the
  // splash is already on screen — this eliminates any blank-window flash.
  await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
  try {
    window.__TAURI__.window.getCurrentWindow().show().catch(() => {});
  } catch (_) {}

  const bootStart = Date.now();
  const MIN_SPLASH_MS = 2400; // keep the welcome visible even if the scan is instant
  const timer = danceSplash();
  spawnParticles();
  const btn = $("scan-btn");
  btn.classList.add("scanning");
  $("scan-label").textContent = "Scanning…";
  results.classList.add("hidden");
  emptyBox.classList.add("hidden");
  try {
    const res = await invoke("scan");
    state.programs = res.programs || [];
    render();
  } catch (e) {
    splashStage.textContent = "Scan failed — opening anyway";
    splashSkip.hidden = false;
    splashSkip.onclick = () => {
      splash.classList.add("gone");
      splashSkip.hidden = true;
      emptyBox.classList.remove("hidden");
      emptyBox.innerHTML =
        `<div class="empty-glyph">&#9888;&#65039;</div>
         <div class="empty-title">Scan failed</div>
         <p class="empty-sub">${esc(String(e))}</p>`;
    };
  } finally {
    clearInterval(timer);
    const elapsed = Date.now() - bootStart;
    if (elapsed < MIN_SPLASH_MS) await new Promise((r) => setTimeout(r, MIN_SPLASH_MS - elapsed));
    btn.classList.remove("scanning");
    $("scan-label").textContent = "Scan Now";
    splashDone();
  }
}

window.addEventListener("DOMContentLoaded", boot);