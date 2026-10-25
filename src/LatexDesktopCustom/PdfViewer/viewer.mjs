import { toSyncTexPosition } from './coordinates.mjs';

const post = message => window.chrome.webview.postMessage(message);
const status = document.getElementById('status');
const container = document.getElementById('viewerContainer');
const pageInput = document.getElementById('page');
const zoom = document.getElementById('zoom');
let viewer, linkService, loadingTask, documentId, syncEnabled = false, generation = 0;
let lastNavigation;

function navigateAt(target, clientX, clientY) {
  const page = target?.closest('.page');
  if (!page || !viewer?.pdfDocument) return false;
  if (!syncEnabled) { status.textContent = 'Compila este proyecto desde la app para activar SyncTeX.'; return false; }
  const pageNumber = Number(page.dataset.pageNumber);
  const pageView = viewer.getPageView(pageNumber - 1);
  const canvas = pageView?.canvas;
  if (!canvas) return false;
  const point = toSyncTexPosition(pageView.viewport, canvas.getBoundingClientRect(), clientX, clientY);
  if (!point) return false;
  const now = performance.now();
  // WPF's native event and Chromium may both report the same physical click.
  if (lastNavigation?.id === documentId && lastNavigation.page === pageNumber && now - lastNavigation.time < 250
      && Math.abs(lastNavigation.h - point.h) < 1 && Math.abs(lastNavigation.v - point.v) < 1) return true;
  lastNavigation = { id: documentId, page: pageNumber, ...point, time: now };
  status.textContent = 'Buscando la posición en Neovim…';
  post({ type: 'gotoSource', documentId, page: pageNumber, ...point });
  return true;
}

async function openDocument(message, data) {
  const current = ++generation;
  const previous = message.preservePosition && viewer.pdfDocument
    ? { page: viewer.currentPageNumber, scale: viewer.currentScaleValue } : null;
  documentId = message.documentId;
  syncEnabled = message.syncEnabled;
  status.textContent = 'Cargando PDF…';
  document.getElementById('empty').style.display = 'none';
  viewer.setDocument(null);
  linkService.setDocument(null);
  await loadingTask?.destroy();
  if (current !== generation) return;
  const task = globalThis.pdfjsLib.getDocument({
    data, isEvalSupported: false,
    cMapUrl: 'vendor/pdfjs/cmaps/', cMapPacked: true,
    standardFontDataUrl: 'vendor/pdfjs/standard_fonts/', wasmUrl: 'vendor/pdfjs/wasm/',
    iccUrl: 'vendor/pdfjs/iccs/'
  });
  loadingTask = task;
  try {
    const pdf = await task.promise;
    if (current !== generation) return;
    document.title = message.name + ' — Visor PDF';
    linkService.setDocument(pdf);
    viewer.setDocument(pdf);
    await viewer.firstPagePromise;
    if (current !== generation) return;
    viewer.currentScaleValue = previous?.scale || 'page-width';
    viewer.currentPageNumber = Math.min(previous?.page || 1, pdf.numPages);
    document.getElementById('pages').textContent = '/ ' + pdf.numPages;
    pageInput.max = pdf.numPages;
    status.textContent = syncEnabled ? 'Doble clic para abrir el archivo y la línea en Neovim.' : 'PDF local: compila desde la app para activar SyncTeX.';
    post({ type: 'pdfLoaded', documentId, pages: pdf.numPages });
  } catch (error) {
    if (current === generation) {
      status.textContent = 'No se pudo abrir el PDF: ' + error.message;
      post({ type: 'viewerError', documentId, error: error.message });
    }
  }
}

async function initialize() {
  globalThis.pdfjsLib = await import('./vendor/pdfjs/build/pdf.mjs');
  const { EventBus, PDFViewer, PDFLinkService } = await import('./vendor/pdfjs/web/pdf_viewer.mjs');
  globalThis.pdfjsLib.GlobalWorkerOptions.workerSrc = './vendor/pdfjs/build/pdf.worker.mjs';
  const eventBus = new EventBus();
  linkService = new PDFLinkService({ eventBus });
  linkService.externalLinkEnabled = false;
  viewer = new PDFViewer({ container, viewer: document.getElementById('viewer'), eventBus, linkService, enableScripting: false });
  linkService.setViewer(viewer);
  eventBus.on('pagechanging', event => pageInput.value = event.pageNumber);
  eventBus.on('scalechanging', event => {
    if (event.presetValue && [...zoom.options].some(option => option.value === event.presetValue)) zoom.value = event.presetValue;
    else {
      let custom = zoom.querySelector('[data-custom]');
      if (!custom) { custom = document.createElement('option'); custom.dataset.custom = ''; zoom.append(custom); }
      custom.value = event.scale; custom.textContent = Math.round(event.scale * 100) + ' %'; zoom.value = custom.value;
    }
  });
  document.getElementById('previous').onclick = () => { if (viewer.currentPageNumber > 1) viewer.currentPageNumber--; };
  document.getElementById('next').onclick = () => { if (viewer.currentPageNumber < viewer.pagesCount) viewer.currentPageNumber++; };
  pageInput.onchange = () => { if (viewer.pdfDocument) viewer.currentPageNumber = Math.min(viewer.pagesCount, Math.max(1, Number(pageInput.value) || 1)); };
  zoom.onchange = () => { if (viewer.pdfDocument) viewer.currentScaleValue = zoom.value; };
  document.getElementById('zoomIn').onclick = () => { if (viewer.pdfDocument) viewer.currentScale = Math.min(5, viewer.currentScale * 1.2); };
  document.getElementById('zoomOut').onclick = () => { if (viewer.pdfDocument) viewer.currentScale = Math.max(0.25, viewer.currentScale / 1.2); };
  container.addEventListener('dblclick', event => {
    if (navigateAt(event.target, event.clientX, event.clientY)) event.preventDefault();
  });
  window.latexDesktopNavigateAt = ({ x, y }) => {
    const clientX = x * window.innerWidth, clientY = y * window.innerHeight;
    return navigateAt(document.elementFromPoint(clientX, clientY), clientX, clientY);
  };
  window.chrome.webview.addEventListener('sharedbufferreceived', event => {
    const buffer = event.getBuffer();
    let data;
    // PDF.js transfers its input to the worker. Copy before releasing the
    // read-only shared buffer so no code attempts to detach or write it.
    try { data = new Uint8Array(buffer).slice(); }
    finally { window.chrome.webview.releaseBuffer(buffer); }
    if (event.additionalData?.type === 'open')
      openDocument(event.additionalData, data).catch(error => post({ type: 'viewerError', documentId, error: error.message }));
  });
  post({ type: 'ready' });
}

initialize().catch(error => { status.textContent = 'No se pudo iniciar el visor: ' + error.message; post({ type: 'viewerError', error: error.message }); });
