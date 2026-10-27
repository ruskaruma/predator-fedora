// PredatorCore spot B: the core. A processor on its green board, heat rising off the die in ribbons,
// on an ultramarine disc. Transparent background.
// Render: node render.mjs spot-chip.js,plants.js spot-chip.png 1600 1600
function draw(ctx, W, H, K) {
  ctx.save();
  ctx.scale(W / 1600, H / 1600);
  const P = Plants(ctx, K), TAU = Math.PI * 2, ink = '#1b1a17';
  P.disc(930, 600, 470, INKS.ultra, 701);

  const cx = 800, cy = 840, rot = -0.16;
  const tr = (pts) => pts.map(([x, y]) => [cx + x * Math.cos(rot) - y * Math.sin(rot), cy + x * Math.sin(rot) + y * Math.cos(rot)]);
  const rect = (x0, y0, x1, y1) => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
  const cut = (pts, seed, amt = 1.4) => K.roughen(tr(pts), amt, { seed, spacing: 3 });

  // heat off the die: three ribbons rising and bending in coral and mustard
  const ribbon = (x0, y0, h, amp, ph, w, ink2, seed) => {
    const L = [], R = [];
    for (let i = 0; i <= 50; i++) {
      const t = i / 50, y = y0 - h * t, x = x0 + amp * Math.sin(ph + t * 7.5) * (0.3 + t), ww = w * (1 - t * 0.75);
      L.push([x - ww, y]); R.push([x + ww, y]);
    }
    const o = K.roughen([...L, ...R.reverse()], 1.4, { seed, spacing: 3 });
    ctx.save(); ctx.translate(14, 10); K.fill(ctx, o, INKS.verm); ctx.restore();
    K.fill(ctx, o, ink2[0]);
    K.hatch(ctx, o, { ink: ink2[1], gap: 6, width: 1.2, angle: 1.5, dash: [20, 70], lift: 0.2, seed: seed + 1, alpha: 0.6 });
  };
  ribbon(700, 700, 430, 46, 0.3, 22, INKS.mustard, 711);
  ribbon(830, 660, 500, 54, 2.0, 26, ['#f47a5a', '#8a2a14'], 712);
  ribbon(960, 640, 420, 42, 3.6, 20, INKS.cream, 713);

  // board, pads and die; the vermilion plate goes under the board first
  const board = cut(rect(-270, -230, 270, 230), 721, 2);
  ctx.save(); ctx.translate(20, 14); K.fill(ctx, board, INKS.verm); ctx.restore();
  K.fill(ctx, board, INKS.green[0]);
  K.flow(ctx, board, { ink: INKS.green[1], per: 0.004, len: 26, turn: 0.2, freq: 0.003, width: 1.2, seed: 722, alpha: 0.7 });
  // gold contacts in rows along every edge, and a pin-one triangle
  const pr = K.rng(723);
  for (let i = 0; i < 11; i++) {
    const t = -205 + i * 41;
    for (const [x0, y0, x1, y1] of [[t - 9, -222, t + 9, -196], [t - 9, 196, t + 9, 222], [-262, t - 9, -236, t + 9], [236, t - 9, 262, t + 9]]) {
      K.fill(ctx, cut(rect(x0, y0, x1, y1), 724 + Math.floor(pr() * 1000), 0.7), INKS.mustard[0]);
    }
  }
  K.fill(ctx, cut([[-220, -180], [-180, -180], [-220, -140]], 725, 0.6), INKS.cream[0]);
  // small surface parts on the board
  for (const [x, y] of [[-150, 150], [-110, 150], [150, -160], [190, -160], [180, 150]]) {
    K.fill(ctx, cut(rect(x - 12, y - 7, x + 12, y + 7), 726 + x, 0.5), ink);
    K.fill(ctx, cut(rect(x - 12, y - 7, x - 7, y + 7), 727 + x, 0.3), INKS.cream[0]);
  }

  // the die: a cream lid with pulled lines, and a maroon core printed slightly off its window
  const die = cut(rect(-130, -120, 130, 120), 731, 1.2);
  K.fill(ctx, die, INKS.cream[0]);
  K.hatch(ctx, die, { ink: INKS.cream[1], gap: 5, width: 1.1, angle: rot + 0.02, dash: [40, 200], lift: 0.2, seed: 732, alpha: 0.6 });
  const core = cut(rect(-62, -56, 62, 56), 733, 1);
  ctx.save(); ctx.translate(8, 6); K.fill(ctx, core, INKS.verm); ctx.restore();
  K.fill(ctx, core, INKS.maroon[0]);
  K.radial(ctx, cx, cy, { clip: core, count: 90, r0: 0, r1: 90, ink: INKS.maroon[1], width: 1.3, bend: 0.2, seed: 734, alpha: 0.9 });
  K.stipple(ctx, die, { ink, per: 0.001, rMin: 0.8, rMax: 1.8, seed: 735 });

  P.wheel(520, 1290, 230, INKS.bottle, 0.55, 791);
  P.wheel(1150, 1300, 210, INKS.teal, 0.55, 792);
  P.wheel(840, 1340, 200, INKS.sage, 0.5, 793);
  ctx.restore();
  K.grain(ctx, W, H, { amount: 13, linen: 5, seed: 799 });
}
