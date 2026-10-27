// PredatorCore spot A: the cooling fan. Cut cream blades in a brass ring on a vermilion plate, over a teal
// disc, with warm sparks thrown off as it spins. Transparent background.
// Render: node render.mjs spot-fan.js,plants.js spot-fan.png 1600 1600
function draw(ctx, W, H, K) {
  ctx.save();
  ctx.scale(W / 1600, H / 1600);
  // ICON (set by icon-flag.js): fan and disc only, filling the square, for the app icon
  const icon = typeof ICON !== 'undefined' && ICON;
  if (icon) { ctx.translate(800, 800); ctx.scale(1.5, 1.5); ctx.translate(-940, -608); }
  const P = Plants(ctx, K), TAU = Math.PI * 2, ink = '#1b1a17';
  P.disc(960, 600, 470, INKS.teal, 801);

  const fx = 800, fy = 690, R = 350;

  // air thrown off the rim: short cream strokes outside the ring, leaning with the spin
  const ar = K.rng(802);
  for (let i = 0; i < (icon ? 0 : 26); i++) {
    const a = ar() * TAU, r0 = R + 70 + ar() * 60, L = 40 + ar() * 70, sw = 0.12;
    P.line([[fx + Math.cos(a) * r0, fy + Math.sin(a) * r0], [fx + Math.cos(a + sw) * (r0 + L), fy + Math.sin(a + sw) * (r0 + L)]], ar() < 0.7 ? INKS.cream[0] : INKS.mustard[0], 3 + ar() * 2);
  }

  // blades: the mark's own burst (10 rays, seed 4), swirled so every ray leans into the spin
  const B = K.burst(fx, fy, 1.3, { seed: 4, rays: 10, from: 0, to: 324 });
  const swirl = (pts) => pts.map(([x, y]) => {
    const dx = x - fx, dy = y - fy, d = Math.hypot(dx, dy), t = 0.62 * Math.pow(d / 285, 1.35);
    const c = Math.cos(t), s = Math.sin(t);
    return [fx + dx * c - dy * s, fy + dx * s + dy * c];
  });
  // widen each ray about its own axis first, so the blades carry more paper
  const widen = (pts, a) => pts.map(([x, y]) => {
    const dx = x - fx, dy = y - fy, d = Math.hypot(dx, dy);
    let th = Math.atan2(dy, dx) - a;
    th = Math.atan2(Math.sin(th), Math.cos(th)) * 1.55 + a;
    return [fx + Math.cos(th) * d, fy + Math.sin(th) * d];
  });
  const blades = B.rays.map((o, i) => ({ a: B.axes[i].a, o: swirl(widen(o, B.axes[i].a)) }));
  const hub = K.roughen(K.blob(fx, fy, 86, 86, { seed: 830, rough: 0.04 }), 1.4, { seed: 831, spacing: 3 });
  const ring = K.blob(fx, fy, R, R, { seed: 832, rough: 0.012 });

  ctx.save();
  ctx.translate(20, 14);
  P.edge(ring, INKS.verm, 40);
  ctx.restore();
  // the housing behind the blades: a deep teal well, so the fan reads as one solid object
  const well = K.roughen(K.blob(fx, fy, R - 14, R - 14, { seed: 833, rough: 0.012 }), 1.2, { seed: 834, spacing: 3 });
  K.fill(ctx, well, '#0b3646');
  K.flow(ctx, well, { ink: '#16556a', per: 0.006, len: 40, turn: 0.15, freq: 0.002, width: 1.4, seed: 835, alpha: 0.8 });
  K.stipple(ctx, well, { ink: '#000000', per: 0.004, rMin: 1, rMax: 2.4, seed: 836, density: (x, y) => Math.max(0, Math.hypot(x - fx, y - fy) / R - 0.6) * 2.5 });

  // the vermilion plate, printed out of register under everything that is cut
  ctx.save();
  ctx.translate(20, 14);
  for (const b of blades) K.fill(ctx, b.o, INKS.verm);
  K.fill(ctx, hub, INKS.verm);
  ctx.restore();

  for (const b of blades) {
    K.fill(ctx, b.o, INKS.cream[0]);
    K.hatch(ctx, b.o, { ink: '#c9822a', gap: 6, width: 1.3, angle: b.a + 0.45, dash: [30, 140], lift: 0.15, wave: 1.5, seed: 840 + Math.round(b.a * 10), alpha: 0.65 });
    K.stipple(ctx, b.o, { ink: ink, per: 0.0012, rMin: 0.9, rMax: 2, seed: 850 + Math.round(b.a * 10) });
    P.edge(b.o, INKS.cream[1], 1.4);
  }

  // hub: the core, maroon with pleats and a cream cap
  K.fill(ctx, hub, INKS.maroon[0]);
  K.radial(ctx, fx, fy, { clip: hub, count: 120, r0: 0, r1: 100, ink: INKS.maroon[1], width: 1.4, bend: 0.3, seed: 860, alpha: 0.9 });
  const cap = K.roughen(K.blob(fx, fy, 30, 30, { seed: 861, rough: 0.05 }), 1, { seed: 862, spacing: 2 });
  K.fill(ctx, cap, INKS.cream[0]);
  P.line([[fx - 14, fy], [fx + 14, fy]], ink, 5);
  P.line([[fx, fy - 14], [fx, fy + 14]], ink, 5);

  // brass ring of the housing
  P.edge(ring, INKS.mustard[0], 40);
  ctx.save();
  ctx.lineWidth = 2; ctx.strokeStyle = INKS.mustard[1];
  for (const k of [-14, 14]) {
    ctx.stroke(K.path(K.blob(fx, fy, R + k, R + k, { seed: 832, rough: 0.012 })));
  }
  ctx.restore();

  // sparks: warm seeds flung outward, each with a black under-plate
  const sr = K.rng(870);
  for (let i = 0; i < (icon ? 0 : 11); i++) {
    const a = -1.2 + i * 0.55 + (sr() - 0.5) * 0.3, rr = R + 120 + sr() * 120;
    const x = fx + Math.cos(a) * rr, y = fy + Math.sin(a) * rr;
    if (x < 70 || x > 1530 || y < 70 || y > 1160) continue;
    const s = K.blob(x, y, 20 + sr() * 6, 12 + sr() * 3, { seed: 880 + i, rough: 0.08, rot: a + Math.PI / 2 + 0.4 });
    ctx.save(); ctx.translate(7, 6); K.fill(ctx, s, ink); ctx.restore();
    K.fill(ctx, s, INKS.mustard[0]);
  }

  if (!icon) {
    P.wheel(520, 1290, 230, INKS.bottle, 0.55, 891);
    P.wheel(1150, 1300, 210, INKS.green, 0.55, 892);
    P.wheel(840, 1340, 200, INKS.sage, 0.5, 893);
  }
  ctx.restore();
  K.grain(ctx, W, H, { amount: 13, linen: 5, seed: 899 });
}
