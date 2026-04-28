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
 * 主题管理：读取/写入 localStorage，并同步切换 <html> 上的 .dark 类。
 *
 * @example
 *   // 页面加载后初始化（恢复上次偏好）
 *   AnyDropTheme.init();
 *
 *   // 从 Blazor 切换主题
 *   await jsRuntime.InvokeVoidAsync("AnyDropTheme.set", isDark);
 *
 *   // 读取当前主题（返回 boolean）
 *   var isDark = await jsRuntime.InvokeAsync<bool>("AnyDropTheme.get");
 */
window.AnyDropTheme = {
  /** 从 localStorage 恢复已保存的主题偏好，并应用到 <html> 元素。 */
  init: function () {
    var dark = localStorage.getItem('theme') === 'dark';
    document.documentElement.classList.toggle('dark', dark);
  },
  /**
   * 设置当前主题。
   * @param {boolean} isDark - true 为暗色，false 为亮色
   */
  set: function (isDark) {
    localStorage.setItem('theme', isDark ? 'dark' : 'light');
    document.documentElement.classList.toggle('dark', isDark);
  },
  /**
   * 获取当前主题偏好。
   * @returns {boolean} true 表示当前为暗色主题
   */
  get: function () {
    return localStorage.getItem('theme') === 'dark';
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
