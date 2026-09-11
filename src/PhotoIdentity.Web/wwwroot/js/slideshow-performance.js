(() => {
    const maximumSamples = 50;
    let sequence = 0;
    let imageStarts = new WeakMap();
    let pendingSamples = [];

    function flushSamples() {
        if (pendingSamples.length === 0) {
            return;
        }

        const samples = pendingSamples;
        pendingSamples = [];

        fetch("/api/slideshows/diagnostics/playback", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ samples }),
            credentials: "same-origin",
            keepalive: true
        }).catch(() => {
            // Diagnostics are best-effort and must never affect slideshow playback.
        });
    }

    function resetSession() {
        flushSamples();
        sequence = 0;
        imageStarts = new WeakMap();
    }

    function latestResourceTiming(image) {
        const resourceUrl = image.currentSrc || image.src;
        if (!resourceUrl || typeof performance?.getEntriesByName !== "function") {
            return null;
        }

        const entries = performance.getEntriesByName(resourceUrl, "resource");
        if (!Array.isArray(entries) || entries.length === 0) {
            return null;
        }

        return entries[entries.length - 1];
    }

    function currentPrefetchState(image) {
        const resourceUrl = image.currentSrc || image.src;
        const getState = window.photoIdentitySlideshow?.getPrefetchState;
        if (!resourceUrl || typeof getState !== "function") {
            return null;
        }

        try {
            return getState(resourceUrl);
        } catch {
            return null;
        }
    }

    function completeSample(image) {
        const sample = imageStarts.get(image);
        if (!sample) {
            return;
        }

        imageStarts.delete(image);
        const completedAt = performance.now();
        const presentationMilliseconds = Math.max(0, completedAt - sample.startedAt);
        const resource = latestResourceTiming(image);
        const resourceMilliseconds = resource && Number.isFinite(resource.duration)
            ? Math.max(0, resource.duration)
            : null;
        const prefetched = sample.prefetchState?.known === true &&
            sample.prefetchState?.completed === true &&
            Number.isFinite(sample.prefetchState?.completedAt) &&
            sample.prefetchState.completedAt <= sample.startedAt;

        pendingSamples.push({
            sequence: sample.sequence,
            presentationMilliseconds,
            resourceMilliseconds,
            prefetched
        });

        if (pendingSamples.length >= maximumSamples) {
            flushSamples();
        }
    }

    function observeImage(image) {
        if (!(image instanceof HTMLImageElement) ||
            !image.classList.contains("slideshow-image") ||
            imageStarts.has(image) ||
            sequence >= maximumSamples) {
            return;
        }

        sequence++;
        imageStarts.set(image, {
            sequence,
            startedAt: performance.now(),
            prefetchState: currentPrefetchState(image)
        });

        if (image.complete && image.naturalWidth > 0) {
            queueMicrotask(() => completeSample(image));
            return;
        }

        image.addEventListener("load", () => completeSample(image), { once: true });
        image.addEventListener("error", () => imageStarts.delete(image), { once: true });
    }

    function containsSlideshowShell(node) {
        return node instanceof Element &&
            (node.classList.contains("slideshow-shell") ||
             node.querySelector(".slideshow-shell") !== null);
    }

    function observeNode(node) {
        if (!(node instanceof Element)) {
            return;
        }

        if (node.classList.contains("slideshow-shell")) {
            resetSession();
        }

        if (node.matches("img.slideshow-image")) {
            observeImage(node);
        }

        for (const image of node.querySelectorAll("img.slideshow-image")) {
            observeImage(image);
        }
    }

    const observer = new MutationObserver(mutations => {
        let slideshowRemoved = false;
        for (const mutation of mutations) {
            for (const node of mutation.removedNodes) {
                slideshowRemoved ||= containsSlideshowShell(node);
            }
        }

        if (slideshowRemoved) {
            flushSamples();
        }

        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                observeNode(node);
            }
        }
    });

    observer.observe(document.documentElement, {
        childList: true,
        subtree: true
    });

    window.addEventListener("pagehide", flushSamples);
    document.addEventListener("visibilitychange", () => {
        if (document.hidden) {
            flushSamples();
        }
    });

    for (const shell of document.querySelectorAll(".slideshow-shell")) {
        observeNode(shell);
    }
})();
