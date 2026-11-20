import { Locator, Page, expect, test } from '@playwright/test';

const password = 'correct horse battery staple';
const run = Date.now().toString(36);

/** The app runs under a strict Content-Security-Policy; a blocked script, style or connection is a bug, not noise. */
const cspViolations = new WeakMap<Page, string[]>();
function watchForCspViolations(page: Page): void {
  const seen: string[] = [];
  cspViolations.set(page, seen);
  page.on('console', (message) => {
    if (/content security policy/i.test(message.text())) seen.push(message.text());
  });
}

async function signUp(page: Page, name: string): Promise<void> {
  watchForCspViolations(page);
  await page.goto('/register');
  await page.getByLabel('Username').fill(name);
  await page.getByLabel('Email').fill(`${name}@example.com`);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('heading', { name: 'Your boards' })).toBeVisible();
}

const column = (page: Page, name: string): Locator => page.getByRole('region', { name, exact: true });
const card = (where: Locator, title: string): Locator => where.getByRole('button', { name: `Open card: ${title}` });

/** Drags with real mouse events, the way a person does: the drag library needs movement before it starts. */
async function drag(page: Page, from: Locator, to: Locator): Promise<void> {
  const start = (await from.boundingBox())!;
  const end = (await to.boundingBox())!;
  await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
  await page.mouse.down();
  await page.mouse.move(start.x + start.width / 2 + 12, start.y + start.height / 2 + 12, { steps: 4 });
  await page.mouse.move(end.x + end.width / 2, end.y + 40, { steps: 15 });
  await page.mouse.up();
}

test('a second browser sees a card move without reloading', async ({ browser }) => {
  // Two separate browser contexts: two users with their own cookies, like two people on two machines.
  const annPage = await (await browser.newContext()).newPage();
  const bobPage = await (await browser.newContext()).newPage();
  const ann = `ann${run}`;
  const bob = `bob${run}`;
  await signUp(annPage, ann);
  await signUp(bobPage, bob);

  // Ann creates a board and invites Bob.
  await annPage.getByLabel('New board name').fill('Sprint demo');
  await annPage.getByRole('button', { name: 'Create board' }).click();
  await expect(annPage.getByRole('heading', { name: 'Sprint demo' })).toBeVisible();
  await annPage.getByRole('button', { name: /Members/ }).click();
  const dialog = annPage.getByRole('dialog');
  await dialog.getByLabel('Email').fill(`${bob}@example.com`);
  await dialog.getByRole('button', { name: 'Invite' }).click();
  await expect(dialog.getByText(bob, { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Close' }).click();

  // Bob opens the board. From here on he never reloads: a marker on the page proves it.
  await bobPage.goto('/boards');
  await bobPage.getByRole('link', { name: /Sprint demo/ }).click();
  await expect(bobPage.locator('.sync-chip')).toContainText('Live');
  await expect(annPage.locator('.sync-chip')).toContainText('Live');
  await bobPage.evaluate(() => ((window as unknown as { __notReloaded: boolean }).__notReloaded = true));

  // Ann adds a card: it appears for Bob.
  await column(annPage, 'To do').getByRole('button', { name: 'Add a card' }).click();
  await annPage.getByLabel('Card title').fill('Write the end-to-end test');
  await annPage.keyboard.press('Enter');
  await expect(card(column(bobPage, 'To do'), 'Write the end-to-end test')).toBeVisible();

  // Ann drags it to Doing: Bob sees it move.
  await drag(annPage, card(column(annPage, 'To do'), 'Write the end-to-end test'), column(annPage, 'Doing').locator('.cards'));
  await expect(card(column(annPage, 'Doing'), 'Write the end-to-end test')).toHaveCount(1); // and only once on her own screen
  await expect(card(column(bobPage, 'Doing'), 'Write the end-to-end test')).toBeVisible();
  await expect(card(column(bobPage, 'To do'), 'Write the end-to-end test')).toHaveCount(0);

  // Bob moves it on to Done: Ann sees that in turn.
  await drag(bobPage, card(column(bobPage, 'Doing'), 'Write the end-to-end test'), column(bobPage, 'Done').locator('.cards'));
  await expect(card(column(annPage, 'Done'), 'Write the end-to-end test')).toBeVisible();

  // Both counts agree, the activity feed tells the story, and Bob's page was never reloaded.
  await expect(column(bobPage, 'Done').locator('.count')).toHaveText('1');
  await bobPage.getByRole('button', { name: 'Activity' }).click();
  await expect(bobPage.getByRole('list', { name: 'Recent activity' })).toContainText(`${ann} moved “Write the end-to-end test” from To do to Doing`);
  expect(await bobPage.evaluate(() => (window as unknown as { __notReloaded?: boolean }).__notReloaded)).toBe(true);
  expect(cspViolations.get(annPage)).toEqual([]);
  expect(cspViolations.get(bobPage)).toEqual([]);

  if (process.env['E2E_SCREENSHOTS']) {
    await annPage.screenshot({ path: '../docs/img/board-ann.png' });
    await bobPage.screenshot({ path: '../docs/img/board-bob.png' });
  }
});

test('a session survives a reload, ends on sign out, and starts again on sign in', async ({ page }) => {
  const name = `carol${run}`;
  await signUp(page, name);

  await page.reload(); // the access token lived only in memory: the refresh cookie brings the session back
  await expect(page.getByRole('heading', { name: 'Your boards' })).toBeVisible();
  await expect(page.getByText(name, { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in to TeamBoard' })).toBeVisible();
  await page.goto('/boards');
  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fboards/); // signed out: the guard sends you to sign in
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Sign in to TeamBoard' })).toBeVisible(); // and the cookie is really gone

  await page.getByLabel('Email').fill(`${name}@example.com`);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/boards$/); // back to where the guard sent them from
  expect(cspViolations.get(page)).toEqual([]);
});
