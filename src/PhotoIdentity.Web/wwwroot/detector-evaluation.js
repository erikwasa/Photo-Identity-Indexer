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
    }
};
