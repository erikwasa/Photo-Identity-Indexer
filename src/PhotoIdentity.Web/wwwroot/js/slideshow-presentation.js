(() => {
    const transitionMilliseconds = 600;
    const chapterTransitionMilliseconds = 850;
    const presentationStates = new WeakMap();

    function now() {
        return typeof performance?.now === "function"
            ? performance.now()
            : Date.now();
    }

    function stateFor(image) {
        let state = presentationStates.get(image);
        if (!state) {
            state = {
                readyAt: null,
                visibleAt: null,
                reducedMotion: false,
                animated: false,
                decodeFallback: false
            };
            presentationStates.set(image, state);
        }
        return state;
    }

    function reducedMotionRequested() {
        try {
            return window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches === true;
        } catch {
            return false;
        }
    }

    function nextAnimationFrame() {
        if (typeof requestAnimationFrame !== "function") {
            return Promise.resolve();
        }

        return new Promise(resolve => requestAnimationFrame(() => resolve()));
    }

    function transitionDuration(mode) {
        return mode === "chapter"
            ? chapterTransitionMilliseconds
            : transitionMilliseconds;
    }

    function waitForOpacityTransition(element, durationMilliseconds) {
        if (!element || typeof element.addEventListener !== "function") {
            return Promise.resolve();
        }

        return new Promise(resolve => {
            let settled = false;
            let timeoutId = null;

            const finish = () => {
                if (settled) {
                    return;
                }
                settled = true;
                if (timeoutId !== null) {
                    clearTimeout(timeoutId);
                }
                element.removeEventListener?.("transitionend", onTransitionEnd);
                resolve();
            };

            const onTransitionEnd = event => {
                if (!event || event.target === element) {
                    finish();
                }
            };

            element.addEventListener("transitionend", onTransitionEnd, { once: true });
            timeoutId = setTimeout(finish, durationMilliseconds + 100);
        });
    }

    function dispatchVisible(image, reducedMotion, animated) {
        if (!image) {
            return;
        }

        const state = stateFor(image);
        state.visibleAt = now();
        state.reducedMotion = reducedMotion;
        state.animated = animated;

        if (image.dataset) {
            image.dataset.photoIdentityPresentationReadyAt = state.readyAt?.toString() ?? "";
            image.dataset.photoIdentityPresentationVisibleAt = state.visibleAt.toString();
        }

        if (typeof image.dispatchEvent === "function" && typeof CustomEvent === "function") {
            image.dispatchEvent(new CustomEvent("photoidentity:slideshow-visible", {
                bubbles: true,
                detail: {
                    readyAt: state.readyAt,
                    visibleAt: state.visibleAt,
                    reducedMotion,
                    animated
                }
            }));
        }
    }

    async function decodePresentationImage(image) {
        if (!image || image.complete !== true || !(image.naturalWidth > 0)) {
            return false;
        }

        let decodeFallback = false;
        if (typeof image.decode === "function") {
            try {
                await image.decode();
            } catch {
                // Some browsers reject decode() even after a successful load. Keep the
                // loaded-pixel fallback rather than turning a displayable image into a
                // slideshow failure; genuine resource failures still arrive via error.
                decodeFallback = true;
            }
        }

        if (image.complete !== true || !(image.naturalWidth > 0)) {
            return false;
        }

        const state = stateFor(image);
        state.readyAt = now();
        state.visibleAt = null;
        state.reducedMotion = false;
        state.animated = false;
        state.decodeFallback = decodeFallback;

        if (image.dataset) {
            image.dataset.photoIdentityPresentationReadyAt = state.readyAt.toString();
            image.dataset.photoIdentityPresentationVisibleAt = "";
        }

        return true;
    }

    async function showPresentationImage(layer, visibleImage = layer) {
        if (!layer?.style || !visibleImage) {
            return false;
        }

        layer.style.transition = "none";
        layer.style.opacity = "1";
        dispatchVisible(visibleImage, reducedMotionRequested(), false);
        return true;
    }

    async function transitionPresentationImages(
        outgoing,
        incoming,
        visibleImage = incoming,
        transitionMode = "standard") {
        if (!outgoing?.style || !incoming?.style || outgoing === incoming || !visibleImage) {
            return false;
        }

        const reducedMotion = reducedMotionRequested();
        if (reducedMotion) {
            outgoing.style.transition = "none";
            incoming.style.transition = "none";
            outgoing.style.opacity = "0";
            incoming.style.opacity = "1";
            dispatchVisible(visibleImage, true, false);
            return true;
        }

        const durationMilliseconds = transitionDuration(transitionMode);
        outgoing.style.transition = `opacity ${durationMilliseconds}ms ease`;
        incoming.style.transition = `opacity ${durationMilliseconds}ms ease`;
        incoming.style.opacity = "0";

        const transitionCompleted = waitForOpacityTransition(incoming, durationMilliseconds);
        await nextAnimationFrame();
        outgoing.style.opacity = "0";
        incoming.style.opacity = "1";
        await transitionCompleted;

        outgoing.style.transition = "";
        incoming.style.transition = "";
        const state = stateFor(visibleImage);
        state.transitionMilliseconds = durationMilliseconds;
        state.transitionMode = transitionMode === "chapter" ? "chapter" : "standard";
        dispatchVisible(visibleImage, false, true);
        return true;
    }

    function getPresentationImageState(image) {
        const state = image ? presentationStates.get(image) : null;
        return state
            ? {
                ...state,
                transitionMilliseconds: state.transitionMilliseconds ?? transitionMilliseconds,
                transitionMode: state.transitionMode ?? "standard"
            }
            : {
                readyAt: null,
                visibleAt: null,
                reducedMotion: false,
                animated: false,
                decodeFallback: false,
                transitionMilliseconds,
                transitionMode: "standard"
            };
    }

    window.photoIdentitySlideshow = window.photoIdentitySlideshow || {};
    Object.assign(window.photoIdentitySlideshow, {
        decodePresentationImage,
        showPresentationImage,
        transitionPresentationImages,
        getPresentationImageState
    });
})();
