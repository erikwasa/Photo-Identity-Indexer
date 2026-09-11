(() => {
    const maximumSamples = 50;
    let sequence = 0;
    let imageStarts = new WeakMap();

    function resetSession() {
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

    function submitSample(image) {
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
        const prefetched = !!resource &&
            Number.isFinite(resource.responseEnd) &&
            resource.responseEnd > 0 &&
            resource.responseEnd <= sample.startedAt;

        const body = {
            sequence: sample.sequence,
            presentationMilliseconds,
            resourceMilliseconds,
            prefetched
        };

        fetch("/api/slideshows/diagnostics/playback", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(body),
            credentials: "same-origin",
            keepalive: true
        }).catch(() => {
            // Diagnostics are best-effort and must never affect slideshow playback.
        });
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
            startedAt: performance.now()
        });

        if (image.complete && image.naturalWidth > 0) {
            queueMicrotask(() => submitSample(image));
            return;
        }

        image.addEventListener("load", () => submitSample(image), { once: true });
        image.addEventListener("error", () => imageStarts.delete(image), { once: true });
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

    for (const shell of document.querySelectorAll(".slideshow-shell")) {
        observeNode(shell);
    }
})();
