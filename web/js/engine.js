// Web Audio engine. Graph per input:
//   source -> chanGain(channel volume) -> chanAnalyser -> sendGain[mix] -> mixBus
// Per mix: mixBus -> mixGain(vol × mute) -> mixAnalyser -> (per output) outGain -> limiter -> dest -> <audio sinkId>
import { store, MIXES } from './state.js';
import { makeClock, demoGenerators } from './demo.js';

class Engine {
  constructor() {
    this.ctx = null;
    this.clock = null;
    this.chans = new Map();     // inputId -> {srcNode, stop, gain, analyser, sends: Map(mixId->gain), data}
    this.mixes = new Map();     // mixId -> {bus, gain, analyser, data}
    this.outs = new Map();      // outputId -> {gain, analyser, limiter, dest, el, sinkId, data}
    this.recorders = new Map(); // mixId -> {rec, chunks}
    this.pendingSources = new Map(); // inputId -> {node, stop} acquired before the input entered the store
    this.started = false;
  }

  get running() { return !!this.ctx && this.ctx.state === 'running'; }

  async start() {
    if (!this.ctx) {
      this.ctx = new (window.AudioContext || window.webkitAudioContext)({ latencyHint: 'interactive' });
      this.clock = makeClock(this.ctx);
      for (const m of MIXES) this.ensureMix(m.id);
      this.sync();
      this.clock.start();
    }
    if (this.ctx.state !== 'running') await this.ctx.resume().catch(() => {});
    this.started = this.ctx.state === 'running';
    return this.started;
  }

  ensureMix(mixId) {
    if (this.mixes.has(mixId)) return this.mixes.get(mixId);
    const bus = this.ctx.createGain();
    const gain = this.ctx.createGain();
    const analyser = this.ctx.createAnalyser();
    analyser.fftSize = 512; analyser.smoothingTimeConstant = 0.7;
    bus.connect(gain); gain.connect(analyser);
    const m = { bus, gain, analyser, data: new Uint8Array(analyser.frequencyBinCount) };
    this.mixes.set(mixId, m);
    return m;
  }

