// Central app state + tiny event bus. Persisted to localStorage.

export const MIXES = [
  { id: 'personal', name: 'Personal Mix', icon: 'headphones' },
  { id: 'stream',   name: 'Stream Mix',   icon: 'broadcast' },
  { id: 'chat',     name: 'Chat Mix',     icon: 'people' },
  { id: 'vod',      name: 'VOD Track',    icon: 'vod' },
];

const LS_KEY = 'soundmaster.v1';

function sends(over = {}) {
  const base = {};
  for (const m of MIXES) base[m.id] = { level: 1, muted: false };
  for (const [k, v] of Object.entries(over)) base[k] = { ...base[k], ...v };
  return base;
}

// Demo roster mirrors the reference layout: hardware inputs up top, software below.
export function demoInputs() {
  return [
    { id: 'in-line',    name: 'Line In',     kind: 'Hardware', icon: 'line',    srcType: 'demo:bass',    volume: 1,    sends: sends() },
    { id: 'in-usbaux',  name: 'USB Aux',     kind: 'Hardware', icon: 'line',    srcType: 'demo:pad',     volume: 1,    sends: sends() },
    { id: 'in-xlr1',    name: 'XLR Mic 1',   kind: 'Hardware', icon: 'mic',     srcType: 'demo:voice',   volume: 1,    sends: sends() },
    { id: 'in-capture', name: 'Capture',     kind: 'Hardware', icon: 'capture', srcType: 'demo:arp',     volume: 1,    sends: sends() },
    { id: 'in-browser', name: 'Browser',     kind: 'Software', icon: 'browser', srcType: 'demo:crackle', volume: 1,    sends: sends({ vod: { level: 0.74 } }) },
    { id: 'in-chat',    name: 'Chat',        kind: 'Software', icon: 'chat',    srcType: 'demo:blip',    volume: 1,    sends: sends({ vod: { level: 0.75 } }) },
    { id: 'in-deck',    name: 'Deck',        kind: 'Software', icon: 'deck',    srcType: 'demo:tick',    volume: 1,    sends: sends() },
    // Music stays out of the VOD Track by default — the classic "keep copyrighted music off the VOD" routing.
    { id: 'in-music',   name: 'Music',       kind: 'Software', icon: 'music',   srcType: 'demo:music',   volume: 1,    sends: sends({ vod: { muted: true }, chat: { level: 0.5 } }) },
  ];
}

export function demoOutputs() {
  return [
    { id: 'out-hp',   name: 'Headphone 1', icon: 'speaker',    deviceId: 'default', mixId: 'personal', volume: 0.8, muted: false },
    { id: 'out-line', name: 'Line Out',    icon: 'speaker',    deviceId: null,      mixId: 'stream',   volume: 1,   muted: false },
    { id: 'out-usb',  name: 'USB Out',     icon: 'speakerbox', deviceId: null,      mixId: 'stream',   volume: 1,   muted: false },
  ];
}

function defaults() {
  return {
    demo: true,
    view: 'flow',
    inputs: demoInputs(),
    mixes: Object.fromEntries(MIXES.map(m => [m.id, { volume: 1, muted: false }])),
    outputs: demoOutputs(),
  };
}

// Saved state may come from an older schema (or be hand-edited). Rebuild every entity
// against the current shape so a stale save can never crash render or the engine.
function normalize(saved) {
  const d = defaults();
  const s = { ...d, ...saved };
  const fixInput = i => ({ volume: 1, kind: 'Software', icon: 'tone', name: 'Input', ...i, sends: sends(i.sends || {}) });
  s.inputs = (Array.isArray(s.inputs) ? s.inputs : d.inputs)
    .filter(i => i && i.id && i.srcType && i.srcType !== 'file')   // blob URLs die on reload
    .map(fixInput);
  s.mixes = Object.fromEntries(MIXES.map(m => [m.id, { volume: 1, muted: false, ...(s.mixes || {})[m.id] }]));
  s.outputs = (Array.isArray(s.outputs) ? s.outputs : d.outputs)
    .filter(o => o && o.id)
    .map(o => ({
      volume: 1, muted: false, icon: 'speaker', name: 'Output', deviceId: null, ...o,
      mixId: MIXES.some(m => m.id === o.mixId) ? o.mixId : MIXES[0].id,
    }));
  s.demoStash = Array.isArray(s.demoStash) ? s.demoStash.map(fixInput) : null;
  return s;
}

