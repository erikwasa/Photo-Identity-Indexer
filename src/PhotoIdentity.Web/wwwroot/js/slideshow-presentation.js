(() => {
    const transitionMilliseconds = 600;
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

    function waitForOpacityTransition(image) {
        if (!image || typeof image.addEventListener !== "function") {
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
                image.removeEventListener?.("transitionend", onTransitionEnd);
                resolve();
            };

            const onTransitionEnd = event => {
                if (!event || event.target === image) {
                    finish();
                }
            };

            image.addEventListener("transitionend", onTransitionEnd, { once: true });
            timeoutId = setTimeout(finish, transitionMilliseconds + 100);
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

    async function showPresentationImage(image) {
        if (!image?.style) {
            return false;
        }

        image.style.transition = "none";
        image.style.opacity = "1";
        dispatchVisible(image, reducedMotionRequested(), false);
        return true;
    }

    async function transitionPresentationImages(outgoing, incoming) {
        if (!outgoing?.style || !incoming?.style || outgoing === incoming) {
            return false;
        }

        const reducedMotion = reducedMotionRequested();
        if (reducedMotion) {
            outgoing.style.transition = "none";
            incoming.style.transition = "none";
            outgoing.style.opacity = "0";
            incoming.style.opacity = "1";
            dispatchVisible(incoming, true, false);
            return true;
        }

        outgoing.style.transition = `opacity ${transitionMilliseconds}ms ease`;
        incoming.style.transition = `opacity ${transitionMilliseconds}ms ease`;
        incoming.style.opacity = "0";

        const transitionCompleted = waitForOpacityTransition(incoming);
        await nextAnimationFrame();
        outgoing.style.opacity = "0";
        incoming.style.opacity = "1";
        await transitionCompleted;

        outgoing.style.transition = "";
        incoming.style.transition = "";
        dispatchVisible(incoming, false, true);
        return true;
    }

    function getPresentationImageState(image) {
        const state = image ? presentationStates.get(image) : null;
        return state
            ? { ...state, transitionMilliseconds }
            : {
                readyAt: null,
                visibleAt: null,
                reducedMotion: false,
                animated: false,
                decodeFallback: false,
                transitionMilliseconds
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
