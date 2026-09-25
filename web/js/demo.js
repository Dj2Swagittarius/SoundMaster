// Procedural demo signal set — a small original lo-fi jam, one generator per demo input,
// so the flow diagram is alive the moment audio starts. No samples, all synthesized.

const BPM = 84;
const STEP = 60 / BPM / 4;                 // 16th note, seconds
const BAR = STEP * 16;

// Chord loop (Am7 – Fmaj7 – Cmaj7 – G6), one chord per bar. Frequencies in Hz.
const N = n => 440 * Math.pow(2, (n - 69) / 12); // midi -> Hz
const CHORDS = [
  [57, 60, 64, 67],   // A C E G
  [53, 57, 60, 65],   // F A C E
  [48, 52, 55, 59],   // C E G B
  [55, 59, 62, 64],   // G B D E
];
const BASS = [45, 41, 36, 43];             // roots, one octave down-ish
const PENTA = [57, 60, 62, 64, 67, 69, 72, 76];

function noiseBuffer(ctx, seconds = 1) {
  const buf = ctx.createBuffer(1, ctx.sampleRate * seconds, ctx.sampleRate);
  const d = buf.getChannelData(0);
  for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
  return buf;
}

// Disconnect a terminal node once its sound is over, so per-note subgraphs can be GC'd
// instead of piling up connections on the destination for the life of the session.
function reap(ctx, node, endTime) {
  setTimeout(() => { try { node.disconnect(); } catch { /* already gone */ } },
    Math.max(50, (endTime - ctx.currentTime + 0.3) * 1000));
}

function env(ctx, out, when, peak, a, d, sustainLevel = 0) {
  const g = ctx.createGain();
  g.gain.setValueAtTime(0.0001, when);
  g.gain.linearRampToValueAtTime(peak, when + a);
  g.gain.exponentialRampToValueAtTime(Math.max(sustainLevel, 0.0001), when + a + d);
  g.connect(out);
  reap(ctx, g, when + a + d);
  return g;
}

function osc(ctx, type, freq, when, stopAt) {
  const o = ctx.createOscillator();
  o.type = type; o.frequency.setValueAtTime(freq, when);
  o.start(when); o.stop(stopAt);
  return o;
}

// Each generator: { start(ctx, dest, clock), stop() }
// clock schedules pattern callbacks: clock.add(fn(stepIndex, when))

export function makeClock(ctx) {
  const subs = new Set();
  let step = 0, nextTime = 0, timer = null;
  const LOOKAHEAD = 0.15, TICK = 40;
  return {
    add(fn) { subs.add(fn); return () => subs.delete(fn); },
    start() {
      if (timer) return;
      nextTime = ctx.currentTime + 0.08; step = 0;
      timer = setInterval(() => {
        while (nextTime < ctx.currentTime + LOOKAHEAD) {
          for (const fn of subs) { try { fn(step, nextTime); } catch (e) { console.warn('demo gen', e); } }
          step = (step + 1) % 64;          // 4 bars of 16ths
          nextTime += STEP;
        }
      }, TICK);
    },
    stop() { clearInterval(timer); timer = null; subs.clear(); },
  };
}

