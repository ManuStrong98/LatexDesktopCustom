import assert from 'node:assert/strict';
import { toSyncTexPosition } from '../src/LatexDesktopCustom/PdfViewer/coordinates.mjs';

// Independent affine transforms for a 612 x 792 PDF page, rotated and zoomed.
for (const scale of [0.5, 1, 1.75, 3]) {
  for (const rotation of [0, 90, 180, 270]) {
    const w = 612, h = 792;
    const matrices = {
      0: [scale, 0, 0, -scale, 0, h * scale],
      90: [0, scale, scale, 0, 0, 0],
      180: [-scale, 0, 0, scale, w * scale, 0],
      270: [0, -scale, -scale, 0, h * scale, w * scale]
    };
    const [a, b, c, d, e, f] = matrices[rotation];
    const width = (rotation % 180 ? h : w) * scale;
    const height = (rotation % 180 ? w : h) * scale;
    const viewport = { width, height, viewBox: [0, 0, w, h], convertToPdfPoint(x, y) {
      return [(d * (x - e) - c * (y - f)) / (a * d - b * c), (-b * (x - e) + a * (y - f)) / (a * d - b * c)];
    } };
    const bounds = { left: 37, top: 91, width: width * 0.8, height: height * 0.8 };
    const expected = { h: 123, v: 456 };
    const x = a * expected.h + c * (h - expected.v) + e;
    const y = b * expected.h + d * (h - expected.v) + f;
    const actual = toSyncTexPosition(viewport, bounds, bounds.left + x * 0.8, bounds.top + y * 0.8);
    assert.ok(Math.abs(actual.h - expected.h) < 1e-8);
    assert.ok(Math.abs(actual.v - expected.v) < 1e-8);
    assert.equal(toSyncTexPosition(viewport, bounds, bounds.left - 5, bounds.top), null);
  }
}
console.log('Coordenadas SyncTeX: zoom, rotación, escala CSS y bordes: OK');
