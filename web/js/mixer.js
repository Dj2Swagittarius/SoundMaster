// Mixer view — the working surface behind the flow diagram.
// Mix cards up top, channel strips for the selected mix, outputs below.
import { store, MIXES } from './state.js';
import { engine } from './engine.js';
import { icon, tileColors } from './icons.js';
import { esc } from './flow.js';

let root, activeMix = 'personal', raf = 0, renderGen = 0;

function pct(v) { return `${Math.round(v * 100)}%`; }

function focusKeyOf(elm) {
  if (!elm || !root.contains(elm)) return null;
  for (const [k, v] of Object.entries(elm.dataset || {})) return `[data-${k.replace(/[A-Z]/g, c => '-' + c.toLowerCase())}="${CSS.escape(v)}"]`;
  return elm.id ? `#${CSS.escape(elm.id)}` : null;
}

function render() {
  if (!root) return;
  const restoreFocus = focusKeyOf(document.activeElement);
  const s = store.state;

  const mixCards = MIXES.map(m => {
    const conf = s.mixes[m.id];
    const recordable = m.id === 'stream' || m.id === 'vod';
    const rec = engine.isRecording(m.id);
    return `
      <div class="mix-card ${activeMix === m.id ? 'is-active' : ''} ${conf.muted ? 'is-muted' : ''}" data-mix="${m.id}"
           role="button" tabindex="0" aria-pressed="${activeMix === m.id}" aria-label="Show channels in ${esc(m.name)}">
        <div class="mix-card-top">
          <div class="fnode-icon">${icon(m.icon)}</div>
          <div class="fnode-text">
            <div class="fnode-title">${esc(m.name)}</div>
            <div class="fnode-sub">Vol ${pct(conf.volume)}</div>
          </div>
          ${recordable ? `<button class="btn-icon btn-rec ${rec ? 'is-on' : ''}" data-rec="${m.id}"
              title="${rec ? 'Stop recording' : 'Record this mix'}" aria-label="${rec ? 'Stop recording' : 'Record ' + esc(m.name)}">
              ${icon(rec ? 'stop' : 'record')}</button>` : ''}
          <button class="btn-icon btn-mute ${conf.muted ? 'is-on' : ''}" data-mute-mix="${m.id}"
              title="Mute mix" aria-label="Mute ${esc(m.name)}" aria-pressed="${conf.muted}">${icon('speaker')}</button>
        </div>
        <input type="range" min="0" max="1" step="0.01" value="${conf.volume}"
               data-mixvol="${m.id}" aria-label="${esc(m.name)} volume">
        <div class="meter"><div class="meter-fill" data-meter-mix="${m.id}"></div></div>
      </div>`;
  }).join('');

  const strips = s.inputs.map(i => {
    const send = i.sends[activeMix];
    const tint = i.kind === 'Software' ? `style="background:${tileColors[i.icon] || '#565a63'};color:#fff"` : '';
    const removable = !i.srcType.startsWith('demo:');
    return `
      <div class="strip ${send.muted ? 'is-muted' : ''}" data-input="${esc(i.id)}">
        <div class="fnode-icon" ${tint}>${icon(i.icon)}</div>
        <div class="strip-name">
          <div class="fnode-title">${esc(i.name)}</div>
          <div class="fnode-sub">${esc(i.kind)} · input ${pct(i.volume)}</div>
        </div>
        <input class="strip-range" type="range" min="0" max="1" step="0.01" value="${send.level}"
               data-send="${esc(i.id)}" aria-label="${esc(i.name)} level in ${esc(MIXES.find(m => m.id === activeMix)?.name || activeMix)}">
        <div class="strip-pct">${pct(send.level)}</div>
        <button class="btn-icon btn-mute ${send.muted ? 'is-on' : ''}" data-mute-send="${esc(i.id)}"
                title="Mute in this mix" aria-pressed="${send.muted}" aria-label="Mute ${esc(i.name)} in this mix">${icon('speaker')}</button>
        <div class="meter meter-v"><div class="meter-fill" data-meter-chan="${esc(i.id)}"></div></div>
        ${removable ? `<button class="btn-icon" data-remove-input="${esc(i.id)}" title="Remove input" aria-label="Remove ${esc(i.name)}">${icon('x')}</button>` : ''}
      </div>`;
  }).join('');

  const outs = s.outputs.map(o => `
      <div class="strip out-strip ${o.muted ? 'is-muted' : ''}" data-output="${esc(o.id)}">
        <div class="fnode-icon">${icon(o.icon)}</div>
        <div class="strip-name">
          <div class="fnode-title">${esc(o.name)}</div>
          <div class="fnode-sub">${o.deviceId ? 'Plays' : 'No device — silent'} · ${esc(MIXES.find(m => m.id === o.mixId)?.name || '')}</div>
        </div>
        <select data-outmix="${esc(o.id)}" aria-label="Mix for ${esc(o.name)}">
          ${MIXES.map(m => `<option value="${m.id}" ${o.mixId === m.id ? 'selected' : ''}>${esc(m.name)}</option>`).join('')}
        </select>
        <select data-outdev="${esc(o.id)}" aria-label="Device for ${esc(o.name)}"><option value="">No device</option></select>
        <input class="strip-range" type="range" min="0" max="1" step="0.01" value="${o.volume}"
               data-outvol="${esc(o.id)}" aria-label="${esc(o.name)} volume">
        <div class="strip-pct">${pct(o.volume)}</div>
        <button class="btn-icon btn-mute ${o.muted ? 'is-on' : ''}" data-mute-out="${esc(o.id)}"
                title="Mute output" aria-pressed="${o.muted}" aria-label="Mute ${esc(o.name)}">${icon('speaker')}</button>
        <button class="btn-icon" data-remove-output="${esc(o.id)}" title="Remove output" aria-label="Remove ${esc(o.name)}">${icon('x')}</button>
      </div>`).join('');

  root.innerHTML = `
    <div class="flow-head">
      <div>
        <h1>Mixes</h1>
        <p class="flow-sub">Set levels and mutes — the flow diagram reflects every change</p>
      </div>
      <div class="head-actions">
        <button class="btn" id="btn-add-output">${icon('plus')}<span>Add output</span></button>
        <button class="btn" id="btn-add-input">${icon('plus')}<span>Add input</span></button>
      </div>
    </div>
    <div class="mix-cards">${mixCards}</div>
    <h2 class="section-title">Channels in ${esc(MIXES.find(m => m.id === activeMix)?.name || '')}</h2>
    <div class="strips">${strips || '<div class="empty">No inputs yet. Add one to get started.</div>'}</div>
    <h2 class="section-title">Outputs</h2>
    <div class="strips">${outs || '<div class="empty">No outputs yet.</div>'}</div>`;

  fillDeviceSelects(++renderGen);
  bind();
  if (restoreFocus) root.querySelector(restoreFocus)?.focus({ preventScroll: true });
  cancelAnimationFrame(raf);
  meterTick();
}

