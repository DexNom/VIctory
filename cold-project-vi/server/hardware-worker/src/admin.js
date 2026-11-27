// The admin side, a Worker of its own (wrangler.admin.toml) on a hostname protected by Cloudflare
// Access (only edupode's account, with 2FA). Access checks the login before any request reaches
// this code; the Worker also checks Access's signed header, so it refuses to answer if the Access
// application is ever removed.
//   GET /                      hardware: the latest 200 PCs, or ?id=VI-XXXX-XXXX
//   GET /reports               bug reports ("Send to edupode"), ?id= to filter; /reports/<n> shows its summary,
//                              /reports/<n>.zip downloads it, POST /reports/<n>/delete deletes it
//   GET /saves                 the save gallery's queue; POST /saves/<n>/approve, /hide, /delete
// A POST is taken only from this page itself (its Origin header), so another site cannot press the buttons.

import { zipFile } from './zip.js';

async function accessOk(request, env) {
  const jwt = request.headers.get('Cf-Access-Jwt-Assertion');
  if (!jwt) return false;
  const res = await fetch(`https://${env.ACCESS_TEAM}.cloudflareaccess.com/cdn-cgi/access/certs`);
  const { keys } = await res.json();
  const [h, p, s] = jwt.split('.');
  const header = JSON.parse(atob(h.replace(/-/g, '+').replace(/_/g, '/')));
  const payload = JSON.parse(atob(p.replace(/-/g, '+').replace(/_/g, '/')));
  const jwk = keys.find(k => k.kid === header.kid);
  if (!jwk || !payload.aud?.includes(env.ACCESS_AUD) || payload.exp * 1000 < Date.now()) return false;
  const key = await crypto.subtle.importKey('jwk', jwk, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']);
  const sig = Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0));
  return crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, sig, new TextEncoder().encode(`${h}.${p}`));
}

const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const ID = /^VI-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$/;

