/**
 * 图片加载失败时的回退处理。
 *
 * 这些行为原先写成内联 onerror 属性，而内联事件处理器要求 CSP 放开
 * script-src 'unsafe-inline'——那会大幅削弱 CSP 对 XSS 的防护。
 * 这里改为在捕获阶段统一监听 error 事件，于是可以使用 script-src 'self'。
 *
 * 声明式属性：
 *   data-fallback-src   失败时改用它作为图片地址（只尝试一次，避免循环）
 *   data-fallback-style 失败时套用占位样式，目前仅支持 "placeholder"
 *
 * 注意：元素上的 error 事件不会冒泡，因此必须使用捕获阶段。
 */
(function () {
    'use strict';

    document.addEventListener('error', function (event) {
        var element = event.target;
        if (!element || element.tagName !== 'IMG') {
            return;
        }

        // 只回退一次，否则备用地址也失败时会反复触发
        if (element.dataset.fallbackApplied === 'true') {
            return;
        }

        var fallbackSrc = element.getAttribute('data-fallback-src');
        if (fallbackSrc) {
            element.dataset.fallbackApplied = 'true';
            element.src = fallbackSrc;
            return;
        }

        if (element.getAttribute('data-fallback-style') === 'placeholder') {
            element.dataset.fallbackApplied = 'true';
            element.style.minHeight = '120px';
            element.style.background = 'rgba(0,0,0,0.3)';
        }
    }, true);
})();
