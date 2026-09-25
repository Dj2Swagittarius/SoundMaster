// App shell: sidebar, view switching, audio power-on, add-input dialog, settings, toasts.
import { store, MIXES } from './state.js';
import { engine } from './engine.js';
import { icon, tileColors } from './icons.js';
import { initFlow, esc } from './flow.js';
import { initMixer } from './mixer.js';

const $ = (sel, el = document) => el.querySelector(sel);

// ---------- audio power-on ----------
let starting = null;
async function startAudio() {
  if (engine.running) return true;
  starting = starting || engine.start().finally(() => { starting = null; updatePowerPill(); });
  const ok = await starting;
  updatePowerPill();
  return ok;
}
window.smStartAudio = startAudio;

function updatePowerPill() {
  const pill = $('#power-pill');
  if (!pill) return;
  pill.style.display = engine.running ? 'none' : '';
}

// ---------- toasts ----------
window.smToast = (msg) => {
  const holder = $('#toasts');
  const t = document.createElement('div');
  t.className = 'toast';
  t.textContent = msg;
  holder.appendChild(t);
  setTimeout(() => t.classList.add('show'));
  setTimeout(() => { t.classList.remove('show'); setTimeout(() => t.remove(), 300); }, 3800);
};

// ---------- sidebar ----------
function renderSidebar() {
  const s = store.state;
  const hw = s.inputs.filter(i => i.kind === 'Hardware');
  const sw = s.inputs.filter(i => i.kind === 'Software');
  const item = (i) => `
    <button class="side-item" data-goto-input="${esc(i.id)}" aria-label="${esc(i.name)}">
      <span class="side-ic"
            ${i.kind === 'Software' ? `style="color:${tileColors[i.icon] || '#9b9ca1'}"` : ''}>${icon(i.icon)}</span>
      <span>${esc(i.name)}</span>
    </button>`;
  $('#side-sources').innerHTML = `
    ${hw.length ? `<div class="side-label">Devices</div>${hw.map(item).join('')}` : ''}
    ${sw.length ? `<div class="side-label">Apps</div>${sw.map(item).join('')}` : ''}`;
  $('#side-sources').querySelectorAll('[data-goto-input]').forEach(b =>
    b.addEventListener('click', () => {
      store.setView('flow');
      syncView();
      store.select({ type: 'input', id: b.dataset.gotoInput });
    }));
}

function syncView() {
  const v = store.state.view;
  $('#view-flow').style.display = v === 'flow' ? '' : 'none';
  $('#view-mixer').style.display = v === 'mixer' ? '' : 'none';
  $('#nav-flow').classList.toggle('is-active', v === 'flow');
  $('#nav-mixer').classList.toggle('is-active', v === 'mixer');
}

// ---------- add input ----------
async function openAddInput() {
  const dlg = $('#dlg-add');
  const list = $('#mic-list');
  list.innerHTML = '<option value="">Default microphone</option>';
  const { mics } = await engine.listDevices();
  mics.forEach((d, n) => {
    const o = document.createElement('option');
    o.value = d.deviceId;
    o.textContent = d.label || `Microphone ${n + 1}`;
    list.appendChild(o);
  });
  dlg.showModal();
}
window.smOpenAddInput = openAddInput;

function uid(p) { return `${p}-${Date.now().toString(36)}${Math.floor(Math.random() * 999)}`; }

async function addMic() {
  const deviceId = $('#mic-list').value || undefined;
  const label = $('#mic-list').selectedOptions[0]?.textContent || 'Microphone';
  const input = {
    id: uid('mic'), name: label.replace(/\s*\(.*?\)\s*$/, '').slice(0, 28) || 'Microphone',
    kind: 'Hardware', icon: 'mic', srcType: 'mic', deviceId, volume: 1,
    sends: Object.fromEntries(MIXES.map(m => [m.id, { level: 1, muted: false }])),
  };
  try {
    await engine.acquireInput(input);        // permission prompt happens HERE, before the store knows
    store.addInput(input);
    window.smToast(`${input.name} added.`);
  } catch {
    window.smToast('Microphone permission was blocked.');
  }
}

async function addSystem() {
  const input = {
    id: uid('sys'), name: 'System Audio', kind: 'Software', icon: 'system', srcType: 'system', volume: 1,
    sends: Object.fromEntries(MIXES.map(m => [m.id, { level: 1, muted: false }])),
  };
  try {
    await engine.acquireInput(input);        // share picker resolves before the input is added
    store.addInput(input);
    window.smToast('System audio added.');
  } catch (e) {
    window.smToast(/audio/i.test(e?.message || '') ? e.message
      : 'Nothing was shared — tick "Also share system audio", or share a tab with audio.');
  }
}

async function addFile(file) {
  await startAudio();
  const input = {
    id: uid('file'), name: file.name.replace(/\.[^.]+$/, '').slice(0, 28) || 'Media',
    kind: 'Software', icon: 'file', srcType: 'file', objectUrl: URL.createObjectURL(file), volume: 1,
    sends: Object.fromEntries(MIXES.map(m => [m.id, { level: 1, muted: false }])),
  };
  store.addInput(input);
  window.smToast(`${input.name} playing on loop.`);
}