class Store {
  constructor() {
    this.listeners = new Set();
    let saved = null;
    try { saved = JSON.parse(localStorage.getItem(LS_KEY) || 'null'); } catch { /* fresh start */ }
    this.s = saved && saved.inputs ? normalize(saved) : defaults();
    this.s.selection = null;         // never persisted
    this.s.live = {};                // runtime flags per input id (active/ended/error)
  }
  get state() { return this.s; }
  on(fn) { this.listeners.add(fn); return () => this.listeners.delete(fn); }
  emit(what = 'change') {
    for (const fn of this.listeners) fn(what, this.s);
    this.save();
  }
  save() {
    const { selection, live, ...persist } = this.s;
    persist.inputs = persist.inputs.filter(i => i.srcType !== 'file');  // objectUrl can't survive reload
    try { localStorage.setItem(LS_KEY, JSON.stringify(persist)); } catch { /* private mode */ }
  }
  pruneSelection() {
    const sel = this.s.selection;
    if (!sel) return;
    const ok = sel.type === 'mix' ? MIXES.some(m => m.id === sel.id)
      : (this.input(sel.id) || this.output(sel.id));
    if (!ok) this.s.selection = null;
  }
  input(id) { return this.s.inputs.find(i => i.id === id); }
  output(id) { return this.s.outputs.find(o => o.id === id); }

  select(sel) { this.s.selection = sel; this.emit('selection'); }
  clearSelection() { if (this.s.selection) { this.s.selection = null; this.emit('selection'); } }
  setView(v) { this.s.view = v; this.emit('view'); }

  setChannelVolume(id, v) { const i = this.input(id); if (i) { i.volume = v; this.emit('levels'); } }
  setSend(id, mixId, level) { const i = this.input(id); if (i) { i.sends[mixId].level = level; this.emit('levels'); } }
  setSendMuted(id, mixId, muted) { const i = this.input(id); if (i) { i.sends[mixId].muted = muted; this.emit('routing'); } }
  setMixVolume(mixId, v) { this.s.mixes[mixId].volume = v; this.emit('levels'); }
  setMixMuted(mixId, m) { this.s.mixes[mixId].muted = m; this.emit('routing'); }
  setOutput(id, patch) { const o = this.output(id); if (o) { Object.assign(o, patch); this.emit('routing'); } }

  addInput(input) { this.s.inputs.push(input); this.emit('structure'); }
  removeInput(id) { this.s.inputs = this.s.inputs.filter(i => i.id !== id); this.pruneSelection(); this.emit('structure'); }
  addOutput(output) { this.s.outputs.push(output); this.emit('structure'); }
  removeOutput(id) { this.s.outputs = this.s.outputs.filter(o => o.id !== id); this.pruneSelection(); this.emit('structure'); }

  setDemo(onOff) {
    this.s.demo = onOff;
    if (onOff) {
      // Restore the user's tweaked demo channels if we stashed them, else fresh defaults.
      const restore = (this.s.demoStash && this.s.demoStash.length) ? this.s.demoStash : demoInputs();
      const have = new Set(this.s.inputs.map(i => i.id));
      this.s.inputs = [...restore.filter(d => !have.has(d.id)), ...this.s.inputs];
      this.s.demoStash = null;
    } else {
      this.s.demoStash = this.s.inputs.filter(i => i.srcType.startsWith('demo:'));
      this.s.inputs = this.s.inputs.filter(i => !i.srcType.startsWith('demo:'));
    }
    this.pruneSelection();
    this.emit('structure');
  }
  reset() {
    try { localStorage.removeItem(LS_KEY); } catch { /* ignore */ }
    this.s = { ...defaults(), selection: null, live: {} };
    this.emit('structure');
  }
}

export const store = new Store();
