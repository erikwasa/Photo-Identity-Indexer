(() => {
    let dotNetReference = null;
    let keydownHandler = null;
    let visibilityHandler = null;
    let fullscreenHandler = null;
    let prefetchImages = [];

    function invoke(method, ...args) {
        if (!dotNetReference) {
            return;
        }

        dotNetReference.invokeMethodAsync(method, ...args).catch(() => {
            // The Blazor component may have been disposed during navigation.
        });
    }

    function clearPrefetch() {
        for (const image of prefetchImages) {
            image.src = "";
        }
        prefetchImages = [];
    }

    window.photoIdentitySlideshow = {
        requestFullscreen: async () => {
            if (document.fullscreenElement) {
                return true;
            }

            const element = document.documentElement;
            if (!element.requestFullscreen) {
                return false;
            }

            try {
                await element.requestFullscreen();
                return document.fullscreenElement !== null;
            } catch {
                return false;
            }
        },

        isFullscreen: () => document.fullscreenElement !== null,

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
            window.photoIdentitySlideshow.unregister();
            dotNetReference = reference;

            keydownHandler = event => {
                if (event.key !== "ArrowLeft" &&
                    event.key !== "ArrowRight" &&
                    event.key !== " ") {
                    return;
                }

                event.preventDefault();
                invoke("OnSlideshowKey", event.key);
            };

            visibilityHandler = () => {
                invoke("OnDocumentVisibilityChanged", !document.hidden);
            };

            fullscreenHandler = () => {
                invoke("OnFullscreenChanged", document.fullscreenElement !== null);
            };

            document.addEventListener("keydown", keydownHandler, { passive: false });
            document.addEventListener("visibilitychange", visibilityHandler);
            document.addEventListener("fullscreenchange", fullscreenHandler);
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

        unregister: () => {
            if (keydownHandler) {
                document.removeEventListener("keydown", keydownHandler);
            }
            if (visibilityHandler) {
                document.removeEventListener("visibilitychange", visibilityHandler);
            }
            if (fullscreenHandler) {
                document.removeEventListener("fullscreenchange", fullscreenHandler);
            }

            keydownHandler = null;
            visibilityHandler = null;
            fullscreenHandler = null;
            dotNetReference = null;
            clearPrefetch();
        }
    };
})();
