import { expect, test } from '@playwright/test';

test.describe('Full stack (real API and live feed)', () => {
  const apiBase = process.env['E2E_API_URL'] ?? 'http://localhost:5070';

  test.beforeAll(async ({ request }) => {
    const health = await request.get(`${apiBase}/health`).catch(() => null);
    test.skip(
      !health?.ok(),
      `API not reachable at ${apiBase}. Start it with: dotnet run --project backend/src/HackerNews.Api`,
    );
  });

  test('serves real stories through to the browser', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });
    await expect(page.locator('.summary')).toContainText('stories');

    // Real titles, not placeholders.
    const firstTitle = await page.locator('.story__title').first().textContent();
    expect(firstTitle?.trim().length).toBeGreaterThan(0);
  });

  test('search narrows the real feed', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    const totalBefore = await readTotal(page);

    // "a" appears in almost every headline but should still filter something out.
    await page.locator('.search__input').fill('the');
    await expect(page.locator('.summary')).toContainText('matching');

    expect(await readTotal(page)).toBeLessThanOrEqual(totalBefore);
  });

  test('highlights the search term in the real results', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    // "the" is common enough to match the live feed whatever it happens to be showing.
    await page.locator('.search__input').fill('the');
    await expect(page.locator('.summary')).toContainText('matching');

    const marks = page.locator('.story__match');
    await expect(marks.first()).toBeVisible();

    // Highlighting must mark the term itself, not arbitrary text around it.
    for (const text of await marks.allTextContents()) {
      expect(text.toLowerCase()).toBe('the');
    }
  });

  test('stops highlighting once the search is cleared', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    await page.locator('.search__input').fill('the');
    await expect(page.locator('.story__match').first()).toBeVisible();

    await page.locator('.search').getByRole('button', { name: 'Clear search' }).click();

    await expect(page.locator('.story__match')).toHaveCount(0);
  });

  test('paging walks the real feed without repeating a story', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    const firstPage = await page.locator('.story__title').allTextContents();

    await page.getByRole('button', { name: '2', exact: true }).click();
    await expect(page.locator('.story__rank').first()).toHaveText('21');

    const secondPage = await page.locator('.story__title').allTextContents();
    expect(secondPage.some(title => firstPage.includes(title))).toBe(false);
  });

  test('Last reaches the end of the real feed', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    await page.getByRole('button', { name: 'Last' }).click();

    // Whatever the feed size, Last must land on the final page and disable itself. The
    // backreference matches only once current === total, and toHaveText retries until then —
    // reading textContent() directly would race the click.
    await expect(page.locator('.summary')).toHaveText(/page (\d+) of \1\b/);
    await expect(page.getByRole('button', { name: 'Last' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'First' })).toBeEnabled();
  });

  test('the API rejects an out-of-range page size with problem details', async ({ request }) => {
    const response = await request.get(`${apiBase}/api/stories/newest?pageSize=500`);

    expect(response.status()).toBe(400);
    expect(response.headers()['content-type']).toContain('application/problem+json');
    expect((await response.json()).errors).toHaveProperty('PageSize');
  });

  test('the API reports an unknown story as not found', async ({ request }) => {
    const response = await request.get(`${apiBase}/api/stories/999999999`);

    expect(response.status()).toBe(404);
  });

  test('repeat requests are served from the cache', async ({ request }) => {
    // Warm it, then time a second call — the snapshot makes this effectively instant.
    await request.get(`${apiBase}/api/stories/newest?pageSize=20`);

    const started = Date.now();
    const response = await request.get(`${apiBase}/api/stories/newest?pageSize=20`);
    const elapsed = Date.now() - started;

    expect(response.ok()).toBe(true);
    expect(elapsed).toBeLessThan(1000);
  });
});

async function readTotal(page: import('@playwright/test').Page): Promise<number> {
  const text = (await page.locator('.summary strong').textContent()) ?? '0';
  return Number(text.replace(/\D/g, ''));
}
