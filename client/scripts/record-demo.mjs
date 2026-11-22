// Drives two browser windows side by side, like two people on one board, slowly enough to be screen-recorded.
//
//   node scripts/record-demo.mjs
//
// 1. Start your screen recorder on the left and right halves of the screen (ScreenToGif, or Windows' Snipping Tool
//    in video mode). 2. Run this script: it opens the two windows, signs up Ann and Bob, and plays the story.
//
// Environment: BASE (the site, default the live deployment), SCREEN_W (default 1920), WINDOW_H (default 820),
//              HEADLESS=1 to run without windows (a dry run that checks the script itself works).
// It uses Chrome (installed) and creates two throwaway accounts and one board on whatever BASE points at.

import { chromium } from '@playwright/test';

const BASE = process.env.BASE ?? 'https://teamboard-taha.thankfulgrass-8dcd7388.centralus.azurecontainerapps.io';
const SCREEN_W = Number(process.env.SCREEN_W ?? 1920);
const WINDOW_H = Number(process.env.WINDOW_H ?? 820);
const HEADLESS = process.env.HEADLESS === '1';
const half = Math.floor(SCREEN_W / 2);

const run = Date.now().toString(36);
const password = 'correct horse battery staple';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const beat = (ms = 900) => (HEADLESS ? Promise.resolve() : sleep(ms)); // a pause so the viewer can follow

// The site scales to zero and its free database auto-pauses, so the first request after a quiet spell can take a
// minute or two. Wake everything up before any window opens, so the recording never shows a spinner.
async function warmUp() {
  console.log('Waking the site up (the first request after idle can take a minute or two)...');
  const give_up = Date.now() + 5 * 60 * 1000;
  while (Date.now() < give_up) {
    try {
      const response = await fetch(`${BASE}/health/ready`, { signal: AbortSignal.timeout(100_000) });
      if (response.ok) { console.log('The site is awake.'); return; }
    } catch { /* still starting: ask again */ }
    await sleep(5000);
  }
  throw new Error(`${BASE} did not become ready within 5 minutes.`);
}

async function openWindow(x) {
  const browser = await chromium.launch({
    channel: 'chrome',
    headless: HEADLESS,
    args: [`--window-position=${x},0`, `--window-size=${half},${WINDOW_H}`],
  });
  // viewport: null makes the page fill the window, so the recording shows a normal browser.
  const context = await browser.newContext({ baseURL: BASE, viewport: HEADLESS ? { width: half, height: WINDOW_H } : null });
  context.setDefaultTimeout(90_000); // slow first requests are expected on the free tier
  const page = await context.newPage();
  page.on('response', (r) => { if (r.url().includes('/api/') && r.status() >= 400 && r.status() !== 401) console.log(`  ! ${r.status()} ${r.url()}`); });
  return { browser, page };
}

async function type(locator, text) {
  await locator.click();
  await locator.pressSequentially(text, { delay: HEADLESS ? 0 : 55 });
}

async function signUp(page, name) {
  await page.goto('/register');
  await type(page.getByLabel('Username'), name);
  await type(page.getByLabel('Email'), `${name}@example.com`);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Create account' }).click();
  await page.getByRole('heading', { name: 'Your boards' }).waitFor();
}

const column = (page, name) => page.getByRole('region', { name, exact: true });
const card = (where, title) => where.getByRole('button', { name: `Open card: ${title}` });

async function drag(page, from, to) {
  const start = await from.boundingBox();
  const end = await to.boundingBox();
  await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
  await page.mouse.down();
  await page.mouse.move(start.x + start.width / 2 + 12, start.y + start.height / 2 + 12, { steps: 6 });
  await page.mouse.move(end.x + end.width / 2, end.y + 40, { steps: HEADLESS ? 15 : 40 });
  await page.mouse.up();
}

await warmUp();

const ann = `ann${run}`;
const bob = `bob${run}`;
const left = await openWindow(0);
const right = await openWindow(half);
const annPage = left.page;
const bobPage = right.page;

try {
  console.log('Signing up Ann and Bob...');
  await Promise.all([signUp(annPage, ann), signUp(bobPage, bob)]);

  // Ann makes a board and invites Bob (not the interesting part: do it briskly, start recording before the board opens).
  await annPage.getByLabel('New board name').fill('Sprint demo');
  await annPage.getByRole('button', { name: 'Create board' }).click();
  await annPage.getByRole('heading', { name: 'Sprint demo' }).waitFor();
  await annPage.getByRole('button', { name: /Members/ }).click();
  const dialog = annPage.getByRole('dialog');
  await dialog.getByLabel('Email').fill(`${bob}@example.com`);
  await dialog.getByRole('button', { name: 'Invite' }).click();
  await dialog.getByText(bob, { exact: true }).waitFor();
  await dialog.getByRole('button', { name: 'Close' }).click();

  await bobPage.goto('/boards');
  await bobPage.getByRole('link', { name: /Sprint demo/ }).click();
  await Promise.all([
    annPage.locator('.sync-chip', { hasText: 'Live' }).waitFor(),
    bobPage.locator('.sync-chip', { hasText: 'Live' }).waitFor(),
  ]);

  console.log('Both windows are on the board. The story starts now.');
  await beat(2500); // a still moment at the start of the recording

  const title = 'Write the launch post';
  await column(annPage, 'To do').getByRole('button', { name: 'Add a card' }).click();
  await beat(500);
  await type(annPage.getByLabel('Card title'), title);
  await beat(500);
  await annPage.keyboard.press('Enter');
  await card(column(bobPage, 'To do'), title).waitFor();
  await beat(1800); // Bob's window shows the card that Ann just added

  await drag(annPage, card(column(annPage, 'To do'), title), column(annPage, 'Doing').locator('.cards'));
  await card(column(bobPage, 'Doing'), title).waitFor();
  await beat(1800);

  await drag(bobPage, card(column(bobPage, 'Doing'), title), column(bobPage, 'Done').locator('.cards'));
  await card(column(annPage, 'Done'), title).waitFor();
  await beat(2500); // the card has landed on Ann's side too; stop recording here

  console.log('Done. Stop the recording now.');
} finally {
  if (!HEADLESS) await sleep(1500);
  await left.browser.close();
  await right.browser.close();
}
