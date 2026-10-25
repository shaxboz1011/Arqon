const api = globalThis.browser ?? globalThis.chrome;

for (const el of document.querySelectorAll("[data-i18n]")) el.textContent = api.i18n.getMessage(el.dataset.i18n);

const box = document.getElementById("catchAll");
api.storage.sync.get("catchAll").then(({ catchAll = false }) => { box.checked = catchAll; });
box.addEventListener("change", () => api.storage.sync.set({ catchAll: box.checked }));
