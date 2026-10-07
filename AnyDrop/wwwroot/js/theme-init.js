/**
 * 尽早恢复主题，避免出现白色/暗色闪烁（FOUC）。
 *
 * 这段逻辑原先内联在 <head> 中，而内联脚本要求 CSP 放开
 * script-src 'unsafe-inline'。抽成同源外部脚本后即可使用 script-src 'self'。
 * 它仍然是同步加载的（没有 defer/async），因此在 <body> 渲染前执行。
 */
(function () {
    'use strict';

    try {
        if (localStorage.getItem('theme') === 'dark') {
            document.documentElement.classList.add('dark');
        }
    } catch (e) {
        // 隐私模式等场景下 localStorage 可能不可用；忽略即可，不影响页面渲染
    }
})();
