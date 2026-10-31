// <netreporter-viewer> — paginated report viewer for NetReporter.Viewer.
// No dependencies. Served by MapNetReporterViewer at {prefix}/_viewer/viewer.js.
//
//   <script type="module" src="/reports/_viewer/viewer.js"></script>
//   <netreporter-viewer src="/reports/invoice/open?id=42" lang="es"></netreporter-viewer>
//
// Attributes: src (the /open URL), lang (es|en), zoom (number, "page-width" or "page-fit"),
//             thumbnails ("open" | "closed"), set-title (copies the report title to document.title).
// Events:     nr-load {title, pageCount}, nr-error {status, error, detail}, nr-pagechange {page}.
// Methods:    reload(), goToPage(n), setZoom(z | "page-width" | "page-fit"), print(), download(format).
// Theming:    --nr-bg, --nr-toolbar-bg, --nr-toolbar-fg, --nr-accent, --nr-font on the element.

const I18N = {
    es: {
        thumbnails: 'Miniaturas', first: 'Primera página', prev: 'Página anterior', next: 'Página siguiente',
        last: 'Última página', page: 'Página', of: 'de', zoomOut: 'Alejar', zoomIn: 'Acercar', zoom: 'Zoom',
        fitWidth: 'Ajustar al ancho', fitPage: 'Página completa', widthShort: 'ancho', pageShort: 'página', print: 'Imprimir', download: 'Descargar',
        loading: 'Cargando reporte…', preparing: 'Preparando…', retry: 'Reintentar',
        notFound: 'El reporte no existe o no tienes acceso.', failed: 'No se pudo generar el reporte.',
        network: 'No se pudo conectar con el servidor.', empty: 'El reporte no tiene páginas.',
        pdf: 'PDF', xlsx: 'Excel (XLSX)', html: 'HTML', pageAlt: 'Página {n}',
    },
    en: {
        thumbnails: 'Thumbnails', first: 'First page', prev: 'Previous page', next: 'Next page',
        last: 'Last page', page: 'Page', of: 'of', zoomOut: 'Zoom out', zoomIn: 'Zoom in', zoom: 'Zoom',
        fitWidth: 'Fit to width', fitPage: 'Fit page', widthShort: 'width', pageShort: 'page', print: 'Print', download: 'Download',
        loading: 'Loading report…', preparing: 'Preparing…', retry: 'Retry',
        notFound: 'The report does not exist or you do not have access to it.', failed: 'The report could not be generated.',
        network: 'Could not reach the server.', empty: 'The report has no pages.',
        pdf: 'PDF', xlsx: 'Excel (XLSX)', html: 'HTML', pageAlt: 'Page {n}',
    },
};

const PT_TO_PX = 96 / 72;          // 100% = real size on screen
const ZOOM_STEPS = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
const MIN_ZOOM = 0.25, MAX_ZOOM = 4;
const PAGE_GAP = 16;                // px between pages
const PAD = 24;                     // px around the page column

const ICONS = {
    menu: 'M3 6h18M3 12h18M3 18h18',
    first: 'M18 17l-5-5 5-5M11 17l-5-5 5-5',
    prev: 'M15 18l-6-6 6-6',
    next: 'M9 18l6-6-6-6',
    last: 'M6 17l5-5-5-5M13 17l5-5-5-5',
    minus: 'M5 12h14',
    plus: 'M12 5v14M5 12h14',
    fitWidth: 'M3 12h18M7 8l-4 4 4 4M17 8l4 4-4 4',
    fitPage: 'M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5',
    print: 'M6 9V3h12v6M6 18H4a1 1 0 01-1-1v-6a2 2 0 012-2h14a2 2 0 012 2v6a1 1 0 01-1 1h-2M6 14h12v7H6z',
    download: 'M12 3v12M7 10l5 5 5-5M5 21h14',
};
const icon = (name) =>
    `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${ICONS[name]}"/></svg>`;

