// Browser client coordinates and canvas bounds use CSS pixels, not device pixels.
// Invert PDF.js's viewport transform to account for zoom, rotation and scaling.
export function toSyncTexPosition(viewport, bounds, clientX, clientY) {
  if (!bounds.width || !bounds.height) return null;
  const x = (clientX - bounds.left) * viewport.width / bounds.width;
  const y = (clientY - bounds.top) * viewport.height / bounds.height;
  if (x < 0 || y < 0 || x > viewport.width || y > viewport.height) return null;
  const [pdfX, pdfY] = viewport.convertToPdfPoint(x, y);
  const [left, , , top] = viewport.viewBox;
  return { h: Math.max(0, pdfX - left), v: Math.max(0, top - pdfY) };
}
