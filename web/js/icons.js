// Hand-drawn SVG icon set. 24x24 viewBox, stroke = currentColor unless noted.
const S = 'fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"';

export const icons = {
  menu: `<path ${S} d="M4 7h16M4 12h16M4 17h16"/>`,
  logo: `<path fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" d="M4 12c2 0 2-5 4-5s2 10 4 10 2-10 4-10 2 5 4 5"/>`,
  mic: `<rect ${S} x="9" y="3" width="6" height="11" rx="3"/><path ${S} d="M5.5 11.5a6.5 6.5 0 0 0 13 0M12 18v3M9 21h6"/>`,
  line: `<rect ${S} x="3" y="8" width="18" height="8" rx="2"/><circle cx="8" cy="12" r="1.4" fill="currentColor"/><circle cx="12.5" cy="12" r="1.4" fill="currentColor"/><path ${S} d="M16.5 10.5v3"/>`,
  capture: `<rect ${S} x="3" y="5" width="18" height="12" rx="2"/><path ${S} d="M9 21h6"/><path d="M10.5 9.2l4 2.3-4 2.3z" fill="currentColor"/>`,
  browser: `<circle ${S} cx="12" cy="12" r="8.5"/><path ${S} d="M3.5 12h17M12 3.5c-5.6 5.4-5.6 11.6 0 17M12 3.5c5.6 5.4 5.6 11.6 0 17"/>`,
  chat: `<path ${S} d="M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v7a2.5 2.5 0 0 1-2.5 2.5H12l-4.5 4v-4h-1A2.5 2.5 0 0 1 4 13.5z"/>`,
  deck: `<circle cx="7" cy="7" r="1.6" fill="currentColor"/><circle cx="12" cy="7" r="1.6" fill="currentColor"/><circle cx="17" cy="7" r="1.6" fill="currentColor"/><circle cx="7" cy="12" r="1.6" fill="currentColor"/><circle cx="12" cy="12" r="1.6" fill="currentColor"/><circle cx="17" cy="12" r="1.6" fill="currentColor"/><circle cx="7" cy="17" r="1.6" fill="currentColor"/><circle cx="12" cy="17" r="1.6" fill="currentColor"/><circle cx="17" cy="17" r="1.6" fill="currentColor"/>`,
  music: `<path ${S} d="M9 18V6l10-2v11.5"/><circle ${S} cx="6.6" cy="18" r="2.4"/><circle ${S} cx="16.6" cy="15.5" r="2.4"/>`,
  tone: `<path ${S} d="M3 12h3l2.5-6 3.5 12 3-9 1.5 3H21"/>`,
  file: `<path ${S} d="M7 3h7l4 4v12a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path ${S} d="M14 3v4h4"/>`,
  system: `<rect ${S} x="3" y="4.5" width="18" height="12.5" rx="2"/><path ${S} d="M8 21h8M12 17v4"/><path ${S} d="M8.8 9.5a4.2 4.2 0 0 1 6.4 0" opacity=".8"/>`,
  headphones: `<path ${S} d="M4.5 14v-2a7.5 7.5 0 0 1 15 0v2"/><rect ${S} x="3.5" y="13.5" width="4" height="6.5" rx="1.8"/><rect ${S} x="16.5" y="13.5" width="4" height="6.5" rx="1.8"/>`,
  broadcast: `<circle cx="12" cy="12" r="1.9" fill="currentColor"/><path ${S} d="M8.2 15.8a5.4 5.4 0 0 1 0-7.6M15.8 8.2a5.4 5.4 0 0 1 0 7.6M5.4 18.6a9.4 9.4 0 0 1 0-13.2M18.6 5.4a9.4 9.4 0 0 1 0 13.2"/>`,
  people: `<circle ${S} cx="9" cy="8.5" r="3.2"/><path ${S} d="M3.5 19.5a5.5 5.5 0 0 1 11 0"/><circle ${S} cx="16.5" cy="9.5" r="2.6"/><path ${S} d="M15.5 14.6a5 5 0 0 1 5 4.9"/>`,
  vod: `<circle cx="12" cy="11" r="2" fill="currentColor"/><path ${S} d="M8.5 14.5a4.8 4.8 0 0 1 0-7M15.5 7.5a4.8 4.8 0 0 1 0 7M6 17a8.2 8.2 0 0 1 0-12M18 5a8.2 8.2 0 0 1 0 12M12 13.5V21"/>`,
  speaker: `<path ${S} d="M4 9.5v5h3.5L12 18.5v-13L7.5 9.5z"/><path ${S} d="M15 9.5a3.5 3.5 0 0 1 0 5M17.5 7a7 7 0 0 1 0 10"/>`,
  speakerbox: `<rect ${S} x="6" y="3.5" width="12" height="17" rx="2"/><circle ${S} cx="12" cy="14.5" r="3.2"/><circle cx="12" cy="8" r="1.5" fill="currentColor"/>`,
  sliders: `<path ${S} d="M5 4v6m0 4v6M12 4v10m0 4v2M19 4v2m0 4v10"/><circle ${S} cx="5" cy="12" r="2"/><circle ${S} cx="12" cy="16" r="2"/><circle ${S} cx="19" cy="8" r="2"/>`,
  flow: `<circle ${S} cx="5.5" cy="6" r="2.2"/><circle ${S} cx="5.5" cy="18" r="2.2"/><circle ${S} cx="18.5" cy="12" r="2.2"/><path ${S} d="M7.7 6.6c4 1 4.6 3.6 8.6 4.6M7.7 17.4c4-1 4.6-3.6 8.6-4.6"/>`,
  settings: `<circle ${S} cx="12" cy="12" r="3"/><path ${S} d="M12 2.8v2.4M12 18.8v2.4M2.8 12h2.4M18.8 12h2.4M5.5 5.5l1.7 1.7M16.8 16.8l1.7 1.7M18.5 5.5l-1.7 1.7M7.2 16.8l-1.7 1.7"/>`,
  plus: `<path ${S} d="M12 5v14M5 12h14"/>`,
  x: `<path ${S} d="M6 6l12 12M18 6L6 18"/>`,
  record: `<circle cx="12" cy="12" r="6" fill="currentColor"/>`,
  stop: `<rect x="7" y="7" width="10" height="10" rx="1.5" fill="currentColor"/>`,
  chevron: `<path ${S} d="M8.5 5.5L15 12l-6.5 6.5"/>`,
  warn: `<path ${S} d="M12 4L2.8 19.5h18.4z"/><path ${S} d="M12 10v4.2"/><circle cx="12" cy="16.8" r="1" fill="currentColor"/>`,
  power: `<path ${S} d="M12 3v8M6.3 6.5a8 8 0 1 0 11.4 0"/>`,
  waveform: `<path fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" d="M2.5 12h1.8M6 12V9.5M8 12V6.5M10 12V3.8M12 12V8M14 12V5M16 12V9M18 12V7.2M20 12h1.5M6 12v2.5M8 12v5.5M10 12v8.2M12 12v4M14 12v7M16 12v3M18 12v4.8"/>`,
};

export function icon(name, cls = '') {
  return `<svg class="ic ${cls}" viewBox="0 0 24 24" aria-hidden="true">${icons[name] || icons.tone}</svg>`;
}

// Tile colors for software-style input icons (generic, hand-picked — no third-party brand assets)
export const tileColors = {
  mic: '#4a6cf0', line: '#565a63', capture: '#3a7bd5', browser: '#e8823a',
  chat: '#5865b8', deck: '#7b5bd6', music: '#1fb35b', tone: '#c2495e',
  file: '#3aa6a0', system: '#4a90d9', headphones: '#565a63', broadcast: '#565a63',
  people: '#565a63', vod: '#565a63', speaker: '#565a63', speakerbox: '#565a63',
};
