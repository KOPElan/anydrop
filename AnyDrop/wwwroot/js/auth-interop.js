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
    }
};

/**
 * 主题管理：读取/写入 localStorage，并同步切换 <html> 上的 .dark 类。
 */
window.AnyDropTheme = {
  init: function () {
    var dark = localStorage.getItem('theme') === 'dark';
    document.documentElement.classList.toggle('dark', dark);
  },
  set: function (isDark) {
    localStorage.setItem('theme', isDark ? 'dark' : 'light');
    document.documentElement.classList.toggle('dark', isDark);
  },
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
