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

async function toResult(response) {
    let body = null;
    try {
        body = await response.json();
    } catch {
        body = null;
    }

    return {
        ok: response.ok,
        status: response.status,
        body
    };
}
