const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

class FakeImage {
    static instances = [];

    constructor() {
        this.decoding = "";
        this.listeners = new Map();
        this._src = "";
        this.complete = false;
        this.naturalWidth = 0;
        this.style = {};
        this.dataset = {};
        this.decodeCalls = 0;
        this.decodeRejects = false;
        FakeImage.instances.push(this);
    }

    addEventListener(name, callback) {
        const callbacks = this.listeners.get(name) ?? new Set();
        callbacks.add(callback);
        this.listeners.set(name, callbacks);
    }

    removeEventListener(name, callback) {
        this.listeners.get(name)?.delete(callback);
    }

    dispatchEvent(event) {
        event.target = this;
        for (const callback of [...(this.listeners.get(event.type) ?? [])]) {
            callback(event);
        }
        return true;
    }

    get src() {
        return this._src;
    }

    set src(value) {
        this._src = value;
    }

    async decode() {
        this.decodeCalls++;
        if (this.decodeRejects) {
            throw new Error("decode failed");
        }
    }

    completeLoad() {
        this.complete = true;
        this.naturalWidth = 100;
        this.dispatchEvent({ type: "load" });
    }

    completeTransition() {
        this.dispatchEvent({ type: "transitionend" });
    }
}

class FakeCustomEvent {
    constructor(type, options = {}) {
        this.type = type;
        this.detail = options.detail ?? null;
        this.bubbles = options.bubbles === true;
        this.target = null;
    }
}

let clock = 0;
let reducedMotion = false;
global.window = {
    matchMedia: () => ({ matches: reducedMotion })
};
global.document = {
    baseURI: "http://localhost/",
    documentElement: {},
    fullscreenElement: null,
    addEventListener() {},
    removeEventListener() {},
};
global.navigator = {};
global.performance = {
    now: () => ++clock,
};
global.Image = FakeImage;
global.CustomEvent = FakeCustomEvent;
global.requestAnimationFrame = callback => {
    callback();
    return 1;
};

const slideshowScriptPath = path.resolve(
    __dirname,
    "../../src/PhotoIdentity.Web/wwwroot/js/slideshow.js");
vm.runInThisContext(
    fs.readFileSync(slideshowScriptPath, "utf8"),
    { filename: slideshowScriptPath });

const presentationScriptPath = path.resolve(
    __dirname,
    "../../src/PhotoIdentity.Web/wwwroot/js/slideshow-presentation.js");
vm.runInThisContext(
    fs.readFileSync(presentationScriptPath, "utf8"),
    { filename: presentationScriptPath });

const slideshow = global.window.photoIdentitySlideshow;

async function reset() {
    await slideshow.unregister(false);
    FakeImage.instances.length = 0;
    clock = 0;
    reducedMotion = false;
}

test("identical prefetch sets do not restart Image requests", async () => {
    await reset();

    slideshow.setPrefetchUrls(["/a", "/b"]);
    assert.equal(FakeImage.instances.length, 2);

    slideshow.setPrefetchUrls(["/a", "/b"]);
    assert.equal(FakeImage.instances.length, 2);
    assert.equal(FakeImage.instances[0].src, "http://localhost/a");
    assert.equal(FakeImage.instances[1].src, "http://localhost/b");
});

test("previous desired generation survives one actual set change", async () => {
    await reset();

    slideshow.setPrefetchUrls(["/a", "/b"]);
    const firstA = FakeImage.instances[0];
    const firstB = FakeImage.instances[1];

    slideshow.setPrefetchUrls(["/c", "/d"]);
    assert.equal(FakeImage.instances.length, 4);
    assert.equal(firstA.src, "http://localhost/a");
    assert.equal(firstB.src, "http://localhost/b");

    slideshow.setPrefetchUrls(["/c", "/d"]);
    assert.equal(FakeImage.instances.length, 4);
    assert.equal(firstA.src, "http://localhost/a");
    assert.equal(firstB.src, "http://localhost/b");

    slideshow.setPrefetchUrls(["/d", "/e"]);
    assert.equal(firstA.src, "");
    assert.equal(firstB.src, "");
    assert.equal(FakeImage.instances.length, 5);
});

