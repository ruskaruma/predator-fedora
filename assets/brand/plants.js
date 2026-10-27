// Shared plants for the square spot illustrations. Load with a scene:
//   node render.mjs spot-runtime.js,plants.js out.png 1600 1600
const INKS = {
  black: '#000000', ultra: ['#3a43aa', '#6f78d8'], cobalt: ['#2445b0', '#5f7fe0'], teal: ['#14526f', '#3f86a8'],
  mustard: ['#e3a52a', '#8a5a10'], verm: '#ee3b05', rose: ['#c6354a', '#6f1626'], poppy: ['#d02d2a', '#7a1414'],
  pink: ['#ea538f', '#9c2357'], violet: ['#8a4fd0', '#4b2580'], lilac: ['#b9a6e8', '#5e4b99'],
  cream: ['#ebe5c8', '#8f8463'], white: ['#f4f1e6', '#8e897d'], green: ['#23805a', '#0b3a2b'],
  bottle: ['#0f4935', '#2f7a5c'], sage: ['#6f8f3a', '#34481a'], maroon: ['#7d1d2a', '#3c0a12'],
};

const Plants = (ctx, K) => {
  const TAU = Math.PI * 2;
  const line = (pts, ink, w) => {
    ctx.strokeStyle = ink; ctx.lineWidth = w; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    ctx.stroke(K.path(pts, false));
  };
  const edge = (pts, ink, w = 1.2) => { ctx.strokeStyle = ink; ctx.lineWidth = w; ctx.lineJoin = 'round'; ctx.stroke(K.path(pts)); };
  const stem = (x0, y0, x1, y1, bend, ink, w) => {
    const pts = [];
    for (let i = 0; i <= 30; i++) { const t = i / 30; pts.push([K.lerp(x0, x1, t) + bend * Math.sin(Math.PI * t), K.lerp(y0, y1, t)]); }
    line(pts, ink, w);
    return pts;
  };

  // The coloured disc behind a spot: pulled strokes, stipple thickening toward the rim.
  const disc = (cx, cy, r, p, seed) => {
    const d = K.roughen(K.blob(cx, cy, r, r, { seed, rough: 0.04 }), 3, { seed: seed + 1 });
    K.fill(ctx, d, p[0]);
    K.flow(ctx, d, { ink: p[1], per: 0.006, len: 30, turn: 0.3, freq: 0.002, width: 1.3, seed: seed + 2, alpha: 0.7 });
    K.stipple(ctx, d, { ink: INKS.black, per: 0.006, rMin: 1, rMax: 2.6, seed: seed + 3, density: (x, y) => Math.max(0, Math.hypot(x - cx, y - cy) / r - 0.55) * 2.2 });
  };

  // Lupin leaf: eleven round-tipped leaflets in a wheel, seen at a tilt.
  const wheel = (x, y, r, p, tilt, seed) => {
    ctx.save();
    ctx.translate(x, y);
    ctx.scale(1, tilt);
    for (const l of K.palmate(0, 0, { lobes: 11, len: r, wid: r * 0.4, dir: -Math.PI / 2, spread: TAU * (10 / 11), even: true, droop: 0, tip: 0.9, seed })) {
      const o = K.roughen(l.outline, 1, { seed: seed + 3 });
      K.fill(ctx, o, p[0]);
      const m = l.mid, a = Math.atan2(m[m.length - 1][1] - m[0][1], m[m.length - 1][0] - m[0][0]);
      K.hatch(ctx, o, { ink: p[1], gap: 5, width: 1, angle: a + 1.1, dash: [16, 50], lift: 0.05, wave: 0.6, seed: seed + 9, alpha: 0.7 });
      edge(o, p[1], 1.3);
      line(m.slice(2), p[1], 2);
    }
    ctx.restore();
  };

  // Lupin spike: loose two-tone pea-flowers thinning toward the tip.
  // `bare` is the share of the stem below the first flower.
  const spike = (x, base, h, p, banner, seed, s0 = 34, bare = 0.5) => {
    const r = K.rng(seed), top = base - h, sway = (r() - 0.5) * 90;
    const cxAt = (y) => x + sway * ((base - y) / h) ** 2;
    const st = [];
    for (let i = 0; i <= 30; i++) { const y = K.lerp(base, top, i / 30); st.push([cxAt(y), y]); }
    line(st, INKS.bottle[0], 10);
    const levels = 34;
    for (let i = levels - 1; i >= 0; i--) {
      const t = i / (levels - 1), y = K.lerp(base - h * bare, top + 14, t), s = K.lerp(s0, s0 * 0.3, t) * (0.8 + 0.4 * r());
      const n = t > 0.86 ? 1 : 2 + Math.floor(r() * 2);
      for (let k = 0; k < n; k++) {
        const side = n === 1 ? 0 : (k / (n - 1)) * 2 - 1;
        const px = cxAt(y) + side * s * 1.1 + (r() - 0.5) * s * 0.4, py = y + (r() - 0.5) * s * 0.5;
        const pt = K.blob(px, py, s * (0.7 + 0.25 * r()), s * (0.5 + 0.2 * r()), { seed: seed + i * 7 + k, rough: 0.25, rot: side * 0.6 + (r() - 0.5) * 0.4 });
        const ink = r() < 0.32 ? banner : p;
        K.fill(ctx, pt, ink[0]);
        K.radial(ctx, cxAt(y), y + s * 0.6, { clip: pt, count: 10, r0: 0, r1: s * 2, a0: -Math.PI * 0.9, a1: -Math.PI * 0.1, ink: ink[1], width: 1, seed: seed + i, alpha: 0.9 });
        edge(pt, ink[1], 1.1);
      }
    }
  };

  // Passion flower, face-on: two rings of striated petals, a three-band corona, anthers and styles.
  const passion = (x, y, R, tilt, rot, petal, corona, seed, plate) => {
    const r = K.rng(seed), petals = [];
    for (let ring = 0; ring < 2; ring++) {
      for (let i = 0; i < 5; i++) {
        const a = (i / 5) * TAU + ring * (TAU / 10) + (r() - 0.5) * 0.12;
        const l = K.lobe(0, 0, a, R * (ring ? 0.9 : 1), R * 0.5, { tip: 0.8, base: 0.4, bend: (r() - 0.5) * 0.2 });
        petals.push({ a, ring, o: K.roughen(l.outline, 2, { seed: seed + i + ring * 9, spacing: 5 }) });
      }
    }
    ctx.save();
    ctx.translate(x, y);
    ctx.scale(1, tilt);
    ctx.rotate(rot);
    if (plate) {
      ctx.save();
      ctx.translate(18, 12);
      for (const p of petals) K.fill(ctx, p.o, plate);
      ctx.restore();
    }
    for (const p of petals) {
      K.fill(ctx, p.o, p.ring ? petal[0] : petal[2] || petal[0]);
      K.radial(ctx, 0, 0, { clip: p.o, count: 70, r0: R * 0.08, r1: R * 1.1, a0: p.a - 0.4, a1: p.a + 0.4, ink: petal[1], width: 1.1, bend: 0.04, seed: seed + 3, alpha: 0.7 });
      edge(p.o, petal[1], 1.1);
    }
    const n = 170;
    for (let i = 0; i < n; i++) {
      const a = (i / n) * TAU + (r() - 0.5) * 0.03, L = R * (0.66 + 0.14 * r()), pts = [];
      for (let s = 0; s <= 18; s++) {
        const t = s / 18, rr = R * 0.15 + (L - R * 0.15) * t, wob = Math.sin(t * 11 + i * 1.7) * 0.018 * t;
        pts.push([Math.cos(a + wob) * rr, Math.sin(a + wob) * rr]);
      }
      line(pts.slice(0, 7), corona[0], 3.2);
      line(pts.slice(6, 12), corona[1], 3);
      line(pts.slice(11), corona[2], 2.8);
    }
    const d = K.blob(0, 0, R * 0.15, R * 0.15, { seed, rough: 0.08 });
    K.fill(ctx, d, INKS.sage[0]);
    K.stipple(ctx, d, { ink: INKS.sage[1], per: 0.03, rMin: 1, rMax: 2, seed: seed + 4 });
    for (let i = 0; i < 5; i++) {
      const a = (i / 5) * TAU + 0.3, ex = Math.cos(a) * R * 0.26, ey = Math.sin(a) * R * 0.26;
      line([[0, 0], [ex, ey]], INKS.sage[0], 7);
      ctx.save();
      ctx.translate(ex, ey);
      ctx.rotate(a + Math.PI / 2);
      K.fill(ctx, K.blob(0, 0, R * 0.075, R * 0.035, { seed: seed + i, rough: 0.05 }), INKS.mustard[0]);
      ctx.restore();
    }
    for (let i = 0; i < 3; i++) {
      const a = (i / 3) * TAU - 0.4, ex = Math.cos(a) * R * 0.17, ey = Math.sin(a) * R * 0.17;
      line([[0, 0], [ex, ey]], INKS.violet[1], 5);
      K.fill(ctx, K.blob(ex, ey, 9, 9, { seed: seed + 20 + i, rough: 0.05 }), INKS.violet[1]);
    }
    ctx.restore();
  };

  return { line, edge, stem, disc, wheel, spike, passion };
};
