// PredatorCore spot C: the mode dial. A knurled cream knob on a vermilion plate, five cut ticks around it
// from Eco to Turbo, on a plum disc. Transparent background.
// Render: node render.mjs spot-dial.js,plants.js spot-dial.png 1600 1600
function draw(ctx, W, H, K) {
  ctx.save();
  ctx.scale(W / 1600, H / 1600);
  const P = Plants(ctx, K), TAU = Math.PI * 2, ink = '#1b1a17';
  const plum = ['#5b2a86', '#8a5fc0'];
  P.disc(920, 610, 470, plum, 601);

  const kx = 800, ky = 690, R = 250;

  // the five modes as cut ticks on an arc, cool to hot: Eco, Quiet, Balanced, Performance, Turbo
  const modes = [INKS.sage, INKS.teal, INKS.cream, INKS.mustard, ['#ee3b05', '#7a1414']];
  const a0 = Math.PI * 0.8, a1 = Math.PI * 2.2;
  modes.forEach((m, i) => {
    const a = K.lerp(a0, a1, i / (modes.length - 1)), long = i === 4;
    const r0 = R + 62, r1 = R + (long ? 210 : 165), w0 = 16, w1 = long ? 34 : 28, ca = Math.cos(a), sa = Math.sin(a);
    const at = (al, sd) => [kx + ca * al - sa * sd, ky + sa * al + ca * sd];
    const o = K.roughen([at(r0, -w0), at(r1 - 8, -w1), at(r1 + 8, w1), at(r0, w0)], 1.6, { seed: 610 + i, spacing: 3 });
    ctx.save(); ctx.translate(12, 9); K.fill(ctx, o, ink); ctx.restore();
    K.fill(ctx, o, m[0]);
    K.hatch(ctx, o, { ink: m[1], gap: 5, width: 1.1, angle: a, dash: [20, 80], lift: 0.2, seed: 620 + i, alpha: 0.6 });
  });
  // small dots between the ticks
  for (let i = 0; i < 17; i++) {
    const a = K.lerp(a0, a1, i / 16);
    if (i % 4 === 0) continue;
    K.fill(ctx, K.blob(kx + Math.cos(a) * (R + 92), ky + Math.sin(a) * (R + 92), 8, 8, { seed: 630 + i, rough: 0.1 }), INKS.cream[0]);
  }

  // knob: knurled rim, brushed face, a pointer groove set to Quiet
  const teeth = [];
  for (let i = 0; i < 240; i++) {
    const a = (i / 240) * TAU, k = i % 8 < 4 ? 1 : 0.955;
    teeth.push([kx + Math.cos(a) * R * k, ky + Math.sin(a) * R * k]);
  }
  const rim = K.roughen(teeth, 1.4, { seed: 641, spacing: 3 });
  ctx.save(); ctx.translate(22, 16); K.fill(ctx, rim, INKS.verm); ctx.restore();
  K.fill(ctx, rim, INKS.mustard[0]);
  K.radial(ctx, kx, ky, { clip: rim, count: 260, r0: R * 0.8, r1: R * 1.05, ink: INKS.mustard[1], width: 1.4, bend: 0, seed: 642, alpha: 0.8 });
  const face = K.roughen(K.blob(kx, ky, R * 0.8, R * 0.8, { seed: 643, rough: 0.02 }), 1.2, { seed: 644, spacing: 3 });
  K.fill(ctx, face, INKS.cream[0]);
  ctx.save();
  ctx.strokeStyle = INKS.cream[1]; ctx.globalAlpha = 0.55;
  const fr = K.rng(645);
  for (let r = 14; r < R * 0.8; r += 7 + fr() * 4) {
    ctx.lineWidth = 0.8 + fr() * 0.9;
    const s = fr() * TAU, e = s + 1 + fr() * 4;
    ctx.beginPath(); ctx.arc(kx, ky, r, s, e); ctx.stroke();
  }
  ctx.restore();
  const pa = K.lerp(a0, a1, 0.25);
  const groove = K.lobe(kx + Math.cos(pa) * 40, ky + Math.sin(pa) * 40, pa, R * 0.7, 30, { tip: 0.9, base: 0.9 });
  K.fill(ctx, K.roughen(groove.outline, 1, { seed: 646, spacing: 3 }), INKS.maroon[0]);
  K.fill(ctx, K.roughen(K.blob(kx, ky, 26, 26, { seed: 647, rough: 0.05 }), 1, { seed: 648, spacing: 2 }), INKS.maroon[0]);

  P.wheel(520, 1290, 230, INKS.green, 0.55, 691);
  P.wheel(1150, 1300, 210, INKS.teal, 0.55, 692);
  P.wheel(840, 1340, 200, INKS.sage, 0.5, 693);
  ctx.restore();
  K.grain(ctx, W, H, { amount: 13, linen: 5, seed: 699 });
}
