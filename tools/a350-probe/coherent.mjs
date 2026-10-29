// The sim's Coherent GT debugger (http://127.0.0.1:19999), shared by this folder's Node scripts.
// - Every request and evaluation has a timeout: a dead process squatting the port hangs a bare fetch or socket for ever.
// - An OIS view is told apart by the page it loads (ini-efb-a350-cpt / -fo), never by its VCockpit number [A350-8].
// - Coherent takes ONE inspector socket per page [DBG-9]: close MSFSBA's window on that view (its OIS window, opened with
//   Shift+T or Ctrl+Shift+T) or MSFSBA itself first, or this socket and MSFSBA's fight over the page.
// COHERENT_PORT overrides the port (a test against a closed port shows the timeouts).
const PORT = process.env.COHERENT_PORT || '19999';
const BASE = `127.0.0.1:${PORT}`;

export const ONE_SOCKET_NOTE = 'Coherent takes one inspector socket per page: close MSFSBA\'s OIS window (or MSFSBA) before this script.';

/** A promise that fails with `what` after `ms`. */
export function within(promise, ms, what) {
  let timer;
  const late = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`${what}: no answer in ${ms} ms`)), ms); });
  return Promise.race([promise, late]).finally(() => clearTimeout(timer));
}

/** The debugger's pages ({ id, title, ... }). */
export async function pages(ms = 4000) {
  let r;
  try { r = await fetch(`http://${BASE}/pagelist.json`, { signal: AbortSignal.timeout(ms) }); }
  catch (e) { throw new Error(`no Coherent debugger at ${BASE} (${e.message}): is the sim running with the A350 loaded?`); }
  if (!r.ok) throw new Error(`pagelist.json: HTTP ${r.status}`);
  return r.json();
}

/** An inspector socket on a page: eval(expr) answers the expression's value; close() lets the page go. */
export async function open(page, ms = 4000) {
  const ws = new WebSocket(`ws://${BASE}/devtools/inspector/${page.id}`);
  let id = 0;
  const pending = new Map();
  ws.onmessage = (m) => {
    const d = JSON.parse(m.data);
    const done = pending.get(d.id);
    if (!done) return;
    pending.delete(d.id);
    if (d.result && d.result.wasThrown) done.reject(new Error('thrown: ' + JSON.stringify(d.result.result)));
    else done.resolve(d.result && d.result.result ? d.result.result.value : undefined);
  };
  await within(new Promise((resolve, reject) => {
    ws.onopen = resolve;
    ws.onerror = () => reject(new Error(`no socket on "${page.title}" (held by another client? ${ONE_SOCKET_NOTE})`));
  }), ms, `socket on "${page.title}"`);
  return {
    eval(expression, evalMs = 15000) {
      const i = ++id;
      const answer = new Promise((resolve, reject) => pending.set(i, { resolve, reject }));
      ws.send(JSON.stringify({ id: i, method: 'Runtime.evaluate', params: { expression, returnByValue: true, awaitPromise: true } }));
      return within(answer, evalMs, `evaluate on "${page.title}"`).finally(() => pending.delete(i));
    },
    close() { try { ws.close(); } catch { /* already gone */ } },
  };
}

/** The OIS view of a side ('cpt' or 'fo'), found by the page it loads. */
export async function oisView(side = 'cpt', ms = 4000) {
  const candidates = (await pages(ms)).filter(p => p.title.includes('iniEfbA350'));
  for (const page of candidates) {
    let s;
    try {
      s = await open(page, ms);
      const loaded = await s.eval("(document.documentElement.innerHTML.match(/ini-efb-a350-(cpt|fo)/) || [''])[0]", ms);
      if (loaded === 'ini-efb-a350-' + side) return page;
    } catch { /* that view is held or not an OIS: try the next */ } finally { if (s) s.close(); }
  }
  throw new Error(`no OIS view for ${side} among ${candidates.length} iniEfbA350 views. ${ONE_SOCKET_NOTE}`);
}

/** A page by title needle, or "ois:cpt" / "ois:fo" for that side's OIS view; the index picks among several matches. */
export async function find(needle, index = 0, ms = 4000) {
  if (needle.startsWith('ois:')) return oisView(needle.slice(4), ms);
  const hits = (await pages(ms)).filter(p => p.title.includes(needle));
  if (!hits[index]) throw new Error(`no view "${needle}" #${index} (${hits.length} match)`);
  return hits[index];
}