const STYLE = `
:host {
    --nr-bg: #525659; --nr-toolbar-bg: #323639; --nr-toolbar-fg: #f1f3f4; --nr-accent: #8ab4f8;
    --nr-font: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
    display: block; position: relative; height: 100%; min-height: 240px;
    font-family: var(--nr-font); font-size: 13px; color: var(--nr-toolbar-fg); background: var(--nr-bg);
    contain: strict;
}
* { box-sizing: border-box; }
.root { position: absolute; inset: 0; display: flex; flex-direction: column; }
.toolbar {
    flex: none; height: 44px; display: flex; align-items: center; gap: 4px; padding: 0 8px;
    background: var(--nr-toolbar-bg); box-shadow: 0 1px 4px rgba(0,0,0,.35); position: relative; z-index: 2;
}
.group { display: flex; align-items: center; gap: 2px; }
.sep { width: 1px; height: 20px; background: rgba(255,255,255,.2); margin: 0 6px; }
.spacer { flex: 1; min-width: 8px; }
.title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; opacity: .85; max-width: 40%; padding: 0 8px; }
button {
    all: unset; cursor: pointer; width: 32px; height: 32px; border-radius: 50%;
    display: inline-flex; align-items: center; justify-content: center; color: inherit;
}
button:hover:not(:disabled) { background: rgba(255,255,255,.12); }
button:focus-visible, input:focus-visible, select:focus-visible { outline: 2px solid var(--nr-accent); outline-offset: 1px; }
button:disabled { opacity: .35; cursor: default; }
button[aria-pressed="true"] { background: rgba(255,255,255,.18); }
svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
input.page-input {
    width: 44px; height: 26px; text-align: center; border: none; border-radius: 4px;
    background: rgba(0,0,0,.35); color: inherit; font: inherit;
}
input.page-input::-webkit-inner-spin-button { -webkit-appearance: none; }
.total { padding: 0 6px 0 4px; opacity: .85; white-space: nowrap; }
select.zoom {
    height: 26px; border: none; border-radius: 4px; background: rgba(0,0,0,.35); color: inherit; font: inherit;
    padding: 0 4px; max-width: 150px;
}
select.zoom option { color: #111; background: #fff; }
.menu { position: relative; }
.menu-list {
    position: absolute; right: 0; top: 38px; min-width: 160px; padding: 4px 0; border-radius: 6px;
    background: #fff; color: #1f2328; box-shadow: 0 6px 24px rgba(0,0,0,.35); display: none;
}
.menu-list.open { display: block; }
.menu-list button { width: 100%; height: auto; border-radius: 0; padding: 8px 14px; justify-content: flex-start; box-sizing: border-box; }
.menu-list button:hover { background: #eef2f7; }
.body { flex: 1; display: flex; min-height: 0; position: relative; }
.thumbs {
    flex: none; width: 168px; overflow-y: auto; padding: 12px 0; background: rgba(0,0,0,.2);
    display: none; flex-direction: column; align-items: center; gap: 14px;
}
.root.thumbs-open .thumbs { display: flex; }
.thumb { all: unset; cursor: pointer; display: flex; flex-direction: column; align-items: center; gap: 4px; font-size: 11px; opacity: .85; }
.thumb .frame { background: #fff; border: 3px solid transparent; border-radius: 2px; box-shadow: 0 1px 3px rgba(0,0,0,.4); }
.thumb img { display: block; width: 100%; height: 100%; }
.thumb.current { opacity: 1; }
.thumb.current .frame { border-color: var(--nr-accent); }
.thumb:focus-visible .frame { outline: 2px solid var(--nr-accent); }
.scroller { position: relative; flex: 1; overflow: auto; min-width: 0; outline: none; }
.pages {
    display: flex; flex-direction: column; align-items: center; gap: ${PAGE_GAP}px; padding: ${PAD}px;
    width: max-content; min-width: 100%;
}
.page {
    position: relative; flex: none; background: #fff; box-shadow: 0 2px 8px rgba(0,0,0,.45);
    width: calc(var(--pw) * var(--z)); height: calc(var(--ph) * var(--z));
}
.page img {
    position: absolute; inset: 0; width: 100%; height: 100%; user-select: none; -webkit-user-drag: none;
    opacity: 0; transition: opacity .15s;
}
.page.ready img { opacity: 1; }
.page:not(.ready):not(.failed)::before {
    content: ''; position: absolute; left: 50%; top: min(120px, 40%); width: 24px; height: 24px; margin-left: -12px;
    border-radius: 50%; border: 3px solid #e5e7eb; border-top-color: #9ca3af; animation: spin .8s linear infinite;
}
.page.failed::after {
    content: '⚠'; position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
    font-size: 32px; color: #b42318;
}
.overlay {
    position: absolute; inset: 44px 0 0 0; display: none; align-items: center; justify-content: center;
    flex-direction: column; gap: 12px; text-align: center; padding: 24px; z-index: 1;
}
.overlay.show { display: flex; }
.overlay .text { max-width: 520px; line-height: 1.5; }
.overlay .detail { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 12px; opacity: .75; white-space: pre-wrap; }
.overlay button.retry { width: auto; height: auto; border-radius: 4px; padding: 6px 14px; background: var(--nr-accent); color: #111; }
.spinner {
    width: 28px; height: 28px; border-radius: 50%; border: 3px solid rgba(255,255,255,.25);
    border-top-color: var(--nr-accent); animation: spin .8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
.busy {
    position: absolute; right: 12px; bottom: 12px; z-index: 3; display: none; align-items: center; gap: 8px;
    padding: 8px 12px; border-radius: 6px; background: var(--nr-toolbar-bg); box-shadow: 0 2px 8px rgba(0,0,0,.4);
}
.busy.show { display: flex; }
.busy .spinner { width: 16px; height: 16px; border-width: 2px; }
:host([data-narrow]) .wide { display: none; }
:host([data-narrow]) .title { display: none; }
:host([data-narrow]) .toolbar { gap: 0; padding: 0 4px; }
:host([data-tiny]) .tiny-hide { display: none; }
.toolbar { overflow-x: auto; scrollbar-width: none; }
@media print { :host { display: none; } }
`;