async function fillDeviceSelects(gen) {
  const { outs } = await engine.listDevices();
  if (gen !== renderGen) return;   // a newer render owns the selects now — don't double-fill
  for (const sel of root.querySelectorAll('[data-outdev]')) {
    const o = store.output(sel.dataset.outdev);
    for (const d of outs) {
      const opt = document.createElement('option');
      opt.value = d.deviceId;
      opt.textContent = d.label || (d.deviceId === 'default' ? 'System default' : `Output ${sel.length}`);
      if (o?.deviceId === d.deviceId) opt.selected = true;
      sel.appendChild(opt);
    }
    if (o?.deviceId === 'default' && ![...sel.options].some(x => x.selected && x.value)) {
      const opt = document.createElement('option');
      opt.value = 'default'; opt.textContent = 'System default'; opt.selected = true;
      sel.appendChild(opt);
    }
  }
}

function bind() {
  root.querySelectorAll('.mix-card').forEach(c => {
    c.addEventListener('click', (ev) => {
      if (ev.target.closest('button,input')) return;
      activeMix = c.dataset.mix; render();
    });
    c.addEventListener('keydown', (ev) => {
      if ((ev.key === 'Enter' || ev.key === ' ') && ev.target === c) {
        ev.preventDefault();
        activeMix = c.dataset.mix; render();
      }
    });
  });
  root.querySelectorAll('[data-mixvol]').forEach(r =>
    r.addEventListener('input', () => {
      store.setMixVolume(r.dataset.mixvol, +r.value);
      r.closest('.mix-card').querySelector('.fnode-sub').textContent = `Vol ${pct(+r.value)}`;
    }));
  root.querySelectorAll('[data-mute-mix]').forEach(b =>
    b.addEventListener('click', () => store.setMixMuted(b.dataset.muteMix, !store.state.mixes[b.dataset.muteMix].muted)));
  root.querySelectorAll('[data-rec]').forEach(b =>
    b.addEventListener('click', async () => {
      const id = b.dataset.rec;
      if (engine.isRecording(id)) { engine.stopRecording(id); toast('Recording saved to your downloads.'); }
      else {
        await window.smStartAudio?.();
        if (engine.startRecording(id)) toast(`Recording ${MIXES.find(m => m.id === id).name}…`);
      }
      render();
    }));
  root.querySelectorAll('[data-send]').forEach(r =>
    r.addEventListener('input', () => {
      store.setSend(r.dataset.send, activeMix, +r.value);
      r.closest('.strip').querySelector('.strip-pct').textContent = pct(+r.value);
    }));
  root.querySelectorAll('[data-mute-send]').forEach(b =>
    b.addEventListener('click', () => {
      const i = store.input(b.dataset.muteSend);
      if (!i) return;                      // stale strip from a removed input
      store.setSendMuted(i.id, activeMix, !i.sends[activeMix].muted);
    }));
  root.querySelectorAll('[data-remove-input]').forEach(b =>
    b.addEventListener('click', () => store.removeInput(b.dataset.removeInput)));
  root.querySelectorAll('[data-outvol]').forEach(r =>
    r.addEventListener('input', () => {
      store.setOutput(r.dataset.outvol, { volume: +r.value });
      r.closest('.strip').querySelector('.strip-pct').textContent = pct(+r.value);
    }));
  root.querySelectorAll('[data-mute-out]').forEach(b =>
    b.addEventListener('click', () => store.setOutput(b.dataset.muteOut, { muted: !store.output(b.dataset.muteOut).muted })));
  root.querySelectorAll('[data-outmix]').forEach(sel =>
    sel.addEventListener('change', () => store.setOutput(sel.dataset.outmix, { mixId: sel.value })));
  root.querySelectorAll('[data-outdev]').forEach(sel =>
    sel.addEventListener('change', async () => {
      await window.smStartAudio?.();
      store.setOutput(sel.dataset.outdev, { deviceId: sel.value || null });
    }));
  root.querySelectorAll('[data-remove-output]').forEach(b =>
    b.addEventListener('click', () => store.removeOutput(b.dataset.removeOutput)));
  root.querySelector('#btn-add-input')?.addEventListener('click', () => window.smOpenAddInput?.());
  root.querySelector('#btn-add-output')?.addEventListener('click', () => {
    const n = store.state.outputs.length + 1;
    store.addOutput({ id: `out-${Date.now()}`, name: `Output ${n}`, icon: 'speaker', deviceId: 'default', mixId: activeMix, volume: 1, muted: false });
  });
}

function meterTick() {
  raf = requestAnimationFrame(meterTick);
  if (!engine.running || root.offsetParent === null) return;
  for (const m of root.querySelectorAll('[data-meter-mix]'))
    m.style.width = `${Math.min(100, engine.mixLevel(m.dataset.meterMix) * 130)}%`;
  for (const m of root.querySelectorAll('[data-meter-chan]'))
    m.style.height = `${Math.min(100, engine.channelLevel(m.dataset.meterChan) * 130)}%`;
}

function toast(msg) { window.smToast?.(msg); }

export function initMixer(container) {
  root = container;
  store.on((what) => {
    if (root.offsetParent === null) return;
    if (what === 'structure' || what === 'routing' || what === 'view') render();
  });
  // Becoming visible fires a resize — this repaints state that changed while hidden.
  new ResizeObserver(() => { if (root.offsetParent !== null) render(); }).observe(root);
  render();
}

export function refreshMixer() { render(); }