export const demoGenerators = {
  // Music: drums + dusty keys. The centerpiece channel.
  'demo:music': (ctx, dest, clock) => {
    const noise = noiseBuffer(ctx);
    return clock.add((step, t) => {
      const s16 = step % 16, bar = (step / 16) | 0;
      // kick
      if (s16 === 0 || s16 === 8 || (s16 === 11 && bar % 2)) {
        const o = osc(ctx, 'sine', 118, t, t + 0.28);
        o.frequency.exponentialRampToValueAtTime(44, t + 0.14);
        o.connect(env(ctx, dest, t, 0.5, 0.004, 0.24));
      }
      // snare
      if (s16 === 4 || s16 === 12) {
        const src = ctx.createBufferSource(); src.buffer = noise;
        const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 1900; bp.Q.value = 0.9;
        src.connect(bp); bp.connect(env(ctx, dest, t, 0.2, 0.002, 0.12));
        src.start(t); src.stop(t + 0.2);
        const o = osc(ctx, 'triangle', 196, t, t + 0.1);
        o.connect(env(ctx, dest, t, 0.07, 0.002, 0.08));
      }
      // hats
      if (s16 % 2 === 0) {
        const src = ctx.createBufferSource(); src.buffer = noise;
        const hp = ctx.createBiquadFilter(); hp.type = 'highpass'; hp.frequency.value = 8200;
        src.connect(hp); hp.connect(env(ctx, dest, t, s16 % 4 === 2 ? 0.08 : 0.04, 0.001, 0.045));
        src.start(t); src.stop(t + 0.08);
      }
      // dusty keys, one strum per bar + pickup
      if (s16 === 0 || s16 === 7) {
        const chord = CHORDS[bar % 4];
        chord.forEach((m, i) => {
          const when = t + i * 0.014;
          const o = osc(ctx, 'triangle', N(m + 12), when, when + 1.6);
          const o2 = osc(ctx, 'sine', N(m + 24), when, when + 1.6);
          const lp = ctx.createBiquadFilter(); lp.type = 'lowpass';
          lp.frequency.setValueAtTime(2600, when);
          lp.frequency.exponentialRampToValueAtTime(700, when + 1.2);
          const g = env(ctx, dest, when, s16 === 0 ? 0.09 : 0.055, 0.006, 1.3);
          o.connect(lp); o2.connect(lp); lp.connect(g);
        });
      }
    });
  },

  // Line In: round sub-ish bass following the roots.
  'demo:bass': (ctx, dest, clock) => clock.add((step, t) => {
    const s16 = step % 16, bar = (step / 16) | 0;
    if (s16 === 0 || s16 === 8 || s16 === 10) {
      const m = BASS[bar % 4] + (s16 === 10 ? 7 : 0);
      const o = osc(ctx, 'sine', N(m), t, t + 0.5);
      const o2 = osc(ctx, 'triangle', N(m), t, t + 0.5);
      const g = env(ctx, dest, t, 0.3, 0.008, 0.42);
      o.connect(g); const g2 = env(ctx, dest, t, 0.07, 0.008, 0.4); o2.connect(g2);
    }
  }),

  // USB Aux: slow airy pad.
  'demo:pad': (ctx, dest, clock) => clock.add((step, t) => {
    if (step % 16 !== 0) return;
    const bar = (step / 16) | 0;
    const chord = CHORDS[bar % 4];
    chord.forEach(m => {
      const o = osc(ctx, 'sawtooth', N(m + 12), t, t + BAR + 0.4);
      const o2 = osc(ctx, 'sawtooth', N(m + 12) * 1.004, t, t + BAR + 0.4);
      const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 620; lp.Q.value = 0.4;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.0001, t);
      g.gain.linearRampToValueAtTime(0.022, t + 1.1);
      g.gain.linearRampToValueAtTime(0.0001, t + BAR + 0.3);
      o.connect(lp); o2.connect(lp); lp.connect(g); g.connect(dest);
      reap(ctx, g, t + BAR + 0.4);
    });
  }),

  // XLR Mic 1: periodic "spoken phrase" — band-passed noise with wandering formant. Reads as voice on a meter.
  'demo:voice': (ctx, dest, clock) => {
    const noise = noiseBuffer(ctx, 2);
    return clock.add((step, t) => {
      if (step % 32 !== 6) return;                       // every 2 bars
      const dur = 1.5;
      const src = ctx.createBufferSource(); src.buffer = noise; src.loop = true;
      const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.Q.value = 2.2;
      bp.frequency.setValueAtTime(380, t);
      // syllable-ish wobble
      for (let i = 0; i < 7; i++) {
        bp.frequency.linearRampToValueAtTime(300 + Math.random() * 900, t + (i + 1) * (dur / 7));
      }
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.0001, t);
      for (let i = 0; i < 7; i++) {
        const tt = t + i * (dur / 7);
        g.gain.linearRampToValueAtTime(0.1 + Math.random() * 0.06, tt + 0.05);
        g.gain.linearRampToValueAtTime(0.015, tt + dur / 7 * 0.85);
      }
      g.gain.linearRampToValueAtTime(0.0001, t + dur);
      src.connect(bp); bp.connect(g); g.connect(dest);
      src.start(t); src.stop(t + dur + 0.05);
      reap(ctx, g, t + dur);
    });
  },

  // Capture: soft game-y arp.
  'demo:arp': (ctx, dest, clock) => clock.add((step, t) => {
    if (step % 2) return;
    const idx = [0, 2, 4, 7, 4, 2][((step / 2) | 0) % 6];
    const o = osc(ctx, 'square', N(PENTA[idx % PENTA.length]), t, t + 0.12);
    const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 2400;
    o.connect(lp); lp.connect(env(ctx, dest, t, 0.045, 0.004, 0.1));
  }),

  // Browser: vinyl-style crackle bed.
  'demo:crackle': (ctx, dest, clock) => {
    const src = ctx.createBufferSource(); src.buffer = noiseBuffer(ctx, 2); src.loop = true;
    const hp = ctx.createBiquadFilter(); hp.type = 'highpass'; hp.frequency.value = 3000;
    const g = ctx.createGain(); g.gain.value = 0.012;
    src.connect(hp); hp.connect(g); g.connect(dest); src.start();
    const off = clock.add((step, t) => {
      if (Math.random() < 0.09) {                        // pops
        const p = ctx.createBufferSource(); p.buffer = src.buffer;
        const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 4200; bp.Q.value = 6;
        p.connect(bp); bp.connect(env(ctx, dest, t, 0.1, 0.001, 0.03));
        p.start(t); p.stop(t + 0.05);
      }
    });
    return () => { off(); try { src.stop(); } catch { /* already stopped */ } };
  },

  // Chat: occasional two-tone notification.
  'demo:blip': (ctx, dest, clock) => clock.add((step, t) => {
    if (step !== 24 && step !== 56) return;
    [660, 880].forEach((f, i) => {
      const o = osc(ctx, 'sine', f, t + i * 0.14, t + i * 0.14 + 0.22);
      o.connect(env(ctx, dest, t + i * 0.14, 0.14, 0.004, 0.18));
    });
  }),

  // Deck: sparse UI tick.
  'demo:tick': (ctx, dest, clock) => clock.add((step, t) => {
    if (step !== 40) return;
    const o = osc(ctx, 'square', 1320, t, t + 0.05);
    o.connect(env(ctx, dest, t, 0.06, 0.001, 0.04));
  }),
};