class NetReporterViewer extends HTMLElement {
    static observedAttributes = ['src', 'lang'];

    constructor() {
        super();
        this._root = this.attachShadow({ mode: 'open' });
        this._doc = null;           // metadata returned by /open
        this._zoom = 1;
        this._zoomMode = 'page-width';  // 'page-width' | 'page-fit' | null (explicit number)
        this._page = 1;
        this._loadSeq = 0;
        this._reopening = null;
        this._printFrame = null;
        this._connected = false;
        this.addEventListener('keydown', (e) => this._onKey(e));
    }

    // ---------- lifecycle ----------

    connectedCallback() {
        this._connected = true;
        this._t = I18N[(this.getAttribute('lang') || document.documentElement.lang || 'es').slice(0, 2)] || I18N.es;
        this._build();
        const z = this.getAttribute('zoom');
        if (z === 'page-width' || z === 'page-fit') {
            this._zoomMode = z;
        } else if (z) {
            this._zoomMode = null;
            this._zoom = clampZoom(+z || 1);
        }
        if (this.getAttribute('src')) this.reload();
    }

    disconnectedCallback() {
        this._connected = false;
        this._io?.disconnect();
        this._thumbIo?.disconnect();
        this._ro?.disconnect();
        document.removeEventListener('pointerdown', this._onDocPointerDown, true);
        this._printFrame?.remove();
        if (this._printUrl) URL.revokeObjectURL(this._printUrl);
    }

    attributeChangedCallback(name, oldValue, newValue) {
        if (!this._connected || oldValue === newValue) return;
        if (name === 'src') { this._page = 1; this._doc = null; this.reload(); }
        else if (name === 'lang') { this._t = I18N[(newValue || 'es').slice(0, 2)] || I18N.es; this._build(); this.reload(); }
    }

    // ---------- public API ----------

    get pageCount() { return this._doc?.pageCount ?? 0; }
    get currentPage() { return this._page; }
    get zoom() { return this._zoom; }

