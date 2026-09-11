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
        FakeImage.instances.push(this);
    }

    addEventListener(name, callback) {
        this.listeners.set(name, callback);
    }

    get src() {
        return this._src;
    }

    set src(value) {
        this._src = value;
    }

    completeLoad() {
        this.listeners.get("load")?.();
    }
}

let clock = 0;
global.window = {};
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

const scriptPath = path.resolve(
    __dirname,
    "../../src/PhotoIdentity.Web/wwwroot/js/slideshow.js");
vm.runInThisContext(fs.readFileSync(scriptPath, "utf8"), { filename: scriptPath });

const slideshow = global.window.photoIdentitySlideshow;

async function reset() {
    await slideshow.unregister(false);
    FakeImage.instances.length = 0;
    clock = 0;
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

    // Repeating the same desired set is a true no-op and must not age out the
    // retained generation while the newly-current image is still loading.
    slideshow.setPrefetchUrls(["/c", "/d"]);
    assert.equal(FakeImage.instances.length, 4);
    assert.equal(firstA.src, "http://localhost/a");
    assert.equal(firstB.src, "http://localhost/b");

    // The next actual prefetch-set change releases entries that have remained
    // outside the desired set for a complete generation.
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