  async makeSource(input) {
    const ctx = this.ctx;
    if (input.srcType.startsWith('demo:')) {
      const g = ctx.createGain();
      const stop = demoGenerators[input.srcType]?.(ctx, g, this.clock) || (() => {});
      return { node: g, stop };
    }
    if (input.srcType === 'tone') {
      const o = ctx.createOscillator(); o.type = 'sine'; o.frequency.value = input.freq || 440;
      const g = ctx.createGain(); g.gain.value = 0.15; o.connect(g); o.start();
      return { node: g, stop: () => { try { o.stop(); } catch { /* noop */ } } };
    }
    if (input.srcType === 'mic') {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          deviceId: input.deviceId ? { exact: input.deviceId } : undefined,
          echoCancellation: false, noiseSuppression: false, autoGainControl: false,
        },
      });
      const node = ctx.createMediaStreamSource(stream);
      stream.getAudioTracks()[0]?.addEventListener('ended', () => this.markEnded(input.id));
      return { node, stop: () => stream.getTracks().forEach(t => t.stop()) };
    }
    if (input.srcType === 'system') {
      // Chrome/Edge: share a screen with "Also share system audio", or a tab with its audio.
      const stream = await navigator.mediaDevices.getDisplayMedia({
        video: true,
        audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
      });
      stream.getVideoTracks().forEach(t => t.stop());   // audio only
      if (!stream.getAudioTracks().length) { stream.getTracks().forEach(t => t.stop()); throw new Error('No audio shared — tick "Also share system audio" (or share a tab with audio).'); }
      const node = ctx.createMediaStreamSource(stream);
      stream.getAudioTracks()[0].addEventListener('ended', () => this.markEnded(input.id));
      return { node, stop: () => stream.getTracks().forEach(t => t.stop()) };
    }
    if (input.srcType === 'file') {
      const el = new Audio();
      el.src = input.objectUrl; el.loop = true; el.crossOrigin = 'anonymous';
      await el.play().catch(() => {});
      const node = ctx.createMediaElementSource(el);
      return { node, stop: () => { el.pause(); el.src = ''; } };
    }
    throw new Error('Unknown source ' + input.srcType);
  }

  markEnded(inputId) {
    store.state.live[inputId] = 'ended';
    store.emit('routing');
  }

  // Acquire a source BEFORE the input enters the store, so permission prompts can't race
  // the store-driven sync. Throws to the caller on denial / cancel / no audio shared.
  async acquireInput(input) {
    await this.start();
    const src = await this.makeSource(input);
    this.pendingSources.set(input.id, src);
  }

  async ensureChannel(input) {
    const existing = this.chans.get(input.id);
    if (existing) return existing;
    // A source that already failed is not retried automatically — that would re-prompt
    // for permission on every state change. Remove and re-add the input to retry.
    if (store.state.live[input.id] === 'error') return null;
    const ctx = this.ctx;
    const gain = ctx.createGain();
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 512; analyser.smoothingTimeConstant = 0.65;
    gain.connect(analyser);
    const sends = new Map();
    for (const m of MIXES) {
      const s = ctx.createGain();
      analyser.connect(s);
      s.connect(this.ensureMix(m.id).bus);
      sends.set(m.id, s);
    }
    const ch = { gain, analyser, sends, data: new Uint8Array(analyser.frequencyBinCount), srcNode: null, stop: null };
    this.chans.set(input.id, ch);
    try {
      const pre = this.pendingSources.get(input.id);
      this.pendingSources.delete(input.id);
      if (!pre && !store.input(input.id)) {   // stale caller: input already gone, never prompt
        this.removeChannel(input.id);
        return null;
      }
      const src = pre || await this.makeSource(input);
      if (this.chans.get(input.id) !== ch) {
        // Input was removed while the source was connecting — stop the stream, don't leak it.
        try { src.stop?.(); } catch { /* noop */ }
        return null;
      }
      src.node.connect(gain);
      ch.srcNode = src.node; ch.stop = src.stop;
      store.state.live[input.id] = 'active';
      return ch;
    } catch (e) {
      this.removeChannel(input.id);
      store.state.live[input.id] = 'error';
      console.warn('source failed', input.name, e);
      return null;
    }
  }

  removeChannel(inputId) {
    const ch = this.chans.get(inputId);
    if (!ch) return;
    try { ch.stop?.(); } catch { /* noop */ }
    try { ch.gain.disconnect(); ch.analyser.disconnect(); } catch { /* noop */ }
    for (const s of ch.sends.values()) { try { s.disconnect(); } catch { /* noop */ } }
    this.chans.delete(inputId);
    delete store.state.live[inputId];
  }

  async ensureOutput(out) {
    if (this.outs.has(out.id)) return this.outs.get(out.id);
    const ctx = this.ctx;
    const gain = ctx.createGain();
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 512; analyser.smoothingTimeConstant = 0.7;
    const limiter = ctx.createDynamicsCompressor();
    limiter.threshold.value = -6; limiter.knee.value = 4; limiter.ratio.value = 12;
    limiter.attack.value = 0.002; limiter.release.value = 0.12;
    const dest = ctx.createMediaStreamDestination();
    gain.connect(analyser); analyser.connect(limiter); limiter.connect(dest);
    const el = new Audio();
    el.srcObject = dest.stream; el.autoplay = true;
    const o = { gain, analyser, limiter, dest, el, data: new Uint8Array(analyser.frequencyBinCount), mixId: null };
    this.outs.set(out.id, o);
    await this.routeOutput(out);
    return o;
  }

  async routeOutput(out) {
    const o = this.outs.get(out.id);
    if (!o) return;
    if (o.mixId !== out.mixId) {
      if (o.mixId && this.mixes.has(o.mixId)) { try { this.mixes.get(o.mixId).analyser.disconnect(o.gain); } catch { /* noop */ } }
      this.ensureMix(out.mixId).analyser.connect(o.gain);
      o.mixId = out.mixId;
    }
    if (out.deviceId) {
      try {
        // '' is the spec value for the OS default sink — needed to route BACK to default.
        const want = out.deviceId === 'default' ? '' : out.deviceId;
        if (typeof o.el.setSinkId === 'function' && (o.sinkId || '') !== want) {
          await o.el.setSinkId(want);
          o.sinkId = want;
        }
        if (o.el.paused) await o.el.play().catch(() => {});
        o.el.muted = false;
      } catch (e) { console.warn('sink failed', out.name, e); }
    } else {
      o.el.muted = true;   // virtual output: metered, not audible
    }
  }

  removeOutput(id) {
    const o = this.outs.get(id);
    if (!o) return;
    try { o.el.pause(); o.el.srcObject = null; } catch { /* noop */ }
    // Sever the incoming edge too — disconnect() alone only cuts outgoing connections,
    // and the long-lived mix analyser would pin this gain node forever.
    if (o.mixId && this.mixes.has(o.mixId)) {
      try { this.mixes.get(o.mixId).analyser.disconnect(o.gain); } catch { /* noop */ }
    }
    try { o.gain.disconnect(); } catch { /* noop */ }
    this.outs.delete(id);
  }

  // Push all state volumes/mutes/structure into the graph.
  async sync() {
    if (!this.ctx) return;
    const s = store.state, t = this.ctx.currentTime;
    const wanted = new Set(s.inputs.map(i => i.id));
    for (const id of [...this.chans.keys()]) if (!wanted.has(id)) this.removeChannel(id);
    let liveChanged = false;
    for (const input of s.inputs) {
      // sync can suspend on a permission prompt while the inputs array is reassigned
      // under it — re-check membership so a removed input is never (re)acquired.
      if (!store.input(input.id)) continue;
      if (store.state.live[input.id] === 'error') continue;   // failed source; not auto-retried
      let ch = this.chans.get(input.id);
      if (!ch) {
        ch = await this.ensureChannel(input);
        liveChanged = true;               // created or failed — either way views must repaint
      }
      if (!ch) continue;   // source failed or input was removed while connecting
      ch.gain.gain.setTargetAtTime(input.volume, t, 0.02);
      for (const m of MIXES) {
        const send = input.sends[m.id];
        ch.sends.get(m.id).gain.setTargetAtTime(send.muted ? 0 : send.level, t, 0.02);
      }
    }
    for (const m of MIXES) {
      const mx = this.ensureMix(m.id), conf = s.mixes[m.id];
      mx.gain.gain.setTargetAtTime(conf.muted ? 0 : conf.volume, t, 0.02);
    }
    const wantedOut = new Set(s.outputs.map(o => o.id));
    for (const id of [...this.outs.keys()]) if (!wantedOut.has(id)) this.removeOutput(id);
    for (const out of s.outputs) {
      if (!store.output(out.id)) continue;   // removed while an earlier await was pending
      await this.ensureOutput(out);
      const o = this.outs.get(out.id);
      if (!o) continue;
      o.gain.gain.setTargetAtTime(out.muted ? 0 : out.volume, t, 0.02);
      await this.routeOutput(out);
    }
    // One batched repaint per sync — per-channel emits caused a render storm on first start.
    if (liveChanged) store.emit('routing');
  }

  levelOf(entry) {
    if (!entry) return 0;
    entry.analyser.getByteTimeDomainData(entry.data);
    let peak = 0;
    for (let i = 0; i < entry.data.length; i += 2) {
      const v = Math.abs(entry.data[i] - 128) / 128;
      if (v > peak) peak = v;
    }
    return peak;
  }
  channelLevel(id) { return this.levelOf(this.chans.get(id)); }
  mixLevel(id) { return this.levelOf(this.mixes.get(id)); }
  outputLevel(id) { return this.levelOf(this.outs.get(id)); }

  // --- recording ---
  isRecording(mixId) { return this.recorders.has(mixId); }
  startRecording(mixId) {
    if (!this.ctx || this.recorders.has(mixId)) return false;
    const dest = this.ctx.createMediaStreamDestination();
    this.mixes.get(mixId).analyser.connect(dest);
    const rec = new MediaRecorder(dest.stream, { mimeType: MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus' : 'audio/webm' });
    const chunks = [];
    rec.ondataavailable = e => { if (e.data.size) chunks.push(e.data); };
    rec.onstop = () => {
      const blob = new Blob(chunks, { type: 'audio/webm' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      const stamp = new Date().toISOString().replace(/[:T]/g, '-').slice(0, 19);
      a.download = `${mixId}-${stamp}.webm`;
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 5000);
      try { this.mixes.get(mixId).analyser.disconnect(dest); } catch { /* noop */ }
    };
    rec.start();
    this.recorders.set(mixId, { rec, chunks });
    return true;
  }
  stopRecording(mixId) {
    const r = this.recorders.get(mixId);
    if (!r) return;
    r.rec.stop();
    this.recorders.delete(mixId);
  }

  async listDevices() {
    try {
      const devs = await navigator.mediaDevices.enumerateDevices();
      return {
        mics: devs.filter(d => d.kind === 'audioinput'),
        outs: devs.filter(d => d.kind === 'audiooutput'),
      };
    } catch { return { mics: [], outs: [] }; }
  }
}

export const engine = new Engine();
store.on((what) => {
  if (what === 'levels' || what === 'routing' || what === 'structure') engine.sync();
});