    /** Reopens the report (new layout, new data). Keeps the current page and zoom. */
    async reload({ silent = false } = {}) {
        const src = this.getAttribute('src');
        if (!src) return;
        const seq = ++this._loadSeq;
        const keepPage = this._page;
        if (!silent) this._showOverlay('loading');
        let res, body;
        try {
            res = await fetch(src, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            body = await res.json().catch(() => null);
        } catch {
            if (seq === this._loadSeq) this._fail(0, 'network');
            return;
        }
        if (seq !== this._loadSeq) return;
        if (!res.ok || !body) {
            this._fail(res.status, res.status === 404 ? 'notFound' : 'failed', body?.detail);
            return;
        }
        this._setDocument(body, keepPage);
    }

    goToPage(n, { smooth = false } = {}) {
        if (!this._doc) return;
        n = Math.min(Math.max(1, Math.round(n) || 1), this._doc.pageCount);
        const el = this._pageEls[n - 1];
        this._$scroller.scrollTo({ top: Math.max(0, el.offsetTop - PAGE_GAP / 2), behavior: smooth ? 'smooth' : 'auto' });
        this._setCurrentPage(n);
    }

    setZoom(z) {
        if (z === 'page-width' || z === 'page-fit') {
            this._zoomMode = z;
            this._refit();
            this._renderZoomSelect();
            this._updateNav();
        } else {
            this._zoomMode = null;
            this._applyZoom(clampZoom(+z || 1));
        }
    }

    async print() {
        const url = this._doc?.exports?.pdf;
        if (!url) return;
        const blob = await this._fetchExport('pdf');
        if (!blob) return;
        this._printFrame?.remove();
        if (this._printUrl) URL.revokeObjectURL(this._printUrl);
        this._printUrl = URL.createObjectURL(blob);
        const frame = document.createElement('iframe');
        frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
        frame.src = this._printUrl;
        frame.onload = () => {
            try {
                frame.contentWindow.focus();
                frame.contentWindow.print();
            } catch {
                window.open(this._printUrl, '_blank', 'noopener');
            }
        };
        document.body.appendChild(frame);
        this._printFrame = frame;
    }

    async download(format) {
        if (!this._doc?.exports?.[format]) return;
        const blob = await this._fetchExport(format);
        if (!blob) return;
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this._doc.fileName || 'report'}.${format}`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
    }

    // ---------- DOM ----------

    _build() {
        const t = this._t;
        this._root.innerHTML = `
<style>${STYLE}</style>
<div class="root" part="root">
  <div class="toolbar" part="toolbar" role="toolbar">
    <button data-act="thumbs" class="wide" title="${t.thumbnails}" aria-label="${t.thumbnails}" aria-pressed="false">${icon('menu')}</button>
    <span class="title" data-ref="title"></span>
    <span class="spacer"></span>
    <div class="group">
      <button data-act="first" class="wide" title="${t.first}" aria-label="${t.first}">${icon('first')}</button>
      <button data-act="prev" title="${t.prev}" aria-label="${t.prev}">${icon('prev')}</button>
      <input class="page-input" data-ref="page" type="text" inputmode="numeric" aria-label="${t.page}" value="1">
      <span class="total" data-ref="total">${t.of} –</span>
      <button data-act="next" title="${t.next}" aria-label="${t.next}">${icon('next')}</button>
      <button data-act="last" class="wide" title="${t.last}" aria-label="${t.last}">${icon('last')}</button>
    </div>
    <span class="sep wide"></span>
    <div class="group">
      <button data-act="zoomOut" title="${t.zoomOut}" aria-label="${t.zoomOut}">${icon('minus')}</button>
      <select class="zoom tiny-hide" data-ref="zoom" aria-label="${t.zoom}"></select>
      <button data-act="zoomIn" title="${t.zoomIn}" aria-label="${t.zoomIn}">${icon('plus')}</button>
      <button data-act="fitWidth" class="wide" title="${t.fitWidth}" aria-label="${t.fitWidth}">${icon('fitWidth')}</button>
      <button data-act="fitPage" class="wide" title="${t.fitPage}" aria-label="${t.fitPage}">${icon('fitPage')}</button>
    </div>
    <span class="spacer"></span>
    <div class="group">
      <button data-act="print" title="${t.print}" aria-label="${t.print}">${icon('print')}</button>
      <div class="menu">
        <button data-act="downloadMenu" title="${t.download}" aria-label="${t.download}" aria-haspopup="menu" aria-expanded="false">${icon('download')}</button>
        <div class="menu-list" data-ref="menu" role="menu"></div>
      </div>
    </div>
  </div>
  <div class="body">
    <nav class="thumbs" part="thumbnails" data-ref="thumbs" aria-label="${t.thumbnails}"></nav>
    <div class="scroller" part="scroller" data-ref="scroller" tabindex="0">
      <div class="pages" data-ref="pages"></div>
    </div>
  </div>
  <div class="overlay" data-ref="overlay" role="status"></div>
  <div class="busy" data-ref="busy" role="status"><span class="spinner"></span><span>${t.preparing}</span></div>
</div>`;
        const $ = (r) => this._root.querySelector(`[data-ref="${r}"]`);
        this._$rootEl = this._root.querySelector('.root');
        this._$title = $('title');
        this._$page = $('page');
        this._$total = $('total');
        this._$zoom = $('zoom');
        this._$menu = $('menu');
        this._$thumbs = $('thumbs');
        this._$scroller = $('scroller');
        this._$pages = $('pages');
        this._$overlay = $('overlay');
        this._$busy = $('busy');
        this._pageEls = [];
        this._thumbEls = [];

        this._root.querySelector('.toolbar').addEventListener('click', (e) => {
            const act = e.target.closest('button[data-act]')?.dataset.act;
            if (act) this._onAction(act, e);
        });
        this._$menu.addEventListener('click', (e) => {
            const fmt = e.target.closest('button[data-format]')?.dataset.format;
            if (fmt) { this._toggleMenu(false); this.download(fmt); }
        });
        if (this._onDocPointerDown) document.removeEventListener('pointerdown', this._onDocPointerDown, true);
        this._onDocPointerDown = (e) => { if (!e.composedPath().includes(this._$menu.parentElement)) this._toggleMenu(false); };
        document.addEventListener('pointerdown', this._onDocPointerDown, true);

        this._$page.addEventListener('change', () => this.goToPage(parseInt(this._$page.value, 10)));
        this._$page.addEventListener('focus', () => this._$page.select());
        this._$page.addEventListener('keydown', (e) => { if (e.key === 'Enter') { this.goToPage(parseInt(this._$page.value, 10)); this._$scroller.focus(); } });
        this._$zoom.addEventListener('change', () => {
            const v = this._$zoom.value;
            this.setZoom(v === 'page-width' || v === 'page-fit' ? v : +v);
        });

        let ticking = false;
        this._$scroller.addEventListener('scroll', () => {
            if (ticking) return;
            ticking = true;
            requestAnimationFrame(() => { ticking = false; this._trackCurrentPage(); });
        }, { passive: true });
        this._$scroller.addEventListener('wheel', (e) => this._onWheel(e), { passive: false });

        this._io?.disconnect();
        this._io = new IntersectionObserver((entries) => {
            for (const en of entries) if (en.isIntersecting) this._loadPage(en.target);
        }, { root: this._$scroller, rootMargin: '150% 0px' });
        this._thumbIo?.disconnect();
        this._thumbIo = new IntersectionObserver((entries) => {
            for (const en of entries) if (en.isIntersecting) this._loadThumb(en.target);
        }, { root: this._$thumbs, rootMargin: '100% 0px' });

        this._ro?.disconnect();
        this._ro = new ResizeObserver(() => {
            this.toggleAttribute('data-narrow', this.clientWidth < 640);
            this.toggleAttribute('data-tiny', this.clientWidth < 440);
            this._refit();
        });
        this._ro.observe(this);

        const wantThumbs = this.getAttribute('thumbnails');
        this._setThumbs(wantThumbs ? wantThumbs === 'open' : this.clientWidth >= 1000);
        this._renderZoomSelect();
        this._updateNav();
    }

    _setDocument(doc, keepPage) {
        const reopened = this._doc && this._doc.pageCount === doc.pageCount;
        this._doc = doc;
        this._hideOverlay();
        if (this.hasAttribute('set-title') && doc.title) document.title = doc.title;
        this._$title.textContent = doc.title || '';
        this._$title.title = doc.title || '';
        this._$total.textContent = `${this._t.of} ${doc.pageCount}`;

        const menu = ['pdf', 'xlsx', 'html'].filter((f) => doc.exports?.[f]);
        this._$menu.innerHTML = menu.map((f) => `<button role="menuitem" data-format="${f}">${this._t[f]}</button>`).join('');
        this._root.querySelector('[data-act="downloadMenu"]').disabled = menu.length === 0;
        this._root.querySelector('[data-act="print"]').disabled = !doc.exports?.pdf;

        if (doc.pageCount === 0) { this._showOverlay('message', this._t.empty); }

        this._$pages.style.setProperty('--pw', `${doc.pageWidth * PT_TO_PX}px`);
        this._$pages.style.setProperty('--ph', `${doc.pageHeight * PT_TO_PX}px`);

        if (reopened) {
            // Same document shape: swap the image URLs in place, without losing scroll.
            this._pageEls.forEach((el) => { el.dataset.loaded = ''; el.classList.remove('failed', 'ready'); el.querySelector('img')?.remove(); this._io.unobserve(el); this._io.observe(el); });
            this._thumbEls.forEach((el) => { el.dataset.loaded = ''; el.querySelector('img')?.remove(); this._thumbIo.unobserve(el); this._thumbIo.observe(el); });
        } else {
            this._buildPages(doc);
        }

        if (this._zoomMode) this._refit(); else this._applyZoom(this._zoom);
        if (!reopened) this.goToPage(keepPage > 1 ? keepPage : 1);
        this._updateNav();
        this.dispatchEvent(new CustomEvent('nr-load', { detail: { title: doc.title, pageCount: doc.pageCount } }));
    }

    _buildPages(doc) {
        this._io.disconnect();
        this._thumbIo.disconnect();
        const pages = document.createDocumentFragment();
        const thumbs = document.createDocumentFragment();
        const tw = 112, th = Math.round(tw * doc.pageHeight / doc.pageWidth);
        this._pageEls = [];
        this._thumbEls = [];
        for (let n = 1; n <= doc.pageCount; n++) {
            const p = document.createElement('div');
            p.className = 'page';
            p.dataset.page = n;
            pages.appendChild(p);
            this._pageEls.push(p);

            const b = document.createElement('button');
            b.className = 'thumb';
            b.dataset.page = n;
            b.type = 'button';
            b.setAttribute('aria-label', this._t.pageAlt.replace('{n}', n));
            b.innerHTML = `<span class="frame" style="width:${tw}px;height:${th}px"></span><span>${n}</span>`;
            b.addEventListener('click', () => this.goToPage(n));
            thumbs.appendChild(b);
            this._thumbEls.push(b);
        }
        this._$pages.replaceChildren(pages);
        this._$thumbs.replaceChildren(thumbs);
        this._pageEls.forEach((el) => this._io.observe(el));
        this._thumbEls.forEach((el) => this._thumbIo.observe(el));
    }

    _pageUrl(n) { return `${this._doc.pageUrl}${n}`; }

    _loadPage(el) {
        if (el.dataset.loaded) return;
        el.dataset.loaded = '1';
        const n = +el.dataset.page;
        const img = new Image();
        img.alt = this._t.pageAlt.replace('{n}', n);
        img.decoding = 'async';
        img.draggable = false;
        img.onload = () => img.decode().catch(() => {}).then(() => el.classList.add('ready'));
        img.onerror = () => { el.classList.add('failed'); this._reopenExpired(); };
        img.src = this._pageUrl(n);
        el.appendChild(img);
    }

    _loadThumb(el) {
        if (el.dataset.loaded) return;
        el.dataset.loaded = '1';
        const img = new Image();
        img.alt = '';
        img.decoding = 'async';
        img.src = this._pageUrl(+el.dataset.page);
        el.querySelector('.frame').appendChild(img);
    }

    /** The server forgets documents after a while: reopen once and reload the visible pages. */
    _reopenExpired() {
        if (this._reopening) return this._reopening;
        this._reopening = this.reload({ silent: true }).finally(() => { setTimeout(() => { this._reopening = null; }, 5000); });
        return this._reopening;
    }

    async _fetchExport(format, retried = false) {
        this._$busy.classList.add('show');
        try {
            const res = await fetch(this._doc.exports[format], { credentials: 'same-origin' });
            if (res.status === 404 && !retried) {
                await this._reopenExpired();
                return this._doc?.exports?.[format] ? this._fetchExport(format, true) : null;
            }
            if (!res.ok) { this._toast(this._t.failed); return null; }
            return await res.blob();
        } catch {
            this._toast(this._t.network);
            return null;
        } finally {
            this._$busy.classList.remove('show');
        }
    }

    // ---------- navigation ----------

    _trackCurrentPage() {
        if (!this._pageEls.length) return;
        const probe = this._$scroller.scrollTop + this._$scroller.clientHeight * 0.35;
        let lo = 0, hi = this._pageEls.length - 1;
        while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (this._pageEls[mid].offsetTop <= probe) lo = mid; else hi = mid - 1;
        }
        this._setCurrentPage(lo + 1);
    }

    _setCurrentPage(n) {
        if (n === this._page && this._$page.value === String(n) && this._thumbEls[n - 1]?.classList.contains('current')) return;
        const prev = this._page;
        this._page = n;
        if (this._root.activeElement !== this._$page) this._$page.value = String(n);
        this._thumbEls[prev - 1]?.classList.remove('current');
        const th = this._thumbEls[n - 1];
        if (th) {
            th.classList.add('current');
            const r = th.getBoundingClientRect(), c = this._$thumbs.getBoundingClientRect();
            if (r.top < c.top || r.bottom > c.bottom) th.scrollIntoView({ block: 'nearest' });
        }
        this._updateNav();
        if (prev !== n) this.dispatchEvent(new CustomEvent('nr-pagechange', { detail: { page: n } }));
    }

    _updateNav() {
        const count = this._doc?.pageCount ?? 0;
        const q = (a) => this._root.querySelector(`[data-act="${a}"]`);
        q('first').disabled = q('prev').disabled = !count || this._page <= 1;
        q('next').disabled = q('last').disabled = !count || this._page >= count;
        this._$page.disabled = !count;
        q('zoomOut').disabled = !count || this._zoom <= MIN_ZOOM + 1e-6;
        q('zoomIn').disabled = !count || this._zoom >= MAX_ZOOM - 1e-6;
        q('fitWidth').setAttribute('aria-pressed', String(this._zoomMode === 'page-width'));
        q('fitPage').setAttribute('aria-pressed', String(this._zoomMode === 'page-fit'));
    }

    // ---------- zoom ----------

    /** Re-applies page-width / page-fit. Runs twice when the zoom adds or removes the vertical scrollbar. */
    _refit() {
        if (!this._doc || !this._zoomMode) return;
        for (let i = 0; i < 2; i++) {
            const z = this._fitZoom(this._zoomMode);
            if (Math.abs(z - this._zoom) < 1e-3) break;
            this._applyZoom(z);
        }
    }

    _fitZoom(mode) {
        if (!this._doc) return 1;
        const w = this._$scroller.clientWidth - PAD * 2;
        const h = this._$scroller.clientHeight - PAD * 2;
        const pw = this._doc.pageWidth * PT_TO_PX, ph = this._doc.pageHeight * PT_TO_PX;
        const z = mode === 'page-fit' ? Math.min(w / pw, h / ph) : w / pw;
        return clampZoom(z > 0 ? z : 1);
    }

    /**
     * Changes the zoom keeping the point under (anchorX, anchorY) — px relative to the scroller viewport —
     * in place. Without an anchor it keeps the same fraction of the current page at the top of the view.
     */
    _applyZoom(z, { anchorX = null, anchorY = null, keepPosition = true } = {}) {
        const s = this._$scroller;
        const old = this._zoom;
        z = clampZoom(z);
        const ax = anchorX ?? s.clientWidth / 2;
        const rx = s.scrollWidth ? (s.scrollLeft + ax) / s.scrollWidth : 0;
        const ry = s.scrollHeight ? (s.scrollTop + (anchorY ?? 0)) / s.scrollHeight : 0;
        const el = this._pageEls[this._page - 1];
        const frac = el && el.offsetHeight ? (s.scrollTop - el.offsetTop) / el.offsetHeight : 0;

        this._zoom = z;
        this._$pages.style.setProperty('--z', String(z));

        if (keepPosition && old !== z) {
            if (anchorY != null) s.scrollTop = ry * s.scrollHeight - anchorY;
            else if (el) s.scrollTop = el.offsetTop + frac * el.offsetHeight;
            s.scrollLeft = rx * s.scrollWidth - ax;
        }
        this._renderZoomSelect();
        this._updateNav();
    }

    _stepZoom(dir, anchor) {
        const cur = this._zoom;
        const next = dir > 0
            ? ZOOM_STEPS.find((v) => v > cur + 1e-3) ?? MAX_ZOOM
            : [...ZOOM_STEPS].reverse().find((v) => v < cur - 1e-3) ?? MIN_ZOOM;
        this._zoomMode = null;
        this._applyZoom(next, anchor);
    }

    _renderZoomSelect() {
        const t = this._t;
        const pct = Math.round(this._zoom * 100);
        const steps = ZOOM_STEPS.map((v) => `<option value="${v}">${Math.round(v * 100)}%</option>`);
        const custom = ZOOM_STEPS.some((v) => Math.round(v * 100) === pct) ? '' : `<option value="${this._zoom}">${pct}%</option>`;
        this._$zoom.innerHTML =
            `<option value="page-width">${t.fitWidth}</option><option value="page-fit">${t.fitPage}</option>${custom}${steps.join('')}`;
        this._$zoom.value = this._zoomMode ?? (custom ? String(this._zoom) : String(ZOOM_STEPS.find((v) => Math.round(v * 100) === pct)));
        if (this._zoomMode) {
            // Show the effective % next to the mode name.
            const opt = this._$zoom.querySelector(`option[value="${this._zoomMode}"]`);
            opt.textContent = `${pct}% (${this._zoomMode === 'page-width' ? t.widthShort : t.pageShort})`;
        }
    }

    _onWheel(e) {
        if (!(e.ctrlKey || e.metaKey) || !this._doc) return;
        e.preventDefault();
        const r = this._$scroller.getBoundingClientRect();
        const anchor = { anchorX: e.clientX - r.left, anchorY: e.clientY - r.top };
        // Trackpad pinch sends small fractional deltas with ctrlKey; mice send ±100.
        if (Math.abs(e.deltaY) < 40) {
            this._zoomMode = null;
            this._applyZoom(this._zoom * Math.exp(-e.deltaY * 0.01), anchor);
        } else {
            this._stepZoom(e.deltaY < 0 ? 1 : -1, anchor);
        }
    }

    // ---------- input ----------

    _onAction(act, e) {
        switch (act) {
            case 'thumbs': this._setThumbs(!this._$rootEl.classList.contains('thumbs-open')); break;
            case 'first': this.goToPage(1); break;
            case 'prev': this.goToPage(this._page - 1); break;
            case 'next': this.goToPage(this._page + 1); break;
            case 'last': this.goToPage(this.pageCount); break;
            case 'zoomOut': this._stepZoom(-1); break;
            case 'zoomIn': this._stepZoom(1); break;
            case 'fitWidth': this.setZoom('page-width'); break;
            case 'fitPage': this.setZoom('page-fit'); break;
            case 'print': this.print(); break;
            case 'downloadMenu': this._toggleMenu(); e.stopPropagation(); break;
        }
    }

    _onKey(e) {
        const inInput = e.composedPath()[0]?.tagName === 'INPUT' || e.composedPath()[0]?.tagName === 'SELECT';
        const mod = e.ctrlKey || e.metaKey;
        if (mod && (e.key === '+' || e.key === '=')) { e.preventDefault(); this._stepZoom(1); return; }
        if (mod && e.key === '-') { e.preventDefault(); this._stepZoom(-1); return; }
        if (mod && e.key === '0') { e.preventDefault(); this.setZoom(1); return; }
        if (mod && e.key.toLowerCase() === 'p') { e.preventDefault(); this.print(); return; }
        if (inInput || mod || e.altKey) return;
        if (e.key === 'Escape') { this._toggleMenu(false); return; }
        if (e.key === 'Home') { e.preventDefault(); this.goToPage(1); }
        else if (e.key === 'End') { e.preventDefault(); this.goToPage(this.pageCount); }
        else if (e.key === 'ArrowRight' && !this._hasHorizontalScroll()) { e.preventDefault(); this.goToPage(this._page + 1); }
        else if (e.key === 'ArrowLeft' && !this._hasHorizontalScroll()) { e.preventDefault(); this.goToPage(this._page - 1); }
    }

    _hasHorizontalScroll() { return this._$scroller.scrollWidth > this._$scroller.clientWidth + 1; }

    _setThumbs(open) {
        this._$rootEl.classList.toggle('thumbs-open', open);
        this._root.querySelector('[data-act="thumbs"]').setAttribute('aria-pressed', String(open));
        requestAnimationFrame(() => this._refit());
    }

    _toggleMenu(force) {
        const open = force ?? !this._$menu.classList.contains('open');
        this._$menu.classList.toggle('open', open);
        this._root.querySelector('[data-act="downloadMenu"]').setAttribute('aria-expanded', String(open));
    }

    // ---------- overlays ----------

    _showOverlay(kind, text, detail) {
        const o = this._$overlay;
        o.classList.add('show');
        if (kind === 'loading') {
            o.innerHTML = `<span class="spinner"></span><span class="text">${this._t.loading}</span>`;
        } else {
            o.innerHTML = '';
            const p = document.createElement('div');
            p.className = 'text';
            p.textContent = text;
            o.appendChild(p);
            if (detail) {
                const d = document.createElement('div');
                d.className = 'detail';
                d.textContent = detail;
                o.appendChild(d);
            }
            if (kind === 'error') {
                const b = document.createElement('button');
                b.className = 'retry';
                b.textContent = this._t.retry;
                b.addEventListener('click', () => this.reload());
                o.appendChild(b);
            }
        }
    }

    _hideOverlay() { this._$overlay.classList.remove('show'); }

    _fail(status, key, detail) {
        this._doc = null;
        this._$pages.replaceChildren();
        this._$thumbs.replaceChildren();
        this._pageEls = [];
        this._thumbEls = [];
        this._$title.textContent = '';
        this._$total.textContent = `${this._t.of} –`;
        this._updateNav();
        this._showOverlay('error', this._t[key], detail);
        this.dispatchEvent(new CustomEvent('nr-error', { detail: { status, error: key, detail } }));
    }

    _toast(text) {
        const b = this._$busy;
        b.querySelector('span:last-child').textContent = text;
        b.classList.add('show');
        setTimeout(() => { b.classList.remove('show'); b.querySelector('span:last-child').textContent = this._t.preparing; }, 3000);
    }
}

function clampZoom(z) { return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z)); }

if (!customElements.get('netreporter-viewer')) customElements.define('netreporter-viewer', NetReporterViewer);

export { NetReporterViewer };
