// Opening: 0 → 1 while the hero scrolls by; the CSS turns the mockup into the bar photo with it.
const hero = document.getElementById('hero');
function onScroll() {
  const range = hero.offsetHeight - window.innerHeight;
  const p = range > 0 ? Math.min(1, Math.max(0, -hero.getBoundingClientRect().top / range)) : 0;
  hero.style.setProperty('--p', p.toFixed(3));
}
window.addEventListener('scroll', onScroll, { passive: true });
window.addEventListener('resize', onScroll);
onScroll();

// Light / dark comparison
for (const compare of document.querySelectorAll('.compare')) {
  const input = compare.querySelector('input');
  input.addEventListener('input', () => compare.style.setProperty('--pos', input.value + '%'));
}

// Highlight the visitor's system. Both Mac buttons: a browser cannot tell Apple Silicon from Intel.
const ua = navigator.userAgent;
const os = /Android|iPhone|iPad/.test(ua) ? null
  : /Mac/.test(ua) ? 'mac' : /Win/.test(ua) ? 'windows' : /Linux/.test(ua) ? 'linux' : null;
if (os) {
  for (const a of document.querySelectorAll(`.dl[data-os="${os}"]`)) a.classList.add('mine');
}

// First-start help: opens after a download starts, or from the link; one tab per system.
const help = document.getElementById('help');
const title = help.querySelector('.help-title');
function showTab(name) {
  for (const b of help.querySelectorAll('[data-tab]')) b.setAttribute('aria-selected', String(b.dataset.tab === name));
  for (const p of help.querySelectorAll('[data-panel]')) p.hidden = p.dataset.panel !== name;
}
for (const b of help.querySelectorAll('[data-tab]')) b.addEventListener('click', () => showTab(b.dataset.tab));
for (const a of document.querySelectorAll('.dl')) {
  a.addEventListener('click', () => {
    title.textContent = 'Your download has started';
    showTab(a.dataset.help);
    help.showModal();
  });
}
document.querySelector('[data-open-help]').addEventListener('click', () => {
  title.textContent = 'Opening the app the first time';
  showTab(os || 'mac');
  help.showModal();
});
// Song tools
const tools = document.getElementById('tools');
document.querySelector('[data-open-tools]').addEventListener('click', () => tools.showModal());

// A click on the dark backdrop closes a popup too.
for (const dialog of [help, tools]) {
  dialog.addEventListener('click', e => {
    const r = dialog.getBoundingClientRect();
    if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) dialog.close();
  });
}
