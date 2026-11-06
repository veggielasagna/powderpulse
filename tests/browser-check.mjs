// Real-browser check: loads the game in headless Chromium with the real three.js,
// presses each mode button, and reports page errors, console errors and game state.
// Screenshots go to tests/shots/.
import { chromium } from 'playwright';
import { mkdirSync } from 'fs';

const base = process.env.GAME_URL || 'http://localhost:8000/index.html';
mkdirSync('tests/shots', { recursive: true });

const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required'] });
let failed = false;

for (const [mode, button, viewport] of [
  ['skate-phone', '#btnSkate', { width: 390, height: 844 }],
  ['skate-tv', '#btnSkate', { width: 1280, height: 720 }],
  ['free', '#btnFree', { width: 1280, height: 720 }],
  ['run', '#btnStart', { width: 1280, height: 720 }],
]) {
  const page = await browser.newPage({ viewport });
  const problems = [];
  page.on('pageerror', e => problems.push('PAGE ERROR: ' + (e.stack || e.message)));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') problems.push('CONSOLE ' + m.type() + ': ' + m.text()); });
  await page.goto(base);
  await page.waitForTimeout(2500);
  await page.click(button);
  await page.waitForTimeout(6000);
  const s = await page.evaluate(() => { const p = window.__pp; return p ? { state: p.state, z: +p.P.z.toFixed(1), y: +p.P.y.toFixed(2), speed: +p.P.speed.toFixed(1) } : null; });
  await page.screenshot({ path: `tests/shots/${mode}.png` });
  await page.waitForTimeout(1500);
  const s2 = await page.evaluate(() => { const p = window.__pp; return p ? +p.P.z.toFixed(1) : null; });
  const moving = s && s2 !== null && s2 !== s.z;
  // Tricky mode: charge the meter, press T, and make sure it runs and speeds things up
  const tricky = await page.evaluate(async () => {
    const p = window.__pp; if(!p || !p.Tricky) return 'missing';
    p.Tricky.meter = 6;
    document.dispatchEvent(new KeyboardEvent('keydown', { code:'KeyT', bubbles:true })); window.dispatchEvent(new KeyboardEvent('keydown', { code:'KeyT' }));
    window.dispatchEvent(new KeyboardEvent('keyup', { code:'KeyT' }));
    await new Promise(r => setTimeout(r, 1200));
    return p.Tricky.on ? `on, ${p.Tricky.left.toFixed(1)}s left, meter ${p.Tricky.meter}` : `NOT ON (state ${p.state}, meter ${p.Tricky.meter})`;
  });
  if(String(tricky).startsWith('NOT') || tricky === 'missing') failed = true;
  console.log(`   tricky: ${tricky}`);
  console.log(`== ${mode}: state=${s && s.state} z=${s && s.z} -> ${s2} ${moving ? 'moving' : 'NOT MOVING'} y=${s && s.y} speed=${s && s.speed}`);
  const shown = [...new Set(problems)].slice(0, 25);
  shown.forEach(p => console.log('   ' + p.split('\n').slice(0, 6).join('\n      ')));
  if (problems.some(p => p.startsWith('PAGE ERROR')) || !moving) failed = true;
  await page.close();
}
// Options: turning the meter off hides it and is remembered after a reload
{
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const problems = [];
  page.on('pageerror', e => problems.push('PAGE ERROR: ' + (e.stack || e.message)));
  await page.goto(base); await page.waitForTimeout(2000);
  await page.click('#btnOptions'); await page.waitForTimeout(300);
  await page.screenshot({ path: 'tests/shots/options.png' });
  await page.click('#optTricky'); await page.waitForTimeout(200);
  const label = await page.textContent('#optTricky');
  await page.reload(); await page.waitForTimeout(2000);
  const after = await page.evaluate(() => window.__pp.Tricky.enabled);
  console.log(`== options: switch shows ${label}, after reload enabled=${after}`);
  problems.forEach(p => console.log('   ' + p.split('\n').slice(0,6).join('\n      ')));
  if(label !== 'OFF' || after !== false || problems.length) failed = true;
  await page.close();
}
// Trees: off from the title, ride Free Ride with none, then back on from the pause menu mid-ride
{
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const problems = [];
  page.on('pageerror', e => problems.push('PAGE ERROR: ' + (e.stack || e.message)));
  await page.goto(base); await page.waitForTimeout(2000);
  await page.click('#btnOptions'); await page.waitForTimeout(200);
  await page.click('#optTrees'); await page.waitForTimeout(400);
  const titleTrees = await page.evaluate(() => window.__pp.course.trees.length);
  await page.click('#btnOptionsBack'); await page.click('#btnFree'); await page.waitForTimeout(4000);
  const rideTrees = await page.evaluate(() => window.__pp.course.trees.length);
  await page.screenshot({ path: 'tests/shots/no-trees.png' });
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  await page.click('#btnPauseOptions'); await page.waitForTimeout(200);
  await page.click('#optTrees'); await page.waitForTimeout(500);
  await page.click('#btnOptionsBack'); await page.click('#btnResume'); await page.waitForTimeout(1500);
  const back = await page.evaluate(() => ({ trees: window.__pp.course.trees.length, state: window.__pp.state, z: window.__pp.P.z }));
  await page.waitForTimeout(800);
  const z2 = await page.evaluate(() => window.__pp.P.z);
  console.log(`== trees: off -> title ${titleTrees}, free ride ${rideTrees}; back on mid-ride -> ${back.trees} trees, state ${back.state}, ${z2 > back.z ? 'moving' : 'NOT MOVING'}`);
  problems.forEach(p => console.log('   ' + p.split('\n').slice(0,6).join('\n      ')));
  if(titleTrees !== 0 || rideTrees !== 0 || back.trees === 0 || !(z2 > back.z) || problems.length) failed = true;
  await page.close();
}
// Shuffle switch: on by default, turning it off is remembered after a reload
{
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const problems = [];
  page.on('pageerror', e => problems.push('PAGE ERROR: ' + (e.stack || e.message)));
  await page.goto(base); await page.waitForTimeout(2000);
  const before = await page.getAttribute('#btnShuffle', 'aria-pressed');
  await page.evaluate(() => document.getElementById('btnShuffle').click()); await page.waitForTimeout(200);
  const label = await page.textContent('#btnShuffle');
  await page.reload(); await page.waitForTimeout(2000);
  const after = await page.getAttribute('#btnShuffle', 'aria-pressed');
  console.log(`== shuffle: default ${before}, switched to "${label}", after reload ${after}`);
  problems.forEach(p => console.log('   ' + p.split('\n').slice(0,6).join('\n      ')));
  if(before !== 'true' || after !== 'false' || !/OFF/.test(label) || problems.length) failed = true;
  await page.close();
}
// Menu navigation with a controller: the music screen opens on the songs, up/down walk the list,
// LT/RT jump a page, LB/RB switch tabs, and up from the list reaches the search box and tabs
{
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const problems = [];
  page.on('pageerror', e => problems.push('PAGE ERROR: ' + (e.stack || e.message)));
  await page.goto(base); await page.waitForTimeout(2000);
  const r = await page.evaluate(async () => {
    const m = window.__pp.menu, wait = ms => new Promise(r => setTimeout(r, ms));
    const songs = Array.from({ length: 300 }, (_, i) => ({ k: 'k' + i, t: 'Song ' + String(i).padStart(3, '0'), a: 'Artist ' + (i % 12), l: 'Album ' + (i % 30), g: i % 2 ? 'Rock' : 'Jazz', y: 1990 + (i % 30), n: i % 12 + 1 }));
    m.setLibrary(songs, 'nas');
    m.pad(true);
    document.getElementById('btnMusic').click(); await wait(300);
    const label = el => !el ? 'nothing' : el.classList.contains('lrow') ? 'row ' + el.dataset.r : (el.id || el.dataset.tab || el.textContent.trim().slice(0, 20));
    const out = { start: label(m.focused) };
    m.navMove('down'); m.navMove('down'); out.down2 = label(m.focused);
    m.libPage(1); out.page = label(m.focused);
    m.libPage(-1); m.libPage(-1); out.pageBack = label(m.focused);
    m.navMove('up'); out.upOut = label(m.focused);
    m.navMove('up'); out.up2 = label(m.focused);
    m.navMove('right'); out.right = label(m.focused);
    m.navMove('down'); m.navMove('down'); out.backDown = label(m.focused);
    m.libTabStep(1); out.tab = m.tab + ' / ' + label(m.focused);
    m.navMove('down'); out.tabRow = label(m.focused);
    return out;
  });
  await page.screenshot({ path: 'tests/shots/music-nav.png' });
  console.log('== menu nav: ' + JSON.stringify(r));
  problems.forEach(p => console.log('   ' + p.split('\n').slice(0,6).join('\n      ')));
  const ok = r.start === 'row 0' && r.down2 === 'row 2' && r.page.startsWith('row ') && +r.page.slice(4) > 4 && r.pageBack === 'row 0'
    && r.upOut === 'libSearch' && r.tab.startsWith('artists') && r.tabRow === 'row 1' && !problems.length;
  if(!ok) failed = true;
  await page.close();
}
await browser.close();
console.log(failed ? 'RESULT: PROBLEMS FOUND' : 'RESULT: ALL MODES RUN');
