export function observe(element, dotNetReference) {
    let disposed = false;
    const observer = new IntersectionObserver(entries => {
        if (disposed || !entries.some(entry => entry.isIntersecting)) {
            return;
        }

        observer.disconnect();
        dotNetReference.invokeMethodAsync("OnIntersectingAsync");
    }, {
        root: null,
        rootMargin: "700px 0px",
        threshold: 0
    });

    observer.observe(element);

    return {
        dispose: () => {
            disposed = true;
            observer.disconnect();
        }
    };
}
