const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const scripts = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

function page(url, { consent = false, loading = false, iframe = false } = {}) {
    const elements = new Map();
    const timers = [];
    const document = {
        readyState: loading ? 'loading' : 'complete',
        querySelectorAll: () => consent ? [{ hidden: false, getClientRects: () => [{}] }] : [],
        getElementById: id => elements.get(id),
        createElement: () => ({}),
        head: { appendChild: element => elements.set(element.id, element) }
    };
    const window = {};
    window.top = iframe ? {} : window;
    const context = vm.createContext({
        window, document, location: new URL(url), URLSearchParams,
        getComputedStyle: () => ({ display: 'block', visibility: 'visible' }),
        setInterval: callback => { timers.push(callback); return timers.length; },
        clearInterval: () => {}
    });
    return { context, elements, timers, dismissConsent: () => { consent = false; } };
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
assert.ok(state.elements.has('jdbd-early-immersive-style'), 'Resume after consent without navigation');
assert.equal(vm.runInContext(scripts.predicate, page(watch).context), true);
console.log('PASS document policy: consent hosts, login, wrong video, modal, loading, iframe, resume');
