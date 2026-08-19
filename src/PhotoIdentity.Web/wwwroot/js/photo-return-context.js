(() => {
    const archiveRoot = '/archive';

    const isArchiveReturn = value =>
        value === archiveRoot ||
        value.startsWith(`${archiveRoot}?`) ||
        value.startsWith(`${archiveRoot}#`);

    const applyLabel = () => {
        if (!location.pathname.startsWith('/photo/')) {
            return;
        }

        const returnUrl = new URLSearchParams(location.search).get('returnUrl');
        if (!returnUrl || !isArchiveReturn(returnUrl)) {
            return;
        }

        const link = document.querySelector('.collection-hero > a.button.secondary');
        if (link instanceof HTMLAnchorElement && isArchiveReturn(link.getAttribute('href') ?? '')) {
            link.textContent = 'Back to archive';
        }
    };

    applyLabel();

    const observer = new MutationObserver(() => applyLabel());
    observer.observe(document.body, {
        childList: true,
        subtree: true,
    });

    window.addEventListener('popstate', applyLabel);
})();
