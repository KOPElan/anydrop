export function scrollTimelineToBottom(elementId) {
    const container = document.getElementById(elementId);
    if (!container) {
        return;
    }

    container.scrollTop = container.scrollHeight;
}