function page(title, body) {
  return new Response(`<!doctype html><meta charset=utf-8><title>VI admin · ${esc(title)}</title>
<style>body{font:14px system-ui;background:#13171f;color:#e2e8f0;margin:16px}a{color:#ff8fbf}td,th{padding:4px 8px;border-bottom:1px solid #2c3442;text-align:left;vertical-align:top}
nav a{margin-right:16px;font-weight:600}pre{background:#1c2230;padding:12px;border-radius:8px;white-space:pre-wrap;max-width:1100px}
button{background:#2c3442;color:#e2e8f0;border:0;border-radius:6px;padding:4px 10px;cursor:pointer}form{display:inline}.dim{color:#8b95a7}</style>
<nav><a href="/">Hardware</a><a href="/reports">Bug reports</a><a href="/saves">Save gallery</a></nav><h1>${esc(title)}</h1>${body}`,
    { headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' } });
}

const action = (path, label, confirm) =>
  `<form method=post action="${esc(path)}"${confirm ? ` onsubmit="return confirm('${esc(confirm)}')"` : ''}><button>${esc(label)}</button></form>`;

async function hardware(env, id) {
  const rows = id
    ? (await env.DB.prepare('SELECT id, report, first_seen, last_seen FROM hardware WHERE id = ?').bind(id.toUpperCase()).all()).results
    : (await env.DB.prepare('SELECT id, report, first_seen, last_seen FROM hardware ORDER BY last_seen DESC LIMIT 200').all()).results;
  const body = rows.map(r => {
    const h = JSON.parse(r.report);
    return `<tr><td><a href="/reports?id=${esc(r.id)}">${esc(r.id)}</a></td><td>${esc(h.gpu)} (${h.vram_mb} MB, ${esc(h.gpu_driver)})</td><td>${h.rebar_bios}/${h.rebar_d3d12}</td>` +
      `<td>${esc(h.cpu)}</td><td>${h.ram_gb} GB</td><td>${esc(h.board_maker)} ${esc(h.board)}</td><td>${esc(h.os)}</td><td>${esc(h.disk)}${h.laptop ? ' laptop' : ''}</td><td>${esc(r.last_seen)}</td></tr>`;
  }).join('');
  return page('Hardware', `<form><input name=id placeholder="VI-XXXX-XXXX" value="${esc(id || '')}"><button>Search</button></form>
<table><tr><th>ID</th><th>GPU</th><th>ReBAR bios/d3d12</th><th>CPU</th><th>RAM</th><th>Board</th><th>Windows</th><th>Disk</th><th>Last seen</th></tr>${body}</table>`);
}

async function reports(env, id) {
  const rows = id
    ? (await env.DB.prepare('SELECT n, created, vi_id, kind, bytes FROM reports WHERE vi_id = ? ORDER BY n DESC LIMIT 200').bind(id.toUpperCase()).all()).results
    : (await env.DB.prepare('SELECT n, created, vi_id, kind, bytes FROM reports ORDER BY n DESC LIMIT 200').all()).results;
  const body = rows.map(r => `<tr><td><a href="/reports/${r.n}">#${r.n}</a></td><td>${esc(r.created)}</td><td><a href="/reports?id=${esc(r.vi_id)}">${esc(r.vi_id)}</a></td>` +
    `<td>${esc(r.kind)}</td><td>${Math.round(r.bytes / 1024)} KB</td><td><a href="/reports/${r.n}.zip">zip</a> ${action(`/reports/${r.n}/delete`, 'Delete', `Delete report #${r.n}?`)}</td></tr>`).join('');
  return page('Bug reports', `<form action=/reports><input name=id placeholder="VI-XXXX-XXXX" value="${esc(id || '')}"><button>Filter</button></form>
<table><tr><th>#</th><th>Sent</th><th>Player</th><th>Kind</th><th>Size</th><th></th></tr>${body}</table>`);
}

async function reportView(env, n) {
  const r = await env.DB.prepare('SELECT n, created, vi_id, kind, bytes, zip FROM reports WHERE n = ?').bind(n).first();
  if (!r) return new Response(null, { status: 404 });
  const zip = new Uint8Array(r.zip);
  let summary = null, activity = null;
  try { summary = await zipFile(zip, 'summary.txt'); activity = await zipFile(zip, 'launcher-activity.txt'); } catch { }
  return page(`Report #${r.n}`, `<p class=dim>${esc(r.created)} · <a href="/reports?id=${esc(r.vi_id)}">${esc(r.vi_id)}</a> · ${esc(r.kind)} · ${Math.round(r.bytes / 1024)} KB ·
<a href="/reports/${r.n}.zip">download the zip</a> ${action(`/reports/${r.n}/delete`, 'Delete', `Delete report #${r.n}?`)}</p>
<h2>summary.txt</h2><pre>${esc(summary ?? '(not in this report)')}</pre>${activity ? `<h2>launcher-activity.txt</h2><pre>${esc(activity)}</pre>` : ''}`);
}

async function saves(env) {
  const rows = (await env.DB.prepare("SELECT n, created, vi_id, title, note, bytes, state, downloads FROM saves ORDER BY state = 'pending' DESC, n DESC LIMIT 300").all()).results;
  const body = rows.map(r => `<tr><td>#${r.n}</td><td>${esc(r.state)}</td><td><b>${esc(r.title)}</b><br><span class=dim>${esc(r.note)}</span></td>` +
    `<td>${esc(r.created)}</td><td>${esc(r.vi_id)}</td><td>${Math.round(r.bytes / 1024)} KB</td><td>${r.downloads}</td><td>` +
    (r.state !== 'approved' ? action(`/saves/${r.n}/approve`, 'Approve') : '') + (r.state !== 'hidden' ? action(`/saves/${r.n}/hide`, 'Hide') : '') +
    action(`/saves/${r.n}/delete`, 'Delete', `Delete save #${r.n}?`) + ` <a href="/saves/${r.n}.zip">zip</a></td></tr>`).join('');
  return page('Save gallery', `<p class=dim>New saves wait here as pending; only approved ones are public (the gallery page of the public Worker, /saves).</p>
<table><tr><th>#</th><th>State</th><th>Title</th><th>Shared</th><th>Player</th><th>Size</th><th>Downloads</th><th></th></tr>${body}</table>`);
}

const zipResponse = (bytes, name) => new Response(bytes, { headers: { 'content-type': 'application/zip', 'content-disposition': `attachment; filename="${name}"`, 'cache-control': 'no-store' } });

export default {
  async fetch(request, env) {
    if (!['GET', 'POST'].includes(request.method) || !(await accessOk(request, env))) return new Response(null, { status: 403 });
    const url = new URL(request.url);
    const path = url.pathname;
    const id = url.searchParams.get('id');
    if (id && !ID.test(id.toUpperCase())) return new Response('Not an ID', { status: 400 });

    if (request.method === 'POST') {
      if (request.headers.get('Origin') !== url.origin) return new Response(null, { status: 403 });
      let m = path.match(/^\/reports\/(\d{1,9})\/delete$/);
      if (m) { await env.DB.prepare('DELETE FROM reports WHERE n = ?').bind(Number(m[1])).run(); return Response.redirect(url.origin + '/reports', 303); }
      m = path.match(/^\/saves\/(\d{1,9})\/(approve|hide|delete)$/);
      if (m) {
        const n = Number(m[1]);
        if (m[2] === 'delete') await env.DB.prepare('DELETE FROM saves WHERE n = ?').bind(n).run();
        else await env.DB.prepare('UPDATE saves SET state = ? WHERE n = ?').bind(m[2] === 'approve' ? 'approved' : 'hidden', n).run();
        return Response.redirect(url.origin + '/saves', 303);
      }
      return new Response(null, { status: 404 });
    }

    if (path === '/') return hardware(env, id);
    if (path === '/reports') return reports(env, id);
    if (path === '/saves') return saves(env);
    let m = path.match(/^\/reports\/(\d{1,9})$/);
    if (m) return reportView(env, Number(m[1]));
    m = path.match(/^\/reports\/(\d{1,9})\.zip$/);
    if (m) {
      const r = await env.DB.prepare('SELECT vi_id, kind, zip FROM reports WHERE n = ?').bind(Number(m[1])).first();
      return r ? zipResponse(r.zip, `report-${m[1]}-${r.vi_id}-${r.kind}.zip`) : new Response(null, { status: 404 });
    }
    m = path.match(/^\/saves\/(\d{1,9})\.zip$/);
    if (m) {
      const r = await env.DB.prepare('SELECT zip FROM saves WHERE n = ?').bind(Number(m[1])).first();
      return r ? zipResponse(r.zip, `save-${m[1]}.zip`) : new Response(null, { status: 404 });
    }
    return new Response(null, { status: 404 });
  },
};
