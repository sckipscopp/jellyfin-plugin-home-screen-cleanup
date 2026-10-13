/*
 * Home Screen Cleanup - Jellyfin plugin web client script.
 * Adds "Remove from Continue Watching" / "Remove from Next Up" to the home-screen card menu.
 * SPDX-License-Identifier: GPL-3.0-only
 */
(function () {
  'use strict';

  if (window.HomeScreenCleanup) {
    return;
  }

  // ---------------------------------------------------------------- constants

  const STORE_KEY = 'hsc.removed.v1';
  const REMOVED_TTL_MS = 120 * 1000;
  const PENDING_WINDOW_MS = 3000;
  const POLL_START_MS = 100;
  const POLL_FACTOR = 1.5;
  const POLL_CAP_MS = 2000;
  const POLL_GIVE_UP_MS = 5 * 60 * 1000;
  const PREFS_DEBOUNCE_MS = 750;
  const PREFS_MIN_INTERVAL_MS = 30 * 1000;
  const TOAST_MS = 4000;
  const ID_RE = /^[0-9a-f]{32}$|^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const SECTION_RE = /(?:^|\s)section(\d+)(?:\s|$)/;
  const PLAYBACK_IDS = new Set(['resume', 'play', 'playallfromhere', 'queue', 'queuenext', 'shuffle']);
  const DEFAULT_SECTIONS = ['smalllibrarytiles', 'resume', 'resumeaudio', 'resumebook', 'livetv', 'nextup', 'latestmedia', 'none', 'none', 'none'];
  const KIND_RESUME = 'resume';
  const KIND_NEXTUP = 'nextup';
  const WRAPPED = Symbol('hscWrapped');

  const ENTRY = {
    resume: { dataId: 'hsc-remove-resume', icon: 'remove_circle_outline', label: 'resume' },
    nextup: { dataId: 'hsc-remove-nextup', icon: 'visibility_off', label: 'nextup' }
  };

  const STRINGS = {
    en: {
      resume: 'Remove from Continue Watching',
      nextup: 'Remove from Next Up',
      e401: 'Your session has expired. Sign in again and retry.',
      e403: 'You are not allowed to change this item.',
      e404: 'This item is no longer available.',
      e409: 'The limit of hidden series has been reached.',
      other: 'Could not update the home screen. Try again later.'
    },
    es: {
      resume: 'Quitar de Seguir viendo',
      nextup: 'Quitar de A continuación',
      e401: 'Tu sesión ha caducado. Vuelve a iniciar sesión e inténtalo de nuevo.',
      e403: 'No tienes permiso para cambiar este elemento.',
      e404: 'Este elemento ya no está disponible.',
      e409: 'Se ha alcanzado el límite de series ocultas.',
      other: 'No se pudo actualizar la pantalla de inicio. Inténtalo más tarde.'
    },
    de: {
      resume: 'Aus „Weiterschauen“ entfernen',
      nextup: 'Aus „Als Nächstes“ entfernen',
      e401: 'Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.',
      e403: 'Du darfst dieses Element nicht ändern.',
      e404: 'Dieses Element ist nicht mehr verfügbar.',
      e409: 'Die Höchstzahl ausgeblendeter Serien ist erreicht.',
      other: 'Der Startbildschirm konnte nicht aktualisiert werden. Bitte später erneut versuchen.'
    },
    fr: {
      resume: 'Retirer de Continuer de regarder',
      nextup: 'Retirer de À suivre',
      e401: 'Votre session a expiré. Reconnectez-vous puis réessayez.',
      e403: 'Vous n’êtes pas autorisé à modifier cet élément.',
      e404: 'Cet élément n’est plus disponible.',
      e409: 'La limite de séries masquées est atteinte.',
      other: 'Impossible de mettre à jour l’écran d’accueil. Réessayez plus tard.'
    }
  };

  // ---------------------------------------------------------------- state

  const disabled = new Set();
  const inflight = new Set();
  const observedContainers = new WeakSet();
  const memoryStore = { data: {} };

  let pending = null;
  let config = { userId: null, cw: true, nu: true };
  let prefs = { userId: null, sections: null, loadedAt: 0, loading: false };
  let pollTimer = 0;
  let pollStart = 0;
  let pollDelay = POLL_START_MS;
  let homeObserved = false;
  let nextUpPrefs = { rewatch: false, maxDays: 365 };
  let prefetchDone = false;
  let prefsTimer = 0;
  let scanQueued = false;

  // ---------------------------------------------------------------- helpers

  function degrade(feature, err) {
    if (disabled.has(feature)) {
      return;
    }
    disabled.add(feature);
    try {
      console.debug('[HomeScreenCleanup] feature disabled: ' + feature, err?.message ?? err ?? '');
    } catch (_) {
      // never throw
    }
  }

  function guard(feature, fn) {
    return function () {
      if (disabled.has(feature)) {
        return undefined;
      }
      try {
        return fn.apply(this, arguments);
      } catch (err) {
        degrade(feature, err);
        return undefined;
      }
    };
  }

  function api() {
    return window.ApiClient;
  }

  function currentUserId() {
    try {
      return api()?.getCurrentUserId?.() || null;
    } catch (_) {
      return null;
    }
  }

  function serverId(card) {
    let id = null;
    try {
      id = api()?.serverId?.() || null;
    } catch (_) {
      id = null;
    }
    return id || card?.getAttribute?.('data-serverid') || '';
  }

  function prop(obj, name) {
    if (!obj || typeof obj !== 'object') {
      return undefined;
    }
    const camel = name.charAt(0).toLowerCase() + name.slice(1);
    return obj[name] ?? obj[camel];
  }

  function isTv() {
    return document.documentElement?.classList?.contains('layout-tv') === true;
  }

  function t(key) {
    const lang = String(document.documentElement?.lang || 'en').toLowerCase().slice(0, 2);
    return STRINGS[lang]?.[key] ?? STRINGS.en[key] ?? '';
  }

  function ajax(options) {
    const client = api();
    if (typeof client?.ajax !== 'function' || typeof client?.getUrl !== 'function') {
      return Promise.reject(new Error('ApiClient unavailable'));
    }
    try {
      return Promise.resolve(client.ajax(options));
    } catch (err) {
      return Promise.reject(err);
    }
  }

  function url(route, params) {
    return api()?.getUrl?.(route, params);
  }

  // ---------------------------------------------------------------- styles

  function installStyles() {
    const style = document.createElement('style');
    style.setAttribute('data-hsc', '1');
    style.textContent =
      '.hsc-pending{opacity:.4;pointer-events:none;transition:opacity .2s}' +
      '.hsc-toast{position:fixed;left:50%;bottom:2em;transform:translateX(-50%);z-index:99999;' +
      'max-width:90vw;padding:.8em 1.2em;border-radius:.3em;background:#303030;color:#fff;' +
      'box-shadow:0 2px 8px rgba(0,0,0,.5);font-size:95%}';
    (document.head || document.documentElement).appendChild(style);
  }

  // ---------------------------------------------------------------- client config + display prefs

  function loadClientConfig(force) {
    const userId = currentUserId();
    if (!userId) {
      return;
    }
    if (!force && config.userId === userId) {
      return;
    }
    if (config.userId !== userId) {
      config = { userId: userId, cw: true, nu: true };
    }
    ajax({ type: 'GET', url: url('HomeScreenCleanup/ClientConfig'), dataType: 'json' }).then(function (res) {
      if (currentUserId() !== userId) {
        return;
      }
      const cw = prop(res, 'EnableContinueWatchingAction');
      const nu = prop(res, 'EnableNextUpAction');
      config = { userId: userId, cw: cw !== false, nu: nu !== false };
    }, function () {
      if (currentUserId() === userId) {
        config = { userId: userId, cw: true, nu: true };
      }
    }).catch(function () { /* swallow */ });
  }

  function loadPrefs(force) {
    const userId = currentUserId();
    if (!userId) {
      return;
    }
    const now = Date.now();
    if (prefs.userId !== userId) {
      prefs = { userId: userId, sections: null, loadedAt: 0, loading: false };
    }
    if (prefs.loading) {
      return;
    }
    if (!force && now - prefs.loadedAt < PREFS_MIN_INTERVAL_MS) {
      return;
    }
    const state = prefs;
    prefs.loading = true;
    ajax({
      type: 'GET',
      url: url('DisplayPreferences/usersettings', { userId: userId, client: 'emby' }),
      dataType: 'json'
    }).then(function (res) {
      if (currentUserId() !== userId) {
        return;
      }
      const custom = prop(res, 'CustomPrefs') || {};
      const maxDays = parseInt(custom.maxDaysForNextUp, 10);
      nextUpPrefs = {
        rewatch: String(custom.enableRewatchingInNextUp ?? 'false').toLowerCase() === 'true',
        maxDays: Number.isFinite(maxDays) && maxDays > 0 ? maxDays : 365
      };
      const sections = [];
      for (let i = 0; i < DEFAULT_SECTIONS.length; i++) {
        let value = custom['homesection' + i] || DEFAULT_SECTIONS[i];
        if (value === 'folders') {
          value = DEFAULT_SECTIONS[0];
        }
        sections.push(String(value).toLowerCase());
      }
      prefs = { userId: userId, sections: sections, loadedAt: Date.now(), loading: false };
    }, function () {
      state.loading = false;
      state.loadedAt = Date.now();
    }).catch(function () {
      state.loading = false;
    }).finally(function () {
      state.loading = false;
    });
  }

  function schedulePrefsReload() {
    if (prefsTimer) {
      clearTimeout(prefsTimer);
    }
    prefsTimer = setTimeout(guard('prefs', function () {
      prefsTimer = 0;
      loadPrefs(false);
    }), PREFS_DEBOUNCE_MS);
  }

  function prefetch(force) {
    loadClientConfig(force);
    loadPrefs(force);
  }

  function stopPoll() {
    if (pollTimer) {
      clearTimeout(pollTimer);
      pollTimer = 0;
    }
  }

  function armPoll() {
    if (pollTimer) {
      return;
    }
    pollStart = Date.now();
    pollDelay = POLL_START_MS;
    const tick = guard('poll', function () {
      pollTimer = 0;
      scanContainers();
      const ready = !!currentUserId();
      if (ready && !prefetchDone) {
        prefetchDone = true;
        prefetch(false);
      }
      // Keep polling until the home rows exist and are observed: on a fresh load they render after
      // ApiClient is ready, and the cache filter must be attached before their first fetch resolves.
      if (ready && homeObserved) {
        return;
      }
      if (Date.now() - pollStart >= POLL_GIVE_UP_MS) {
        return;
      }
      pollDelay = Math.min(pollDelay * POLL_FACTOR, POLL_CAP_MS);
      pollTimer = setTimeout(tick, pollDelay);
    });
    pollTimer = setTimeout(tick, pollDelay);
  }

  // ---------------------------------------------------------------- classification

  function sectionIndex(section) {
    const m = SECTION_RE.exec(section?.className || '');
    return m ? parseInt(m[1], 10) : -1;
  }

  function prefsKind(section) {
    if (!prefs.sections || prefs.userId !== currentUserId()) {
      return null;
    }
    const idx = sectionIndex(section);
    if (idx < 0) {
      return null;
    }
    const shifted = idx - (isTv() ? 1 : 0);
    if (shifted < 0) {
      return 'smalllibrarytiles';
    }
    return prefs.sections[shifted] ?? null;
  }

  function cardFits(kind, card) {
    if (!card) {
      return true;
    }
    if (kind === KIND_NEXTUP) {
      return card.getAttribute('data-type') === 'Episode';
    }
    if (kind === KIND_RESUME) {
      return card.getAttribute('data-mediatype') === 'Video' && card.hasAttribute('data-positionticks');
    }
    return false;
  }

  function domKind(section) {
    if (section.querySelector(':scope > .sectionTitleContainer a[href*="type=nextup"]')) {
      return KIND_NEXTUP;
    }
    if (section.querySelector(':scope > h2.sectionTitle')) {
      const ic = section.querySelector('.itemsContainer');
      const monitor = String(ic?.getAttribute?.('data-monitor') || '').split(',');
      if (monitor.indexOf('videoplayback') !== -1) {
        return KIND_RESUME;
      }
    }
    return null;
  }

  /** Returns 'resume' | 'nextup' | null. DOM shape is primary; display prefs veto or fill in. */
  function sectionKind(section, card) {
    if (!section || !section.closest?.('.homeSectionsContainer')) {
      return null;
    }
    const fromDom = domKind(section);
    const fromPrefs = prefsKind(section);
    let kind = null;
    if (fromDom) {
      kind = fromPrefs && fromPrefs !== fromDom ? null : fromDom;
    } else if (fromPrefs === KIND_RESUME || fromPrefs === KIND_NEXTUP) {
      kind = fromPrefs;
    }
    return kind && cardFits(kind, card) ? kind : null;
  }

  function kindEnabled(kind) {
    if (config.userId && config.userId !== currentUserId()) {
      return true;
    }
    return kind === KIND_RESUME ? config.cw : kind === KIND_NEXTUP ? config.nu : false;
  }

  // ---------------------------------------------------------------- removed set (localStorage, 120 s TTL)

  function readStore() {
    let data = null;
    try {
      const raw = window.localStorage?.getItem(STORE_KEY);
      data = raw ? JSON.parse(raw) : {};
    } catch (_) {
      data = memoryStore.data;
    }
    if (!data || typeof data !== 'object' || Array.isArray(data)) {
      data = {};
    }
    const now = Date.now();
    let changed = false;
    Object.keys(data).forEach(function (scope) {
      const list = Array.isArray(data[scope]) ? data[scope] : [];
      const kept = list.filter(function (e) {
        return e && typeof e.id === 'string' && typeof e.exp === 'number' && e.exp > now;
      });
      if (kept.length !== list.length || !Array.isArray(data[scope])) {
        changed = true;
      }
      if (kept.length) {
        data[scope] = kept;
      } else {
        delete data[scope];
        changed = true;
      }
    });
    if (changed) {
      writeStore(data);
    }
    return data;
  }

  function writeStore(data) {
    memoryStore.data = data;
    try {
      window.localStorage?.setItem(STORE_KEY, JSON.stringify(data));
    } catch (_) {
      // private mode / quota: memory copy still applies for this page load
    }
  }

  function scopeKey(card) {
    const userId = currentUserId();
    return userId ? serverId(card) + ':' + userId : null;
  }

  function addRemoved(scope, ids, kind) {
    if (!scope) {
      return;
    }
    const data = readStore();
    const list = Array.isArray(data[scope]) ? data[scope] : [];
    const exp = Date.now() + REMOVED_TTL_MS;
    ids.forEach(function (id) {
      if (typeof id !== 'string' || !id) {
        return;
      }
      const norm = id.toLowerCase();
      const existing = list.find(function (e) { return e.id === norm && e.kind === kind; });
      if (existing) {
        existing.exp = exp;
      } else {
        list.push({ id: norm, kind: kind, exp: exp });
      }
    });
    data[scope] = list;
    writeStore(data);
  }

  function removedIds(scope, kind) {
    const out = new Set();
    if (!scope) {
      return out;
    }
    const list = readStore()[scope];
    if (Array.isArray(list)) {
      list.forEach(function (e) {
        if (e.kind === kind) {
          out.add(e.id);
        }
      });
    }
    return out;
  }

  // ---------------------------------------------------------------- fetchData wrapper (react-query cache workaround)

  function isRemoved(item, ids) {
    const id = String(prop(item, 'Id') ?? '').toLowerCase();
    const sid = String(prop(item, 'SeriesId') ?? '').toLowerCase();
    return (id && ids.has(id)) || (sid && ids.has(sid));
  }

  function filterResult(ic, res) {
    try {
      if (disabled.has('filter')) {
        return res;
      }
      const kind = sectionKind(ic.closest?.('.verticalSection'), null);
      if (!kind) {
        return res;
      }
      const ids = removedIds(scopeKey(null), kind);
      if (!ids.size) {
        return res;
      }
      if (Array.isArray(res)) {
        return res.filter(function (item) { return !isRemoved(item, ids); });
      }
      const items = prop(res, 'Items');
      if (Array.isArray(items)) {
        const kept = items.filter(function (item) { return !isRemoved(item, ids); });
        if (kept.length === items.length) {
          return res;
        }
        const key = 'Items' in res ? 'Items' : 'items';
        const copy = Object.assign({}, res);
        copy[key] = kept;
        return copy;
      }
      return res;
    } catch (err) {
      degrade('filter', err);
      return res;
    }
  }

  // Same requests the web client's home rows make (resume.ts / nextUp.ts in jellyfin-web 12.2), sent
  // straight to the server so a row can be re-rendered from server truth while the 60 s query cache is stale.
  function fetchFresh(kind) {
    const userId = currentUserId();
    if (!userId) {
      return Promise.reject(new Error('no user'));
    }
    let route;
    let params;
    if (kind === KIND_RESUME) {
      route = 'UserItems/Resume';
      params = {
        userId: userId, limit: 12, fields: 'PrimaryImageAspectRatio', imageTypeLimit: 1,
        enableImageTypes: 'Primary,Backdrop,Thumb', enableTotalRecordCount: false, mediaTypes: 'Video'
      };
    } else {
      const cutoff = new Date(Date.now() - nextUpPrefs.maxDays * 86400000).toISOString().slice(0, 10);
      route = 'Shows/NextUp';
      params = {
        userId: userId, limit: 24, fields: 'PrimaryImageAspectRatio,DateCreated,Path,MediaSourceCount',
        imageTypeLimit: 1, enableImageTypes: 'Primary,Backdrop,Thumb', enableTotalRecordCount: false,
        enableResumable: false, enableRewatching: nextUpPrefs.rewatch, nextUpDateCutoff: cutoff
      };
    }
    return ajax({ type: 'GET', url: url(route, params), dataType: 'json' });
  }

  function makeWrapper(ic, inner) {
    return function hscFetchData() {
      const self = this;
      const args = arguments;
      const cached = function () {
        const result = inner.apply(self, args);
        if (result && typeof result.then === 'function') {
          return result.then(function (res) { return filterResult(ic, res); });
        }
        return filterResult(ic, result);
      };
      try {
        // Within the removed-id TTL the cached row is known to be stale: ask the server instead.
        const kind = sectionKind(ic.closest?.('.verticalSection'), null);
        if (kind && removedIds(scopeKey(null), kind).size) {
          return fetchFresh(kind).then(
            function (res) { return filterResult(ic, res); },
            function () { return cached(); }
          );
        }
      } catch (_) {
        // fall through to the cached path
      }
      return cached();
    };
  }

  function wrapContainer(ic) {
    if (!ic || ic[WRAPPED]) {
      return;
    }
    let inner = ic.fetchData;
    let innerRef = null;
    let wrapped = null;
    Object.defineProperty(ic, 'fetchData', {
      configurable: true,
      enumerable: true,
      get: function () {
        if (typeof inner !== 'function') {
          return inner;
        }
        if (innerRef !== inner) {
          innerRef = inner;
          wrapped = makeWrapper(ic, inner);
        }
        return wrapped;
      },
      set: function (value) {
        inner = value;
      }
    });
    ic[WRAPPED] = true;
  }

  function scanContainers() {
    if (disabled.has('wrap')) {
      return;
    }
    try {
      document.querySelectorAll('.homeSectionsContainer').forEach(observeHome);
      document.querySelectorAll('.homeSectionsContainer .itemsContainer').forEach(wrapContainer);
      homeObserved = document.querySelector('.homeSectionsContainer') !== null;
    } catch (err) {
      degrade('wrap', err);
    }
  }

  function queueScan() {
    if (scanQueued) {
      return;
    }
    scanQueued = true;
    queueMicrotask(function () {
      scanQueued = false;
      scanContainers();
    });
  }

  function observeHome(container) {
    if (observedContainers.has(container)) {
      return;
    }
    observedContainers.add(container);
    const mo = new MutationObserver(guard('wrap', function () {
      queueScan();
      schedulePrefsReload();
    }));
    mo.observe(container, { childList: true, subtree: true });
  }

  // ---------------------------------------------------------------- menu intent

  function notePending(origin) {
    pending = null;
    if (!origin?.closest) {
      return;
    }
    const card = origin.closest('.card[data-id]');
    if (!card || !card.closest('.homeSectionsContainer')) {
      return;
    }
    const itemId = card.getAttribute('data-id') || '';
    if (!ID_RE.test(itemId)) {
      return;
    }
    const kind = sectionKind(card.closest('.verticalSection'), card);
    if (!kind) {
      return;
    }
    pending = { card: card, itemId: itemId, kind: kind, ts: Date.now() };
    loadClientConfig(true);
  }

  function onMenuClick(e) {
    const btn = e.target?.closest?.('[data-action="menu"]');
    if (btn) {
      notePending(btn);
    }
  }

  function onCommand(e) {
    if (e?.detail?.command === 'menu') {
      notePending(e.target);
    }
  }

  // ---------------------------------------------------------------- sheet detection + entry injection

  function findSheet(node) {
    if (node?.nodeType !== 1) {
      return null;
    }
    if (node.matches('.dialogContainer .actionSheet')) {
      return node;
    }
    if (node.matches('.dialogContainer')) {
      return node.querySelector('.actionSheet');
    }
    return node.querySelector('.dialogContainer .actionSheet');
  }

  function onBodyMutations(records) {
    if (!pending) {
      return;
    }
    for (const record of records) {
      for (const node of record.addedNodes) {
        const sheet = findSheet(node);
        if (!sheet) {
          continue;
        }
        const p = pending;
        pending = null;
        if (acceptSheet(sheet, p)) {
          guard('inject', function () { inject(sheet, p); })();
        }
        return;
      }
    }
  }

  function acceptSheet(sheet, p) {
    if (!p || Date.now() - p.ts > PENDING_WINDOW_MS || !p.card.isConnected) {
      return false;
    }
    if (!kindEnabled(p.kind)) {
      return false;
    }
    const hasPlayback = Array.prototype.some.call(
      sheet.querySelectorAll('.actionSheetMenuItem[data-id]'),
      function (b) { return PLAYBACK_IDS.has(b.getAttribute('data-id')); }
    );
    if (!hasPlayback) {
      return false;
    }
    if (sheet.querySelector('[data-id^="hsc-"]')) {
      return false;
    }
    return !document.querySelector('.selectionCommandsPanel:not(.hide)');
  }

  function inject(sheet, p) {
    const scroller = sheet.querySelector('.actionSheetScroller');
    if (!scroller) {
      degrade('inject', 'no .actionSheetScroller');
      return;
    }
    const items = Array.prototype.slice.call(scroller.querySelectorAll('.actionSheetMenuItem[data-id]'));
    const playback = items.filter(function (b) { return PLAYBACK_IDS.has(b.getAttribute('data-id')); });
    const anchor = playback[playback.length - 1] || null;
    const sibling = anchor || items[0] || null;
    const spec = ENTRY[p.kind];

    // Plain createElement: jellyfin-web's v0 registerElement polyfill wraps createElement and rejects an options object.
    const btn = document.createElement('button');
    btn.setAttribute('is', 'emby-button');
    btn.type = 'button';
    btn.className = sibling?.className || 'listItem listItem-button actionSheetMenuItem';
    btn.classList.add('emby-button', 'actionSheetMenuItem');
    btn.setAttribute('data-id', spec.dataId);

    const siblingIcon = sibling?.querySelector('.actionsheetMenuItemIcon');
    if (siblingIcon || !sibling) {
      const icon = document.createElement('span');
      icon.className = 'actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons ' + spec.icon;
      icon.setAttribute('aria-hidden', 'true');
      btn.appendChild(icon);
    }

    const siblingBody = sibling?.querySelector('.listItemBody');
    const siblingText = sibling?.querySelector('.listItemBodyText');
    const body = document.createElement('div');
    body.className = siblingBody?.className || 'listItemBody actionsheetListItemBody';
    const text = document.createElement('div');
    text.className = siblingText?.className || 'listItemBodyText actionSheetItemText';
    text.textContent = t(spec.label);
    body.appendChild(text);
    btn.appendChild(body);

    btn.addEventListener('click', guard('action', function () {
      doRemove(p);
    }));

    if (anchor) {
      anchor.after(btn);
    } else {
      scroller.appendChild(btn);
    }
    reclamp(sheet);
  }

  function reclamp(sheet) {
    try {
      const rect = sheet.getBoundingClientRect();
      const vh = window.innerHeight || document.documentElement.clientHeight || 0;
      if (!vh || rect.bottom <= vh) {
        return;
      }
      const current = parseFloat(sheet.style.top);
      const base = Number.isFinite(current) ? current : rect.top;
      sheet.style.top = Math.max(0, base - (rect.bottom - vh)) + 'px';
    } catch (_) {
      // cosmetic only
    }
  }

  // ---------------------------------------------------------------- action

  function errorKey(err) {
    const status = err?.status;
    if (status === 401 || status === 403 || status === 404 || status === 409) {
      return 'e' + status;
    }
    return 'other';
  }

  function toast(message) {
    try {
      if (typeof window.Dashboard?.alert === 'function') {
        window.Dashboard.alert(message);
        return;
      }
    } catch (_) {
      // fall through to inline toast
    }
    try {
      const el = document.createElement('div');
      el.className = 'hsc-toast';
      el.setAttribute('role', 'status');
      el.setAttribute('aria-live', 'polite');
      el.textContent = message;
      document.body.appendChild(el);
      setTimeout(function () { el.remove(); }, TOAST_MS);
    } catch (_) {
      // nothing else to do
    }
  }

  function focusTarget(el) {
    if (!el) {
      return null;
    }
    return el.querySelector('button, [tabindex], a[href]') || el;
  }

  function removeCard(card) {
    const ic = card.closest('.itemsContainer');
    const section = card.closest('.verticalSection');
    const active = document.activeElement;
    const hadFocus = !active || active === document.body || !active.isConnected || card.contains(active);
    let neighbour = card.nextElementSibling;
    while (neighbour && !neighbour.classList.contains('card')) {
      neighbour = neighbour.nextElementSibling;
    }
    if (!neighbour) {
      neighbour = card.previousElementSibling;
      while (neighbour && !neighbour.classList.contains('card')) {
        neighbour = neighbour.previousElementSibling;
      }
    }
    card.remove();
    if (ic && !ic.querySelector('.card')) {
      const parent = ic.parentContainer || section;
      parent?.classList?.add('hide');
      return;
    }
    if (hadFocus && neighbour) {
      try {
        focusTarget(neighbour)?.focus?.({ preventScroll: true });
      } catch (_) {
        // focus is best effort
      }
    }
  }

  function doRemove(p) {
    const card = p.card;
    const kind = p.kind;
    const itemId = p.itemId;
    const key = kind + ':' + itemId;
    if (inflight.has(key)) {
      return;
    }
    if (!api()) {
      toast(t('other'));
      return;
    }
    const scope = scopeKey(card);
    const route = kind === KIND_RESUME
      ? 'HomeScreenCleanup/ContinueWatching/' + encodeURIComponent(itemId) + '/Remove'
      : 'HomeScreenCleanup/NextUp/Hidden/' + encodeURIComponent(itemId);

    inflight.add(key);
    card.classList.add('hsc-pending');

    ajax({ type: 'POST', url: url(route), dataType: 'json' }).then(function (res) {
      try {
        const ids = [itemId];
        if (kind === KIND_RESUME) {
          const reset = prop(res, 'ResetItemIds');
          if (Array.isArray(reset)) {
            reset.forEach(function (id) { ids.push(String(id)); });
          }
        } else {
          const sid = prop(res, 'SeriesId');
          if (sid) {
            ids.push(String(sid));
          }
        }
        addRemoved(scope, ids, kind);
        if (card.isConnected) {
          removeCard(card);
        }
      } catch (err) {
        degrade('action', err);
      }
    }, function (err) {
      card.classList.remove('hsc-pending');
      toast(t(errorKey(err)));
    }).catch(function () { /* swallow */ }).finally(function () {
      inflight.delete(key);
      card.classList.remove('hsc-pending');
    });
  }

  // ---------------------------------------------------------------- page lifecycle

  function onViewShow() {
    scanContainers();
    if (currentUserId()) {
      prefetch(true);
    }
    armPoll();
  }

  function onSettingsChange() {
    loadPrefs(true);
    queueScan();
  }

  // ---------------------------------------------------------------- install

  function install() {
    const ns = {
      name: 'Home Screen Cleanup',
      degraded: function () { return Array.from(disabled); }
    };
    try {
      Object.defineProperty(window, 'HomeScreenCleanup', { value: Object.freeze(ns), configurable: false, writable: false });
    } catch (_) {
      window.HomeScreenCleanup = ns;
    }

    guard('styles', installStyles)();

    document.addEventListener('click', guard('menu', onMenuClick), true);
    document.addEventListener('command', guard('menu', onCommand), true);
    document.addEventListener('viewshow', guard('lifecycle', onViewShow), true);
    document.addEventListener('pageshow', guard('lifecycle', onViewShow), true);
    document.addEventListener('settingschange', guard('lifecycle', onSettingsChange), true);

    guard('sheet', function () {
      const target = document.body || document.documentElement;
      new MutationObserver(guard('sheet', onBodyMutations)).observe(target, { childList: true });
    })();

    scanContainers();
    armPoll();
  }

  try {
    install();
  } catch (err) {
    degrade('install', err);
  }
})();
