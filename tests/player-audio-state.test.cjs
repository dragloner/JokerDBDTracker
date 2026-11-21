const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const scripts = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const nextTurn = () => new Promise(resolve => setImmediate(resolve));

class Element {
    constructor() {
        this.style = { setProperty(k, v) { this[k] = v; }, removeProperty(k) { delete this[k]; } };
        this.classList = { contains: name => name === 'html5-main-video' };
        this.isConnected = true;
        this.listeners = {};
    }
    getBoundingClientRect() { return { left: 0, top: 0, right: 1280, bottom: 720, width: 1280, height: 720 }; }
    closest() { return null; }
    addEventListener(kind, fn) { this.listeners[kind] = fn; }
    removeEventListener(kind) { delete this.listeners[kind]; }
}
class Video extends Element {
    constructor() { super(); this.playbackRate = 1.5; this.readyState = 4; }
}
class Canvas extends Element { getContext() { return null; } }
function videoPage() {
    const video = new Video();
    const videos = [video];
    const window = { getComputedStyle: () => ({ display: 'block', visibility: 'visible', opacity: '1', filter: 'none' }) };
    const nodes = new Map();
    const body = { appendChild(node) { nodes.set(node.id, node); node.parentElement = body; },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 1280, height: 720 }) };
    const document = {
        querySelectorAll: () => videos, getElementById: id => nodes.get(id),
        querySelector: () => videos[0], body, documentElement: body,
        createElement: tag => tag === 'canvas' ? new Canvas() : new Element()
    };
    const context = vm.createContext({ window, document, HTMLVideoElement: Video, HTMLElement: Element, HTMLCanvasElement: Canvas,
        cancelAnimationFrame() {}, requestAnimationFrame() { return 1; },
        performance: { now: () => 0 } });
    return { context, video, videos, window };
}
const page = videoPage();
assert.equal(vm.runInContext(scripts.neutralVideo, page.context), true, 'Neutral video state acknowledges success');
assert.equal(page.video.playbackRate, 1.5, 'Neutral effects preserve user playback speed');
for (let i = 0; i < 5; i++) {
    vm.runInContext(scripts.pitchVideo, page.context);
    assert.equal(page.video.playbackRate, 3, 'Pitch is relative to the unpitched base');
    page.video.listeners.ratechange();
    vm.runInContext(scripts.neutralVideo, page.context);
    assert.equal(page.video.playbackRate, 1.5, 'Reset restores the user rate without accumulating pitch');
    page.video.listeners.ratechange();
}
console.log('PASS video reset, stable pitch cycles, user playback rate');
const replacement = new Video();
replacement.playbackRate = 1.25;
page.videos.splice(0, 1, replacement);
vm.runInContext(scripts.neutralVideo, page.context);
assert.equal(page.window.__stwfxRuntime.video, replacement, 'Replace a still-connected stale video reference');
assert.equal(replacement.playbackRate, 1.25);
vm.runInContext(scripts.edgeVideo, page.context);
assert.equal(page.window.__stwfxRuntime.fisheyeCenterX, 0, 'Left edge is a valid fisheye center');
assert.equal(page.window.__stwfxRuntime.fisheyeCenterY, 0, 'Top edge is a valid fisheye center');
assert.equal(replacement.style.filter, 'grayscale(1)', 'Preserve basic filters when a post-FX canvas cannot render');
console.log('PASS video replacement, fisheye edges and post-FX fallback');

class AudioNode {
    constructor() {
        for (const name of ['gain', 'frequency', 'Q', 'delayTime', 'playbackRate']) this[name] = { value: 1 };
    }
    connect() { return this; }
    disconnect() {}
    start() {}
    stop() { this.stopped = true; }
}
function soundPage(deferred = false) {
    const contexts = [], sources = [], messages = [];
    let finishDecode;
    const decodePromise = deferred ? new Promise(resolve => { finishDecode = resolve; }) : Promise.resolve({});
    class AudioContext {
        constructor() { this.state = 'suspended'; this.sampleRate = 100; this.destination = new AudioNode(); this.impulses = 0; contexts.push(this); }
        async resume() { this.state = 'running'; }
        async close() { this.state = 'closed'; }
        async decodeAudioData() { return decodePromise; }
        createBufferSource() { const source = new AudioNode(); sources.push(source); return source; }
        createGain() { return new AudioNode(); }
        createBiquadFilter() { return new AudioNode(); }
        createWaveShaper() { return new AudioNode(); }
        createDelay() { return new AudioNode(); }
        createConvolver() { return new AudioNode(); }
        createOscillator() { return new AudioNode(); }
        createBuffer(channels, length) { this.impulses++; return { numberOfChannels: channels, getChannelData: () => new Float32Array(length) }; }
    }
    const window = { AudioContext, chrome: { webview: { postMessage: m => messages.push(m) } } };
    return { window, contexts, sources, messages, finishDecode,
        context: vm.createContext({ window, atob: value => Buffer.from(value, 'base64').toString('binary') }) };
}
async function play(state, script) {
    await vm.runInContext(script, state.context);
    assert.deepEqual(state.messages, [], 'Sound script did not report errors');
}
(async () => {
    const state = soundPage();
    await play(state, scripts.coloredSound);
    assert.equal(state.sources[0].playbackRate.value, 2);
    assert.equal(state.contexts[1].impulses, 1);
    for (let i = 0; i < 5; i++) vm.runInContext(scripts.eqSync, state.context);
    assert.equal(state.contexts[1].impulses, 1, 'Equalizer updates do not recreate reverb buffers');
    vm.runInContext(scripts.neutralSync, state.context);
    assert.equal(state.sources[0].playbackRate.value, 1, 'Reset updates a currently playing sound');
    await play(state, scripts.neutralSound); // toggle off
    await play(state, scripts.neutralSound); // start neutral
    assert.equal(state.sources.at(-1).playbackRate.value, 1, 'New playback never reuses stale pitch');
    await play(state, scripts.otherSound);
    assert.equal(state.window.__jdbdSfx.count, 2);
    const previous = state.sources.at(-2);
    await play(state, scripts.neutralSound); // stop Aura, retain Laugh
    assert.equal(state.window.__jdbdSfx.count, 1);
    previous.onended();
    assert.equal(state.window.__jdbdSfx.count, 1, 'Late ended event cannot decrement another sound');
    assert.equal(state.contexts[0].state, 'suspended', 'Decode-only context needs no playback activation');

    const pending = soundPage(true);
    const first = vm.runInContext(scripts.neutralSound, pending.context);
    await nextTurn();
    await vm.runInContext(scripts.neutralSound, pending.context); // cancel while decoding
    const third = vm.runInContext(scripts.neutralSound, pending.context);
    await nextTurn();
    assert.ok(pending.window.__jdbdSfx.decoding.AuraFarm);
    pending.finishDecode({});
    await Promise.all([first, third]);
    assert.equal(pending.sources.length, 1, 'Only latest uncancelled request starts');
    assert.equal(pending.window.__jdbdSfx.count, 1);
    assert.equal(pending.window.__jdbdSfx.decoding.AuraFarm, false);
    console.log('PASS sound reset, stale parameters, late ended events, decode cancellation and restart');
})().catch(error => { console.error(error); process.exitCode = 1; });
