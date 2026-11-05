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
  console.log(`== ${mode}: state=${s && s.state} z=${s && s.z} -> ${s2} ${moving ? 'moving' : 'NOT MOVING'} y=${s && s.y} speed=${s && s.speed}`);
  const shown = [...new Set(problems)].slice(0, 25);
  shown.forEach(p => console.log('   ' + p.split('\n').slice(0, 6).join('\n      ')));
  if (problems.some(p => p.startsWith('PAGE ERROR')) || !moving) failed = true;
  await page.close();
}
await browser.close();
console.log(failed ? 'RESULT: PROBLEMS FOUND' : 'RESULT: ALL MODES RUN');