test("explicit prefetch state reports completion for browser diagnostics", async () => {
    await reset();

    slideshow.setPrefetchUrls(["/a"]);
    const image = FakeImage.instances[0];
    const before = slideshow.getPrefetchState("/a");
    assert.equal(before.known, true);
    assert.equal(before.completed, false);

    image.completeLoad();
    const after = slideshow.getPrefetchState("http://localhost/a");
    assert.equal(after.known, true);
    assert.equal(after.completed, true);
    assert.equal(typeof after.completedAt, "number");
});

test("presentation decode must complete before an image is ready", async () => {
    await reset();

    const image = new FakeImage();
    assert.equal(await slideshow.decodePresentationImage(image), false);

    image.complete = true;
    image.naturalWidth = 100;
    assert.equal(await slideshow.decodePresentationImage(image), true);
    assert.equal(image.decodeCalls, 1);

    const state = slideshow.getPresentationImageState(image);
    assert.equal(typeof state.readyAt, "number");
    assert.equal(state.visibleAt, null);
});

test("decode rejection keeps the staged image from being presented", async () => {
    await reset();

    const image = new FakeImage();
    image.complete = true;
    image.naturalWidth = 100;
    image.decodeRejects = true;

    assert.equal(await slideshow.decodePresentationImage(image), false);
    const state = slideshow.getPresentationImageState(image);
    assert.equal(state.readyAt, null);
    assert.equal(state.visibleAt, null);
});

test("initial presentation shows a decoded image without animation", async () => {
    await reset();

    const image = new FakeImage();
    image.complete = true;
    image.naturalWidth = 100;
    await slideshow.decodePresentationImage(image);

    let visibleEvent = null;
    image.addEventListener("photoidentity:slideshow-visible", event => {
        visibleEvent = event.detail;
    });

    assert.equal(await slideshow.showPresentationImage(image), true);
    assert.equal(image.style.opacity, "1");
    assert.equal(image.style.transition, "none");
    assert.equal(visibleEvent.animated, false);
    assert.ok(visibleEvent.visibleAt >= visibleEvent.readyAt);
});

test("crossfade keeps the outgoing image until the decoded incoming layer is visible", async () => {
    await reset();

    const outgoing = new FakeImage();
    outgoing.complete = true;
    outgoing.naturalWidth = 100;
    await slideshow.decodePresentationImage(outgoing);
    await slideshow.showPresentationImage(outgoing);

    const incoming = new FakeImage();
    incoming.complete = true;
    incoming.naturalWidth = 100;
    await slideshow.decodePresentationImage(incoming);

    let visibleEvent = null;
    incoming.addEventListener("photoidentity:slideshow-visible", event => {
        visibleEvent = event.detail;
    });

    const transition = slideshow.transitionPresentationImages(outgoing, incoming);
    await Promise.resolve();
    incoming.completeTransition();

    assert.equal(await transition, true);
    assert.equal(outgoing.style.opacity, "0");
    assert.equal(incoming.style.opacity, "1");
    assert.equal(visibleEvent.animated, true);
    assert.equal(visibleEvent.reducedMotion, false);
    assert.ok(visibleEvent.visibleAt >= visibleEvent.readyAt);
});

test("reduced motion swaps only after readiness and skips crossfade animation", async () => {
    await reset();
    reducedMotion = true;

    const outgoing = new FakeImage();
    outgoing.complete = true;
    outgoing.naturalWidth = 100;
    await slideshow.decodePresentationImage(outgoing);
    await slideshow.showPresentationImage(outgoing);

    const incoming = new FakeImage();
    incoming.complete = true;
    incoming.naturalWidth = 100;
    await slideshow.decodePresentationImage(incoming);

    assert.equal(
        await slideshow.transitionPresentationImages(outgoing, incoming),
        true);
    assert.equal(outgoing.style.opacity, "0");
    assert.equal(incoming.style.opacity, "1");

    const state = slideshow.getPresentationImageState(incoming);
    assert.equal(state.reducedMotion, true);
    assert.equal(state.animated, false);
    assert.ok(state.visibleAt >= state.readyAt);
});
