const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const scripts = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

function page(url, { consent = false, loading = false, iframe = false } = {}) {
    const elements = new Map();
    const timers = [];
    const listeners = [];
    const document = {
        readyState: loading ? 'loading' : 'complete',
        querySelectorAll: () => consent ? [{ hidden: false, getClientRects: () => [{}] }] : [],
        getElementById: id => elements.get(id),
        createElement: () => ({}),
        head: { appendChild: element => elements.set(element.id, element) },
        addEventListener: (...args) => listeners.push(args)
    };
    const messages = [];
    const window = { chrome: { webview: { postMessage: message => messages.push(message) } } };
    window.top = iframe ? {} : window;
    const context = vm.createContext({
        window, document, location: new URL(url), URLSearchParams,
        getComputedStyle: () => ({ display: 'block', visibility: 'visible' }),
        setInterval: callback => { timers.push(callback); return timers.length; },
        clearInterval: () => {}
    });
    return { context, elements, timers, listeners, messages, dismissConsent: () => { consent = false; } };
}

const watch = 'https://www.youtube.com/watch?v=abcdefghijk';
for (const url of [
    'https://consent.youtube.com/m?continue=' + encodeURIComponent(watch),
    'https://consent.google.com/m',
    'https://accounts.google.com/ServiceLogin',
    'https://www.youtube.com/signin',
    'https://www.youtube.com/watch?v=another-id'
]) {
    const state = page(url);
    assert.equal(vm.runInContext(scripts.predicate, state.context), false, url);
    assert.equal(vm.runInContext(scripts.kiosk, state.context), false, url);
    vm.runInContext(scripts.early, state.context);
    assert.equal(state.elements.size, 0, 'No player CSS on ' + url);
}
for (const options of [{ consent: true }, { loading: true }, { iframe: true }]) {
    const state = page(watch, options);
    assert.equal(vm.runInContext(scripts.predicate, state.context), false);
    assert.equal(vm.runInContext(scripts.kiosk, state.context), false);
    vm.runInContext(scripts.early, state.context);
    assert.equal(state.elements.size, 0);
}
const state = page(watch, { consent: true });
vm.runInContext(scripts.early, state.context);
assert.equal(state.elements.size, 0);
state.dismissConsent();
assert.equal(vm.runInContext(scripts.predicate, state.context), true);
state.timers.forEach(callback => callback());
assert.ok(state.elements.has('jdbd-player-layout'), 'Resume after consent without navigation');
assert.equal(vm.runInContext(scripts.predicate, page(watch).context), true);
const normal = page(watch);
assert.equal(vm.runInContext(scripts.kiosk, normal.context), true);
assert.equal(normal.elements.size, 1, 'Only the application stylesheet is inserted');
assert.equal(normal.listeners.length, 2, 'Only keyboard handlers; no player lifecycle overrides');
for (let i = 0; i < 20; i++) assert.equal(vm.runInContext(scripts.kiosk, normal.context), true);
assert.equal(normal.listeners.length, 2, 'Repeated initialization cannot duplicate handlers');
let prevented = false;
normal.listeners[0][1]({ type: 'keydown', key: 'h', code: 'KeyH', target: null,
    preventDefault() { prevented = true; }, stopImmediatePropagation() {} });
assert.ok(prevented);
assert.deepEqual(normal.messages, ['jdbd:toggle-effects-panel']);
// Native playback keys must reach YouTube; the app must not emit synthetic clicks or seeks.
normal.listeners[0][1]({ type: 'keydown', key: ' ', code: 'Space', target: null,
    preventDefault() { throw new Error('Playback key was intercepted'); } });
console.log('PASS document policy: consent hosts, login, wrong video, modal, loading, iframe, resume');
const mediaPage = page(watch);
vm.runInContext('class HTMLVideoElement {}; globalThis.HTMLVideoElement = HTMLVideoElement', mediaPage.context);
vm.runInContext(scripts.media, mediaPage.context);
vm.runInContext(`globalThis.video = new HTMLVideoElement(); Object.assign(video, {
    currentTime: 42, readyState: 2, networkState: 2, paused: false, seeking: false,
    buffered: { length: 0 }, error: { code: 3 }
}); Object.freeze(video);`, mediaPage.context);
assert.equal(mediaPage.listeners.length, 8);
const errorListener = mediaPage.listeners.find(([kind]) => kind === 'error')[1];
errorListener({ target: vm.runInContext('video', mediaPage.context) });
const diagnostic = JSON.parse(mediaPage.messages[0].slice('jdbd:media:'.length));
assert.equal(diagnostic.error, 3);
assert.equal(diagnostic.current, 42);
assert.ok(!mediaPage.messages[0].includes('https:'), 'No signed URLs in diagnostics');
console.log('PASS media diagnostics report decode errors without modifying playback');
