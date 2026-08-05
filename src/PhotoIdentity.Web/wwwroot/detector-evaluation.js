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

function configureDetectorComparisonPan(viewport) {
    if (viewport.__detectorComparisonPanBound) {
        return;
    }

    viewport.__detectorComparisonPanBound = true;
    let pointerId = null;
    let startX = 0;
    let startY = 0;
    let startScrollLeft = 0;
    let startScrollTop = 0;

    viewport.addEventListener("pointerdown", event => {
        const target = event.target instanceof Element ? event.target : null;
        if (viewport.dataset.panEnabled !== "true" || event.button !== 0 || target?.closest(".comparison-box")) {
            return;
        }

        pointerId = event.pointerId;
        startX = event.clientX;
        startY = event.clientY;
        startScrollLeft = viewport.scrollLeft;
        startScrollTop = viewport.scrollTop;
        viewport.setPointerCapture(pointerId);
        viewport.classList.add("is-panning");
        event.preventDefault();
    });

    viewport.addEventListener("pointermove", event => {
        if (pointerId !== event.pointerId) {
            return;
        }

        viewport.scrollLeft = startScrollLeft - (event.clientX - startX);
        viewport.scrollTop = startScrollTop - (event.clientY - startY);
    });

    const endPan = event => {
        if (pointerId !== event.pointerId) {
            return;
        }

        if (viewport.hasPointerCapture(pointerId)) {
            viewport.releasePointerCapture(pointerId);
        }
        pointerId = null;
        viewport.classList.remove("is-panning");
    };

    viewport.addEventListener("pointerup", endPan);
    viewport.addEventListener("pointercancel", endPan);
}

function configureDetectorComparisonResize(viewport, stage, image) {
    viewport.__detectorComparisonStage = stage;
    viewport.__detectorComparisonImage = image;
    if (viewport.__detectorComparisonResizeObserver) {
        return;
    }

    viewport.__detectorComparisonResizeObserver = new ResizeObserver(() => {
        if (viewport.dataset.zoomScale !== "0") {
            return;
        }

        window.detectorComparison.applyZoom(
            viewport,
            viewport.__detectorComparisonStage,
            viewport.__detectorComparisonImage,
            0);
    });
    viewport.__detectorComparisonResizeObserver.observe(viewport);
}

window.detectorComparison = {
    applyZoom(viewport, stage, image, zoomScale) {
        if (!viewport || !stage || !image) {
            throw new Error("Detector comparison zoom surface is unavailable.");
        }

        const naturalWidth = image.naturalWidth;
        const naturalHeight = image.naturalHeight;
        if (!Number.isFinite(naturalWidth) || naturalWidth <= 0 ||
            !Number.isFinite(naturalHeight) || naturalHeight <= 0) {
            return;
        }

        configureDetectorComparisonPan(viewport);
        configureDetectorComparisonResize(viewport, stage, image);

        const previousBounds = stage.getBoundingClientRect();
        const centerX = previousBounds.width > 0
            ? (viewport.scrollLeft + (viewport.clientWidth / 2)) / previousBounds.width
            : 0.5;
        const centerY = previousBounds.height > 0
            ? (viewport.scrollTop + (viewport.clientHeight / 2)) / previousBounds.height
            : 0.5;

        const requestedScale = Number(zoomScale);
        const fitMode = !Number.isFinite(requestedScale) || requestedScale <= 0;
        const availableWidth = Math.max(1, viewport.clientWidth - 2);
        const availableHeight = Math.max(1, viewport.clientHeight - 2);
        const fitScale = Math.min(availableWidth / naturalWidth, availableHeight / naturalHeight);
        const effectiveScale = fitMode ? fitScale : requestedScale;
        const targetWidth = Math.max(1, Math.floor(naturalWidth * effectiveScale));
        const targetHeight = Math.max(1, Math.floor(naturalHeight * effectiveScale));

        viewport.dataset.zoomScale = fitMode ? "0" : String(requestedScale);
        stage.style.width = `${targetWidth}px`;
        stage.style.height = `${targetHeight}px`;
        stage.style.maxWidth = "none";
        stage.style.marginLeft = "auto";
        stage.style.marginRight = "auto";
        stage.style.marginTop = fitMode
            ? `${Math.max(0, Math.floor((availableHeight - targetHeight) / 2))}px`
            : "0px";
        viewport.dataset.panEnabled = fitMode ? "false" : "true";

        if (fitMode) {
            viewport.scrollLeft = 0;
            viewport.scrollTop = 0;
            return;
        }

        requestAnimationFrame(() => {
            const currentBounds = stage.getBoundingClientRect();
            viewport.scrollLeft = Math.max(0, (centerX * currentBounds.width) - (viewport.clientWidth / 2));
            viewport.scrollTop = Math.max(0, (centerY * currentBounds.height) - (viewport.clientHeight / 2));
        });
    },

    resetWorkspace(workspace, viewport, decisionPanel) {
        if (viewport) {
            viewport.scrollLeft = 0;
            viewport.scrollTop = 0;
        }
        if (decisionPanel) {
            decisionPanel.scrollTop = 0;
        }
        if (workspace) {
            workspace.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "auto" });
        }
    },

    focusDecision(elementId) {
        const decision = document.getElementById(elementId);
        if (!decision) {
            throw new Error(`Detector comparison decision '${elementId}' was not found.`);
        }

        const focusTarget = decision.matches(":disabled")
            ? decision.closest(".comparison-resolution-row")
            : decision;
        if (!focusTarget) {
            throw new Error(`Detector comparison decision '${elementId}' has no focusable review row.`);
        }
        if (!focusTarget.hasAttribute("tabindex") && focusTarget !== decision) {
            focusTarget.setAttribute("tabindex", "-1");
        }

        focusTarget.focus({ preventScroll: true });
        focusTarget.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "auto" });
    }
};
