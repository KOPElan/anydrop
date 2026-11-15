window.authInterop = {
    postJson: async function (url, payload) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify(payload)
        });
        return await toResult(response);
    },
    putJson: async function (url, payload) {
        const response = await fetch(url, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify(payload)
        });
        return await toResult(response);
    },
    getJson: async function (url) {
        const response = await fetch(url, {
            method: "GET",
            credentials: "include"
        });
        return await toResult(response);
    },
    deleteJson: async function (url, payload) {
        const options = {
            method: "DELETE",
            credentials: "include"
        };
        if (payload !== undefined && payload !== null) {
            options.headers = { "Content-Type": "application/json" };
            options.body = JSON.stringify(payload);
        }
        const response = await fetch(url, options);
        return await toResult(response);
    }
};

/**
 * 主题切换的唯一 JS 契约（S3-06）：Web 与 MAUI 两端共用同一份实现，
 * 块内容由 ThemeContractTests 保证字节一致，改一处必须改两处。
 *
 *   init()      按 localStorage 恢复偏好并应用到 <html>（启动时在 Blazor 之前调用，避免闪烁）
 *   set(isDark) 应用并持久化（isDark 为 bool）
 *   get()       返回当前是否为暗色（bool）
 *
 * 存储键 'anydrop_theme'。2026-11-15 前 Web 端用的是 'theme'，统一后老用户
 * 的已存偏好会重置一次（默认亮色），属一次性迁移。
 */
window.AnyDropTheme = {
  init: function () {
    var dark = localStorage.getItem('anydrop_theme') === 'dark';
    document.documentElement.classList.toggle('dark', dark);
  },
  set: function (isDark) {
    localStorage.setItem('anydrop_theme', isDark ? 'dark' : 'light');
    document.documentElement.classList.toggle('dark', isDark);
  },
  get: function () {
    return localStorage.getItem('anydrop_theme') === 'dark';
  }
};

async function toResult(response) {
    let body = null;
    try {
        const contentType = response.headers.get("content-type") || "";
        if (contentType.includes("application/json")) {
            body = await response.json();
        } else {
            // not JSON (likely HTML error page or redirect), capture as text for diagnostics
            const text = await response.text();
            body = { error: `Unexpected non-JSON response: ${text.slice(0, 200)}` };
        }
    } catch (e) {
        body = { error: e?.message ?? 'Failed to parse response' };
    }

    return {
        ok: response.ok,
        status: response.status,
        body
    };
}
