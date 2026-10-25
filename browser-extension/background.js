// Hands links to Arqon through its own arqon:// scheme. Arqon registers the scheme in
// Settings -> "Open torrents and magnet links with Arqon"; the browser asks once whether to open Arqon.
const api = globalThis.browser ?? globalThis.chrome;

const MENU_LINK = "arqon-link";

/** Only web and magnet links go to Arqon (the same rule Arqon itself applies to arqon:// links). */
function isSendable(url) {
  return typeof url === "string" && /^(https?:\/\/|magnet:\?)/i.test(url);
}

function arqonLink(url) {
  return "arqon://add?url=" + encodeURIComponent(url);
}

async function send(url, tabId) {
  if (!isSendable(url)) return false;
  let target = tabId;
  if (target === undefined || target < 0) {
    const [active] = await api.tabs.query({ active: true, lastFocusedWindow: true });
    target = active?.id;
  }
  if (target === undefined) {
    await api.tabs.create({ url: arqonLink(url), active: false });
  } else {
    // An external scheme does not leave the page: the browser only asks to open Arqon.
    await api.tabs.update(target, { url: arqonLink(url) });
  }
  return true;
}

api.runtime.onInstalled.addListener(() => {
  api.contextMenus.create({
    id: MENU_LINK,
    title: api.i18n.getMessage("menuLink"),
    contexts: ["link"],
  });
});

api.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === MENU_LINK) send(info.linkUrl, tab?.id);
});

// Optional: every download the browser starts goes to Arqon instead (off by default; set in the options).
api.downloads.onCreated.addListener(async (item) => {
  const { catchAll = false } = await api.storage.sync.get("catchAll");
  const url = item.finalUrl || item.url;
  if (!catchAll || !isSendable(url)) return;   // blob: and data: downloads stay in the browser
  try {
    await api.downloads.cancel(item.id);
    await api.downloads.erase({ id: item.id });
  } catch {
    return;   // already finished or gone: leave it to the browser
  }
  await send(url);
});
