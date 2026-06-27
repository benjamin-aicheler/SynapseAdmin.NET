export function initInfiniteScroll(sentinel, dotnetRef) {
    if (!sentinel || !dotnetRef) return null;

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                dotnetRef.invokeMethodAsync('OnSentinelIntersected');
            }
        });
    }, {
        root: null,
        rootMargin: '300px',
        threshold: 0.1
    });

    observer.observe(sentinel);

    return {
        dispose: () => {
            observer.unobserve(sentinel);
            observer.disconnect();
        }
    };
}
