/**
 * 尽早恢复主题，避免出现白色/暗色闪烁（FOUC）。
 *
 * 只是 window.AnyDropTheme.init() 的同源薄壳：真正的恢复逻辑在
 * auth-interop.js 的 AnyDropTheme 块里（Web 与 MAUI 共用同一份实现，
 * 块内容由 ThemeContractTests 保证字节一致，存储键 anydrop_theme）。
 * 抽成外部脚本而不用内联 <script>，是为了让 CSP 保持 script-src 'self'。
 * 注意加载顺序：auth-interop.js 必须在前（见 App.razor）。
 * 它仍然是同步加载的（没有 defer/async），因此在 <body> 渲染前执行。
 */
try {
    window.AnyDropTheme.init();
} catch (e) {
    // 隐私模式等场景下 localStorage 可能不可用；忽略即可，不影响页面渲染
}
