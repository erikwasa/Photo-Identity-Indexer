// WI-0155: browser-only, explicit user-triggered video proxy seek.
// Never captures frames, writes storage, fetches source originals, or uploads pixels.
window.photoIdentityPersistedFaceReview = Object.freeze({
    seek(video, seconds) {
        if (!(video instanceof HTMLVideoElement) ||
            !Number.isFinite(seconds) || seconds < 0 || seconds > 86400) {
            throw new Error("Invalid bounded video observation seek.");
        }
        const go = () => {
            const end = Number.isFinite(video.duration) && video.duration > 0
                ? Math.max(0, video.duration - 0.1)
                : seconds;
            video.pause();
            video.currentTime = Math.min(seconds, end);
        };
        if (video.readyState >= HTMLMediaElement.HAVE_METADATA) {
            go();
        } else {
            video.addEventListener("loadedmetadata", go, { once: true });
            video.load();
        }
    }
});
