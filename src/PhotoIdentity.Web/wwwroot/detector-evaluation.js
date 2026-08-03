window.detectorEvaluation = {
    getNormalizedPoint(element, clientX, clientY) {
        if (!element) {
            throw new Error("Detector evaluation image stage is unavailable.");
        }

        const bounds = element.getBoundingClientRect();
        if (bounds.width <= 0 || bounds.height <= 0) {
            throw new Error("Detector evaluation image stage has invalid dimensions.");
        }

        const x = Math.min(1, Math.max(0, (clientX - bounds.left) / bounds.width));
        const y = Math.min(1, Math.max(0, (clientY - bounds.top) / bounds.height));
        return [x, y];
    },

    applyZoom(viewport, stage, image, zoomScale) {
        if (!viewport || !stage || !image) {
            throw new Error("Detector evaluation zoom surface is unavailable.");
        }

        const naturalWidth = image.naturalWidth;
        if (!Number.isFinite(naturalWidth) || naturalWidth <= 0) {
            return;
        }

        const previousWidth = stage.getBoundingClientRect().width;
        const previousHeight = stage.getBoundingClientRect().height;
        const centerX = previousWidth > 0
            ? (viewport.scrollLeft + (viewport.clientWidth / 2)) / previousWidth
            : 0.5;
        const centerY = previousHeight > 0
            ? (viewport.scrollTop + (viewport.clientHeight / 2)) / previousHeight
            : 0.5;

        const requestedScale = Number(zoomScale);
        const fitMode = !Number.isFinite(requestedScale) || requestedScale <= 0;
        const targetWidth = fitMode
            ? Math.max(1, viewport.clientWidth)
            : Math.max(1, naturalWidth * requestedScale);

        stage.style.width = `${Math.round(targetWidth)}px`;
        stage.style.maxWidth = "none";

        if (fitMode) {
            viewport.scrollLeft = 0;
            viewport.scrollTop = 0;
            return;
        }

        requestAnimationFrame(() => {
            const currentWidth = stage.getBoundingClientRect().width;
            const currentHeight = stage.getBoundingClientRect().height;
            viewport.scrollLeft = Math.max(0, (centerX * currentWidth) - (viewport.clientWidth / 2));
            viewport.scrollTop = Math.max(0, (centerY * currentHeight) - (viewport.clientHeight / 2));
        });
    }
};