async function addTone() {
  await startAudio();
  store.addInput({
    id: uid('tone'), name: 'Test Tone', kind: 'Software', icon: 'tone', srcType: 'tone', freq: 440, volume: 1,
    sends: Object.fromEntries(MIXES.map(m => [m.id, { level: 1, muted: false }])),
  });
  window.smToast('440 Hz test tone added.');
}

// ---------- boot ----------
function boot() {
  document.body.innerHTML = `
    <aside class="sidebar">
      <div class="side-top">
        <span class="side-logo">${icon('logo')}</span>
        <span class="side-appname">SoundMaster</span>
      </div>
      <div class="side-scroll">
        <div id="side-sources"></div>
        <div class="side-label">Mixes &amp; effects</div>
        <button class="side-item" id="nav-mixer" aria-label="Mixes">${icon('sliders')}<span>Mixes</span></button>
        <button class="side-item is-active" id="nav-flow" aria-label="Audio flow">${icon('flow')}<span>Audio flow</span></button>
      </div>
      <div class="side-bottom">
        <button class="side-item" id="nav-add" aria-label="Add input">${icon('plus')}<span>Add input</span></button>
        <button class="side-item" id="nav-settings" aria-label="Settings">${icon('settings')}<span>Settings</span></button>
      </div>
    </aside>
    <main class="main">
      <button id="power-pill" class="power-pill" title="Browsers need one click before audio can start">
        ${icon('power')}<span>Start audio</span>
      </button>
      <section id="view-flow" class="view"></section>
      <section id="view-mixer" class="view" style="display:none"></section>
    </main>
    <div id="toasts" role="status" aria-live="polite"></div>

    <dialog id="dlg-add" class="dlg" aria-labelledby="dlg-add-title">
      <h2 id="dlg-add-title">Add input</h2>
      <div class="dlg-row">
        <select id="mic-list" aria-label="Microphone device"></select>
        <button class="btn" id="add-mic">${icon('mic')}<span>Add microphone</span></button>
      </div>
      <button class="btn dlg-wide" id="add-system">${icon('system')}<span>System / app audio (screen share picker)</span></button>
      <button class="btn dlg-wide" id="add-file-btn">${icon('file')}<span>Media file (loops)</span></button>
      <input type="file" id="add-file-input" accept="audio/*,video/*" hidden>
      <button class="btn dlg-wide" id="add-tone">${icon('tone')}<span>Test tone (440 Hz)</span></button>
      <div class="dlg-actions"><button class="btn" id="dlg-add-close">Close</button></div>
    </dialog>

    <dialog id="dlg-settings" class="dlg" aria-labelledby="dlg-settings-title">
      <h2 id="dlg-settings-title">Settings</h2>
      <label class="dlg-check">
        <input type="checkbox" id="opt-demo"> Demo signal set (synthesized jam so the diagram is alive)
      </label>
      <button class="btn dlg-wide" id="opt-reset">Reset everything to defaults</button>
      <p class="dlg-note">SoundMaster is a personal, local recreation of the Audio Flow experience.
      It runs entirely on this machine — no accounts, no network calls. Not affiliated with Elgato.</p>
      <div class="dlg-actions"><button class="btn" id="dlg-settings-close">Close</button></div>
    </dialog>`;

  initFlow($('#view-flow'));
  initMixer($('#view-mixer'));
  renderSidebar();
  syncView();

  $('#nav-flow').addEventListener('click', () => { store.setView('flow'); syncView(); });
  $('#nav-mixer').addEventListener('click', () => { store.setView('mixer'); syncView(); });
  $('#nav-add').addEventListener('click', openAddInput);
  $('#nav-settings').addEventListener('click', () => {
    $('#opt-demo').checked = store.state.demo;
    $('#dlg-settings').showModal();
  });
  $('#power-pill').addEventListener('click', startAudio);

  $('#dlg-add-close').addEventListener('click', () => $('#dlg-add').close());
  $('#dlg-settings-close').addEventListener('click', () => $('#dlg-settings').close());
  $('#add-mic').addEventListener('click', async () => { $('#dlg-add').close(); await addMic(); });
  $('#add-system').addEventListener('click', async () => { $('#dlg-add').close(); await addSystem(); });
  $('#add-file-btn').addEventListener('click', () => $('#add-file-input').click());
  $('#add-tone').addEventListener('click', async () => { $('#dlg-add').close(); await addTone(); });
  $('#add-file-input').addEventListener('change', (e) => {
    const f = e.target.files[0];
    $('#dlg-add').close();
    if (f) addFile(f);
    e.target.value = '';
  });
  $('#opt-demo').addEventListener('change', (e) => store.setDemo(e.target.checked));
  $('#opt-reset').addEventListener('click', () => { $('#dlg-settings').close(); store.reset(); });

  store.on((what) => { if (what === 'structure') renderSidebar(); });

  // Browsers require one user gesture before audio — first click anywhere powers on.
  const once = () => { startAudio(); document.removeEventListener('pointerdown', once); };
  document.addEventListener('pointerdown', once);
  updatePowerPill();
}

boot();
