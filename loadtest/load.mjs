// Load test: 50 simulated users on one 500-card board, against a running API.
//
//   node loadtest/load.mjs
//
// Environment: BASE (default http://localhost:5099), VUS (50), DURATION_S (30), CARDS (500).
// The API must have a high auth rate limit (RateLimit__AuthPermitPerMinute=100000), because setup registers every user
// from one address. Needs Node 22 (global fetch). Writes loadtest/results.json.
//
// Each user loops: 70% load the board, 20% move one of their own cards, 10% open the activity feed,
// then "thinks" for 100-300 ms. Cards are split between users, so moves never collide on purpose
// (409s are therefore reported as errors: they would mean something is wrong).

import { writeFileSync } from 'node:fs';

const BASE = process.env.BASE ?? 'http://localhost:5099';
const VUS = Number(process.env.VUS ?? 50);
const DURATION_S = Number(process.env.DURATION_S ?? 30);
const CARDS = Number(process.env.CARDS ?? 500);
const RUN = Date.now().toString(36);

async function call(method, path, { token, body, clientId = 'load' } = {}) {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      'X-Requested-With': 'load',
      'X-Client-Id': clientId,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  return { status: response.status, ms: performance.now() - started, bytes: text.length, json: text ? safeJson(text) : null };
}

const safeJson = (text) => {
  try { return JSON.parse(text); } catch { return null; }
};

const must = (result, what) => {
  if (result.status >= 300) throw new Error(`${what} failed: ${result.status} ${JSON.stringify(result.json)}`);
  return result.json;
};

const percentile = (sorted, p) => sorted.length ? sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)] : NaN;

async function inBatches(items, size, work) {
  for (let i = 0; i < items.length; i += size) await Promise.all(items.slice(i, i + size).map(work));
}

// ---- setup ----

console.log(`Setting up: ${VUS} users, one board with ${CARDS} cards (run ${RUN})...`);
const setupStarted = performance.now();

const password = 'load-test-password-' + RUN;
const register = async (name) => must(await call('POST', '/api/auth/register', { body: { username: `${name}_${RUN}`, email: `${name}_${RUN}@example.com`, password } }), 'register');

const owner = await register('owner');
const board = must(await call('POST', '/api/boards', { token: owner.accessToken, body: { name: 'Load test board' } }), 'create board');
const detail = must(await call('GET', `/api/boards/${board.id}`, { token: owner.accessToken }), 'load board');
const columns = detail.columns.map((c) => c.id);
for (const name of ['Review', 'QA']) {
  const created = must(await call('POST', `/api/boards/${board.id}/columns`, { token: owner.accessToken, body: { name } }), 'add column');
  columns.push(created.id);
}

const users = [{ ...owner, email: `owner_${RUN}@example.com` }];
await inBatches(Array.from({ length: VUS - 1 }, (_, i) => i), 8, async (i) => {
  const user = await register(`vu${i}`);
  must(await call('POST', `/api/boards/${board.id}/members`, { token: owner.accessToken, body: { email: user.user.email, role: 'Editor' } }), 'invite');
  users.push(user);
});

await inBatches(Array.from({ length: CARDS }, (_, i) => i), 10, async (i) => {
  must(await call('POST', `/api/boards/${board.id}/tasks`, { token: owner.accessToken, body: { title: `Load card ${i}`, description: 'Some description text for the card.', columnId: columns[i % columns.length] } }), 'create card');
});
console.log(`Setup done in ${((performance.now() - setupStarted) / 1000).toFixed(1)} s. Running ${DURATION_S} s with ${VUS} users...`);

// Each user owns a slice of the cards (and keeps its row versions up to date as it moves them).
const first = must(await call('GET', `/api/boards/${board.id}`, { token: owner.accessToken }), 'load board');
const allCards = first.columns.flatMap((c) => c.cards);

// ---- the run ----

const samples = { board: [], move: [], activity: [] };
const errors = { board: 0, move: 0, activity: 0 };
let bytesTotal = 0;
const endAt = performance.now() + DURATION_S * 1000;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function virtualUser(index) {
  const me = users[index];
  const mine = allCards.filter((_, i) => i % VUS === index).map((c) => ({ id: c.id, rowVersion: c.rowVersion, columnId: c.columnId }));
  const clientId = `vu-${index}`;

  while (performance.now() < endAt) {
    const roll = Math.random();
    if (roll < 0.7) {
      const r = await call('GET', `/api/boards/${board.id}`, { token: me.accessToken, clientId });
      if (r.status === 200) samples.board.push(r.ms);
      else errors.board++;
      bytesTotal += r.bytes;
    } else if (roll < 0.9 && mine.length) {
      const card = mine[Math.floor(Math.random() * mine.length)];
      const target = columns[(columns.indexOf(card.columnId) + 1 + Math.floor(Math.random() * (columns.length - 1))) % columns.length];
      const r = await call('PATCH', `/api/tasks/${card.id}/move`, { token: me.accessToken, clientId, body: { columnId: target, rowVersion: card.rowVersion } });
      if (r.status === 200) {
        samples.move.push(r.ms);
        card.rowVersion = r.json.rowVersion;
        card.columnId = r.json.columnId;
      } else errors.move++;
    } else {
      const r = await call('GET', `/api/boards/${board.id}/activity?limit=30`, { token: me.accessToken, clientId });
      if (r.status === 200) samples.activity.push(r.ms);
      else errors.activity++;
    }
    await sleep(100 + Math.random() * 200);
  }
}

const runStarted = performance.now();
await Promise.all(users.map((_, i) => virtualUser(i)));
const elapsed = (performance.now() - runStarted) / 1000;

// ---- report ----

const rows = {};
let total = 0;
console.log(`\n${VUS} users, ${CARDS} cards, ${elapsed.toFixed(1)} s`);
console.log('endpoint            requests   req/s     p50      p95      p99      max   errors');
for (const [name, list] of Object.entries(samples)) {
  const sorted = [...list].sort((a, b) => a - b);
  total += sorted.length;
  rows[name] = { requests: sorted.length, errors: errors[name], p50: percentile(sorted, 50), p95: percentile(sorted, 95), p99: percentile(sorted, 99), max: sorted.at(-1) };
  const f = (n) => (Number.isFinite(n) ? n.toFixed(1).padStart(7) + ' ms' : '       n/a');
  console.log(`${name.padEnd(18)} ${String(sorted.length).padStart(8)} ${(sorted.length / elapsed).toFixed(1).padStart(7)} ${f(rows[name].p50)} ${f(rows[name].p95)} ${f(rows[name].p99)} ${f(rows[name].max)} ${String(errors[name]).padStart(6)}`);
}
const failed = Object.values(errors).reduce((a, b) => a + b, 0);
console.log(`total ${total} requests, ${(total / elapsed).toFixed(1)} req/s, ${failed} errors, ${(bytesTotal / 1e6).toFixed(1)} MB of board JSON`);

writeFileSync(new URL('./results.json', import.meta.url), JSON.stringify({ at: new Date().toISOString(), vus: VUS, cards: CARDS, seconds: elapsed, endpoints: rows, errors: failed }, null, 2));

const boardP95 = rows.board.p95;
const ok = failed === 0 && boardP95 < 200;
console.log(ok ? `\nPASS: no errors and board load p95 ${boardP95.toFixed(1)} ms is under 200 ms.` : `\nFAIL: ${failed} errors, board load p95 ${boardP95?.toFixed?.(1)} ms (target under 200 ms).`);
process.exit(ok ? 0 : 1);
