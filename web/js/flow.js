// Audio Flow view — live signal-path diagram (inputs → channels → mixes → outputs).
// Click any element to trace its signal path; everything else dims.
import { store, MIXES } from './state.js';
import { engine } from './engine.js';
import { icon, tileColors } from './icons.js';

const WIRE = { in: '#2ec0d1', mix: '#c43cd4', out: '#12b981' };
const COLS = [
  { key: 'inputs',   label: 'Inputs',   icon: 'mic' },
  { key: 'channels', label: 'Channels', icon: 'speaker' },
  { key: 'mixes',    label: 'Mixes',    icon: 'sliders' },
  { key: 'outputs',  label: 'Outputs',  icon: 'speakerbox' },
];

let root, svg, grid, clearBtn, raf = 0, pendingDraw = 0;
let dots = [];            // {el, path, len, phase, speed, levelFn}
let measured = [];        // edges with computed paths for redraw

function el(html) {
  const t = document.createElement('template');
  t.innerHTML = html.trim();
  return t.content.firstElementChild;
}

// Names can come from device labels / file names — always escape before templating.
export function esc(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// ---------- graph model ----------
function buildGraph() {
  const s = store.state;
  const nodes = new Map();  // key -> node
  const edges = [];         // {from,to,stage,flowing,muted,inactive}

  const add = (key, n) => { nodes.set(key, { key, ...n }); };

  for (const i of s.inputs) {
    const inactive = s.live[i.id] === 'ended' || s.live[i.id] === 'error';
    add(`input:${i.id}`, { col: 0, type: 'input', id: i.id, name: i.name, sub: i.kind, icon: i.icon, tint: i.kind === 'Software', inactive });
    add(`chan:${i.id}`, { col: 1, type: 'chan', id: i.id, name: i.name, icon: i.icon, tint: i.kind === 'Software', inactive, volume: i.volume });
    const chanMuted = i.volume === 0;
    edges.push({ from: `input:${i.id}`, to: `chan:${i.id}`, stage: 'in', muted: chanMuted, inactive, flowing: !chanMuted && !inactive });
    for (const m of MIXES) {
      const send = i.sends[m.id];
      const muted = send.muted || send.level === 0;
      edges.push({ from: `chan:${i.id}`, to: `mix:${m.id}`, stage: 'mix', muted, inactive, flowing: !muted && !chanMuted && !inactive, send });
    }
  }
  for (const m of MIXES) {
    const conf = s.mixes[m.id];
    add(`mix:${m.id}`, { col: 2, type: 'mix', id: m.id, name: m.name, icon: m.icon, muted: conf.muted, volume: conf.volume });
  }
  for (const o of s.outputs) {
    const virtual = !o.deviceId;
    // Virtual outputs stay full-brightness (the reference default state dims nothing);
    // the "· no device" subtitle carries the information instead.
    add(`out:${o.id}`, { col: 3, type: 'out', id: o.id, name: o.name, icon: o.icon, muted: o.muted, volume: o.volume, virtual });
    const mixMuted = s.mixes[o.mixId]?.muted;
    const muted = o.muted || mixMuted;
    edges.push({ from: `mix:${o.mixId}`, to: `out:${o.id}`, stage: 'out', muted, flowing: !muted });
  }
  return { nodes, edges };
}

function selectionKey(sel) {
  if (!sel) return null;
  return `${sel.type}:${sel.id}`;
}

// Trace reachable nodes/edges through flowing edges from the selected node (both directions).
function tracePath(graph, selKey) {
  const litNodes = new Set([selKey]);
  const litEdges = new Set();
  const out = new Map(), inc = new Map();
  graph.edges.forEach((e, idx) => {
    (out.get(e.from) || out.set(e.from, []).get(e.from)).push({ e, idx });
    (inc.get(e.to) || inc.set(e.to, []).get(e.to)).push({ e, idx });
  });
  const walk = (key, dir) => {
    const list = (dir === 'down' ? out : inc).get(key) || [];
    for (const { e, idx } of list) {
      if (!e.flowing || litEdges.has(idx)) continue;
      litEdges.add(idx);
      const next = dir === 'down' ? e.to : e.from;
      if (!litNodes.has(next)) { litNodes.add(next); walk(next, dir); }
    }
  };
  walk(selKey, 'down');
  walk(selKey, 'up');
  // Muted edges touching the selected node stay visible (dashed) so the block is obvious.
  // An input and its channel are the same strip, so treat them as one node here —
  // clicking "Music" in either column should reveal its muted VOD send.
  const keys = new Set([selKey]);
  const [selType, selId] = [selKey.split(':')[0], selKey.split(':').slice(1).join(':')];
  if (selType === 'input') keys.add(`chan:${selId}`);
  if (selType === 'chan') keys.add(`input:${selId}`);
  const dashedIncident = new Set();
  graph.edges.forEach((e, idx) => {
    if ((keys.has(e.from) || keys.has(e.to)) && e.muted) dashedIncident.add(idx);
  });
  return { litNodes, litEdges, dashedIncident };
}

// ---------- rendering ----------
function nodeCard(n, state, mixCtx) {
  const s = store.state;
  let sub = '';
  if (n.type === 'input') sub = n.inactive ? 'No signal' : n.sub;
  if (n.type === 'chan') {
    sub = `${Math.round(n.volume * 100)}%`;
    if (mixCtx && state.lit) {
      const input = s.inputs.find(i => i.id === n.id);
      const send = input?.sends[mixCtx];
      if (send) sub += ` · in mix ${Math.round(send.level * 100)}%`;
    }
  }
  if (n.type === 'mix' || n.type === 'out') sub = `Vol ${Math.round(n.volume * 100)}%`;
  if (n.type === 'out' && n.virtual) sub += ' · no device';

  // Hardware channels show a waveform thumbnail (like the reference) instead of repeating the input glyph.
  const glyph = (n.type === 'chan' && !n.tint) ? 'waveform' : n.icon;
  const tint = n.tint ? `style="background:${tileColors[n.icon] || '#565a63'};color:#fff"` : '';
  const cls = ['fnode'];
  if (state.sel) cls.push('is-sel');
  if (state.dim) cls.push('is-dim');
  if (n.muted) cls.push('is-muted');
  return el(`
    <div class="${cls.join(' ')}" data-key="${esc(n.key)}" tabindex="0" role="button"
         aria-pressed="${state.sel}" aria-label="${esc(n.name)}, ${esc(sub || n.type)}${n.muted ? ', muted' : ''}">
      <div class="fnode-icon${n.tint ? ' is-tile' : ''}" ${tint}>${icon(glyph)}</div>
      <div class="fnode-text">
        <div class="fnode-title">${esc(n.name)}</div>
        <div class="fnode-sub">${esc(sub)}</div>
      </div>
    </div>`);
}

function render() {
  if (!root) return;
  cancelAnimationFrame(raf);
  dots = []; measured = [];

  const s = store.state;
  const graph = buildGraph();
  const selKey = selectionKey(s.selection);
  const trace = selKey && graph.nodes.has(selKey) ? tracePath(graph, selKey) : null;
  const mixCtx = trace && (s.selection.type === 'mix' ? s.selection.id
    : s.selection.type === 'out' ? store.output(s.selection.id)?.mixId : null);

  clearBtn.style.display = trace ? '' : 'none';

  // Capture focus BEFORE the wipe — removing the focused card resets activeElement to <body>.
  const hadFocus = document.activeElement?.closest?.('.flow-grid') ? document.activeElement.dataset.key : null;

  grid.innerHTML = '';
  const colEls = COLS.map((c, ci) => {
    if (ci > 0) grid.appendChild(el('<div class="flow-gut"></div>'));
    const col = el(`<div class="flow-col" data-col="${ci}">
      <div class="col-pill">${icon(c.icon)}<span>${c.label}</span></div>
      <div class="col-cards"></div>
    </div>`);
    grid.appendChild(col);
    return col.querySelector('.col-cards');
  });

  for (const n of graph.nodes.values()) {
    const lit = !trace || trace.litNodes.has(n.key);
    const card = nodeCard(n, { lit, dim: trace ? !lit : n.inactive, sel: selKey === n.key }, mixCtx);
    colEls[n.col].appendChild(card);
  }
  if (hadFocus) grid.querySelector(`[data-key="${CSS.escape(hadFocus)}"]`)?.focus({ preventScroll: true });

  // wires after layout settles — token-guarded so back-to-back renders can't interleave
  cancelAnimationFrame(pendingDraw);
  pendingDraw = requestAnimationFrame(() => {
    dots = []; measured = [];
    drawWires(graph, trace);
    if (trace) spawnDots(graph);
    cancelAnimationFrame(raf);
    tick();
  });
}

function anchor(key, side, canvasRect) {
  const card = grid.querySelector(`[data-key="${key}"]`);
  if (!card) return null;
  const r = card.getBoundingClientRect();
  return {
    x: (side === 'right' ? r.right : r.left) - canvasRect.left,
    y: r.top + r.height / 2 - canvasRect.top,
  };
}

function drawWires(graph, trace) {
  const canvasRect = root.querySelector('.flow-canvas').getBoundingClientRect();
  svg.setAttribute('viewBox', `0 0 ${canvasRect.width} ${canvasRect.height}`);
  svg.innerHTML = '';
  const gGray = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  const gMain = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  const gDots = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  svg.append(gGray, gMain, gDots);
  svg._gDots = gDots;

  graph.edges.forEach((e, idx) => {
    const a = anchor(e.from, 'right', canvasRect);
    const b = anchor(e.to, 'left', canvasRect);
    if (!a || !b) return;
    const dx = Math.max(30, (b.x - a.x) * (e.stage === 'in' ? 0.4 : 0.55));
    const d = `M ${a.x} ${a.y} C ${a.x + dx} ${a.y}, ${b.x - dx} ${b.y}, ${b.x} ${b.y}`;

    const lit = trace ? trace.litEdges.has(idx) : e.flowing;
    const dashed = trace ? trace.dashedIncident.has(idx) : e.muted && !e.inactive;
    const mk = (attrs) => {
      const p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      p.setAttribute('d', d);
      p.setAttribute('fill', 'none');
      for (const [k, v] of Object.entries(attrs)) p.setAttribute(k, v);
      return p;
    };

    if (lit) {
      const c = WIRE[e.stage];
      if (trace) gMain.appendChild(mk({ stroke: c, 'stroke-width': 7, opacity: 0.14, 'stroke-linecap': 'round' }));
      const main = mk({ stroke: c, 'stroke-width': trace ? 2.2 : 1.6, 'stroke-linecap': 'round' });
      gMain.appendChild(main);
      if (trace) {
        // Origin dot where the wire leaves its card, like the reference trace state.
        const o = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        o.setAttribute('cx', a.x); o.setAttribute('cy', a.y);
        o.setAttribute('r', 3.2); o.setAttribute('fill', c);
        gMain.appendChild(o);
        measured.push({ e, path: main });
      }
    } else if (dashed) {
      // Softer while tracing so the dashed block doesn't outshine the lit path.
      gGray.appendChild(mk({ stroke: trace ? '#55565a' : '#77787d', 'stroke-width': trace ? 1.3 : 1.6, 'stroke-dasharray': '5 5' }));
    } else {
      gGray.appendChild(mk({ stroke: '#3c3d40', 'stroke-width': 1, opacity: 0.9 }));
    }
  });
}

function spawnDots(graph) {
  const gDots = svg._gDots;
  const still = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  measured.forEach(({ e, path }, i) => {
    const len = path.getTotalLength();
    const c = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    c.setAttribute('r', 3.4);
    c.setAttribute('fill', WIRE[e.stage]);
    gDots.appendChild(c);
    const srcId = e.stage === 'out' ? null : (e.from.split(':')[1]);
    const mixId = e.stage === 'out' ? e.from.split(':')[1] : null;
    dots.push({
      el: c, path, len,
      phase: still ? 0.5 : (i * 0.37) % 1,           // reduced motion: dots hold position
      speed: still ? 0 : 1 / Math.max(1.1, len / 260),
      levelFn: e.stage === 'out'
        ? () => engine.mixLevel(mixId)
        : () => engine.channelLevel(srcId),
    });
  });
}

let last = 0;
function tick(ts) {
  raf = requestAnimationFrame(tick);
  if (ts === undefined) { last = performance.now(); return; }
  const dt = Math.min(0.05, Math.max(0, (ts - last) / 1000) || 0.016);
  last = ts;
  if (root.offsetParent === null) return;   // view hidden: keep loop idle
  for (const d of dots) {
    d.phase = (d.phase + dt * d.speed) % 1;
    const pt = d.path.getPointAtLength(d.phase * d.len);
    d.el.setAttribute('cx', pt.x);
    d.el.setAttribute('cy', pt.y);
    const lvl = engine.running ? d.levelFn() : 0.3;
    d.el.setAttribute('opacity', Math.min(1, 0.35 + lvl * 2.2).toFixed(2));
  }
}

// ---------- public ----------
export function initFlow(container) {
  root = container;
  root.innerHTML = `
    <div class="flow-head">
      <div>
        <h1>Audio flow</h1>
        <p class="flow-sub">Click any element to trace its signal path</p>
      </div>
      <button class="btn btn-clear" style="display:none">${icon('x')}<span>Clear selection</span></button>
    </div>
    <div class="flow-canvas">
      <svg class="flow-wires" aria-hidden="true"></svg>
      <div class="flow-grid"></div>
    </div>
    <div class="flow-legend">
      <span><i class="lg" style="background:${WIRE.in}"></i>Input to channel</span>
      <span><i class="lg" style="background:${WIRE.mix}"></i>Channel to mix</span>
      <span><i class="lg" style="background:${WIRE.out}"></i>Mix to output</span>
      <span><i class="lg lg-dash"></i>Muted</span>
      <span><i class="lg" style="background:#3c3d40"></i>Not in path</span>
    </div>`;
  svg = root.querySelector('.flow-wires');
  grid = root.querySelector('.flow-grid');
  clearBtn = root.querySelector('.btn-clear');

  clearBtn.addEventListener('click', () => store.clearSelection());
  root.querySelector('.flow-canvas').addEventListener('click', (ev) => {
    const card = ev.target.closest('.fnode');
    if (card) {
      const [type, ...rest] = card.dataset.key.split(':');
      store.select({ type, id: rest.join(':') });
    } else {
      store.clearSelection();
    }
  });
  // Escape lives on document: after a select re-render, focus may sit on <body>,
  // where a root-scoped listener would never hear the key.
  document.addEventListener('keydown', (ev) => {
    if (ev.key === 'Escape' && root.offsetParent !== null && !document.querySelector('dialog[open]')) {
      store.clearSelection();
    }
  });
  root.addEventListener('keydown', (ev) => {
    if ((ev.key === 'Enter' || ev.key === ' ') && ev.target.classList?.contains('fnode')) {
      ev.preventDefault();
      const [type, ...rest] = ev.target.dataset.key.split(':');
      store.select({ type, id: rest.join(':') });
    }
  });

  new ResizeObserver(() => { if (root.offsetParent !== null) render(); }).observe(root);
  store.on(() => { if (root.offsetParent !== null) render(); });
  render();
}

export function refreshFlow() { render(); }
export function stopFlow() { cancelAnimationFrame(raf); cancelAnimationFrame(pendingDraw); pendingDraw = 0; }
