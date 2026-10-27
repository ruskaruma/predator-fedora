// PredatorCore mark: the skill's burst (seed 4, 10 rays, full circle), each ray widened about its axis and
// swirled into a fan blade, around a coloured hub. node mark.mjs <out.svg> [hub=#4fc3dc] [blades=#fff]
import { writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { homedir } from 'node:os';
const require = createRequire(import.meta.url);
require(homedir() + '/.claude/skills/new-horizon-style/scripts/print-kit.js');
const K = globalThis.PrintKit;
const [out, hub = '#4fc3dc', fill = '#ffffff'] = process.argv.slice(2);
const C = 256, B = K.burst(C, C, 1.02, { seed: 4, rays: 10, from: 0, to: 324 });
const widen = (pts, a) => pts.map(([x, y]) => {
  const dx = x - C, dy = y - C, d = Math.hypot(dx, dy);
  let th = Math.atan2(dy, dx) - a; th = Math.atan2(Math.sin(th), Math.cos(th)) * 1.55 + a;
  return [C + Math.cos(th) * d, C + Math.sin(th) * d];
});
const swirl = (pts) => pts.map(([x, y]) => {
  const dx = x - C, dy = y - C, d = Math.hypot(dx, dy), t = 0.62 * Math.pow(d / 240, 1.35), c = Math.cos(t), s = Math.sin(t);
  return [C + dx * c - dy * s, C + dx * s + dy * c];
});
const d = (shapes) => shapes.map((s) => 'M' + s.map(([x, y]) => `${x.toFixed(1)} ${y.toFixed(1)}`).join('L') + 'Z').join('');
const rays = B.rays.map((o, i) => swirl(widen(o, B.axes[i].a)));
const disc = K.roughen(K.blob(C, C, 60, 60, { seed: 103, rough: 0.05, steps: 90 }), 1.2, { seed: 11, spacing: 3 });
writeFileSync(out, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="PredatorCore"><path fill="${fill}" d="${d(rays)}"/><path fill="${hub}" d="${d([disc])}"/></svg>\n`);
