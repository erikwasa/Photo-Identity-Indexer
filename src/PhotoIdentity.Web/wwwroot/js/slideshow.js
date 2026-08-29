(() => {
    let dotNetReference = null;
    let keydownHandler = null;
    let visibilityHandler = null;
    let fullscreenHandler = null;
    let gestureHandler = null;
    let prefetchImages = [];
    let startingOrientationType = null;
    let orientationActive = false;
    let orientationFailed = false;
    let orientationMessage = null;
    let orientationMode = null;
    let wakeLockSentinel = null;
    let wakeLockFailed = false;
    let wakeLockMessage = null;
    let fullscreenFailed = false;
    let fullscreenMessage = null;
    let releasingProtections = false;
    let wakeReacquirePending = false;

    function invoke(method, ...args) {
        if (!dotNetReference) {
            return;
        }

        dotNetReference.invokeMethodAsync(method, ...args).catch(() => {
            // The Blazor component may have been disposed during navigation.
        });
    }

    function captureStartingOrientation() {
        if (startingOrientationType) {
            return startingOrientationType;
        }

        const current = window.screen?.orientation?.type;
        if (typeof current === "string" && current.length > 0) {
            startingOrientationType = current;
            return startingOrientationType;
        }

        startingOrientationType = window.innerWidth > window.innerHeight
            ? "landscape"
            : "portrait";
        return startingOrientationType;
    }

    function fullscreenSupported() {
        return typeof document.documentElement?.requestFullscreen === "function";
    }

    function orientationSupported() {
        return !!window.screen?.orientation &&
            typeof window.screen.orientation.lock === "function";
    }

    function wakeLockSupported() {
        return !!navigator.wakeLock &&
            typeof navigator.wakeLock.request === "function";
    }

    function status() {
        return {
            fullscreen: {
                supported: fullscreenSupported(),
                active: document.fullscreenElement !== null,
                failed: fullscreenFailed,
                message: fullscreenMessage,
                mode: null
            },
            orientationLock: {
                supported: orientationSupported(),
                active: orientationActive,
                failed: orientationFailed,
                message: orientationMessage,
                mode: orientationMode
            },
            wakeLock: {
                supported: wakeLockSupported(),
                active: !!wakeLockSentinel && !wakeLockSentinel.released,
                failed: wakeLockFailed,
                message: wakeLockMessage,
                mode: null
            },
            secureContext: window.isSecureContext === true,
            startingOrientation: captureStartingOrientation()
        };
    }

    function notifyStatus() {
        invoke("OnProtectionStatusChanged", status());
    }

    async function acquireOrientationLock() {
        captureStartingOrientation();
        orientationActive = false;
        orientationFailed = false;
        orientationMessage = null;
        orientationMode = null;

        if (!orientationSupported()) {
            orientationMessage = "Screen Orientation lock is not supported by this browser.";
            return;
        }

        if (!document.fullscreenElement) {
            orientationFailed = true;
            orientationMessage = "Orientation lock requires fullscreen on this browser.";
            return;
        }

        const requested = startingOrientationType;
        const family = requested?.startsWith("landscape")
            ? "landscape"
            : requested?.startsWith("portrait")
                ? "portrait"
                : null;

        try {
            await window.screen.orientation.lock(requested);
            orientationActive = true;
            orientationMode = "exact";
            return;
        } catch (exactError) {
            if (!family || family === requested) {
                orientationFailed = true;
                orientationMessage = exactError?.message || "The browser rejected the orientation lock.";
                return;
            }

            try {
                await window.screen.orientation.lock(family);
                orientationActive = true;
                orientationMode = "family";
                orientationMessage = "Exact orientation lock was rejected; the matching orientation family is locked.";
                return;
            } catch (familyError) {
                orientationFailed = true;
                orientationMessage = familyError?.message ||
                    exactError?.message ||
                    "The browser rejected the orientation lock.";
            }
        }
    }

    async function acquireWakeLock() {
        wakeLockFailed = false;
        wakeLockMessage = null;

        if (!wakeLockSupported()) {
            wakeLockMessage = "Screen Wake Lock is not supported by this browser.";
            return;
        }

        if (!window.isSecureContext) {
            wakeLockFailed = true;
            wakeLockMessage = "Screen Wake Lock requires a secure browser context.";
            return;
        }

        if (document.hidden) {
            wakeLockMessage = "Screen Wake Lock will be requested when the document becomes visible.";
            return;
        }

        if (wakeLockSentinel && !wakeLockSentinel.released) {
            return;
        }

        try {
            const sentinel = await navigator.wakeLock.request("screen");
            wakeLockSentinel = sentinel;
            wakeLockFailed = false;
            wakeLockMessage = null;

            sentinel.addEventListener("release", () => {
                if (wakeLockSentinel === sentinel) {
                    wakeLockSentinel = null;
                }

                if (releasingProtections) {
                    return;
                }

                wakeLockFailed = true;
                wakeLockMessage = "The browser or operating system released the screen wake lock.";
                notifyStatus();

                if (!document.hidden && !wakeReacquirePending) {
                    wakeReacquirePending = true;
                    setTimeout(async () => {
                        wakeReacquirePending = false;
                        if (!document.hidden && dotNetReference) {
                            await acquireWakeLock();
                            notifyStatus();
                        }
                    }, 250);
                }
            }, { once: true });
        } catch (error) {
            wakeLockSentinel = null;
            wakeLockFailed = true;
            wakeLockMessage = error?.message || "The browser rejected the screen wake lock.";
        }
    }

    async function acquireProtections() {
        await acquireOrientationLock();
        await acquireWakeLock();
        const current = status();
        notifyStatus();
        return current;
    }

    async function releaseProtections() {
        releasingProtections = true;
        try {
            if (orientationSupported()) {
                try {
                    window.screen.orientation.unlock();
                } catch {
                    // Best effort release.
                }
            }

            orientationActive = false;
            orientationFailed = false;
            orientationMessage = null;
            orientationMode = null;

            if (wakeLockSentinel && !wakeLockSentinel.released) {
                try {
                    await wakeLockSentinel.release();
                } catch {
                    // Best effort release.
                }
            }

            wakeLockSentinel = null;
            wakeLockFailed = false;
            wakeLockMessage = null;
            startingOrientationType = null;
        } finally {
            releasingProtections = false;
        }
    }

    function clearPrefetch() {
        for (const image of prefetchImages) {
            image.src = "";
        }
        prefetchImages = [];
    }

    window.photoIdentitySlideshow = {
        requestFullscreen: async () => {
            captureStartingOrientation();

            if (document.fullscreenElement) {
                fullscreenFailed = false;
                fullscreenMessage = null;
                return true;
            }

            const element = document.documentElement;
            if (!fullscreenSupported()) {
                fullscreenFailed = true;
                fullscreenMessage = "Fullscreen is not supported by this browser.";
                return false;
            }

            try {
                await element.requestFullscreen();
                const active = document.fullscreenElement !== null;
                fullscreenFailed = !active;
                fullscreenMessage = active ? null : "Fullscreen did not become active.";
                return active;
            } catch (error) {
                fullscreenFailed = true;
                fullscreenMessage = error?.message || "The browser rejected fullscreen.";
                return false;
            }
        },

        isFullscreen: () => document.fullscreenElement !== null,

        getProtectionStatus: () => status(),

        acquireProtections,

        releaseProtections,

        exitFullscreen: async () => {
            if (!document.fullscreenElement || !document.exitFullscreen) {
                return;
            }

            try {
                await document.exitFullscreen();
            } catch {
                // Navigation away from the slideshow remains available even if exit fails.
            }
        },

        register: (reference) => {
            window.photoIdentitySlideshow.unregister(false);
            dotNetReference = reference;
            captureStartingOrientation();

            keydownHandler = event => {
                if (event.ctrlKey &&
                    event.shiftKey &&
                    event.key.toLowerCase() === "x") {
                    event.preventDefault();
                    invoke("OnParentShortcut");
                    return;
                }

                if (event.key !== "ArrowLeft" &&
                    event.key !== "ArrowRight" &&
                    event.key !== " ") {
                    return;
                }

                event.preventDefault();
                invoke("OnSlideshowKey", event.key);
            };

            visibilityHandler = async () => {
                const visible = !document.hidden;
                invoke("OnDocumentVisibilityChanged", visible);

                if (visible) {
                    await acquireWakeLock();
                    notifyStatus();
                }
            };

            fullscreenHandler = () => {
                const active = document.fullscreenElement !== null;
                if (active) {
                    fullscreenFailed = false;
                    fullscreenMessage = null;
                } else {
                    orientationActive = false;
                }

                invoke("OnFullscreenChanged", active);
                notifyStatus();
            };

            gestureHandler = event => {
                event.preventDefault();
            };

            document.addEventListener("keydown", keydownHandler, { passive: false });
            document.addEventListener("visibilitychange", visibilityHandler);
            document.addEventListener("fullscreenchange", fullscreenHandler);
            document.addEventListener("gesturestart", gestureHandler, { passive: false });
            document.addEventListener("gesturechange", gestureHandler, { passive: false });
            document.addEventListener("gestureend", gestureHandler, { passive: false });
        },

        setPrefetchUrls: urls => {
            clearPrefetch();
            const bounded = Array.isArray(urls) ? urls.slice(0, 4) : [];
            prefetchImages = bounded.map(url => {
                const image = new Image();
                image.decoding = "async";
                image.src = url;
                return image;
            });
        },

        unregister: async (release = true) => {
            if (keydownHandler) {
                document.removeEventListener("keydown", keydownHandler);
            }
            if (visibilityHandler) {
                document.removeEventListener("visibilitychange", visibilityHandler);
            }
            if (fullscreenHandler) {
                document.removeEventListener("fullscreenchange", fullscreenHandler);
            }
            if (gestureHandler) {
                document.removeEventListener("gesturestart", gestureHandler);
                document.removeEventListener("gesturechange", gestureHandler);
                document.removeEventListener("gestureend", gestureHandler);
            }

            keydownHandler = null;
            visibilityHandler = null;
            fullscreenHandler = null;
            gestureHandler = null;
            dotNetReference = null;
            clearPrefetch();

            if (release) {
                await releaseProtections();
            }
        }
    };
})();
