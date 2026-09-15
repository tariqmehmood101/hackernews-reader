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

  test('renders a linkless story as plain text, never a dead anchor', async ({ page, request }) => {
    // Text posts are a small fraction of the feed and can sit anywhere in the 500, so ask the API
    // where one is rather than hoping it lands on the first page. That keeps the test
    // deterministic while still asserting on what the browser actually renders.
    const pageSize = 50;

    // 100 is the API's documented maximum, so the whole feed takes several requests.
    const items: { url: string | null; title: string }[] = [];
    for (let apiPage = 1; apiPage <= 5; apiPage++) {
      const feed = await request.get(
        `${apiBase}/api/stories/newest?page=${apiPage}&pageSize=100`,
      );
      expect(feed.ok()).toBe(true);
      const body = await feed.json();
      items.push(...body.items);
      if (!body.hasNextPage) {
        break;
      }
    }

    const index = items.findIndex(story => story.url === null);
    test.skip(index === -1, 'The whole feed currently contains no text posts.');

    const target = items[index];
    const targetPage = Math.floor(index / pageSize) + 1;

    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });
    await page.getByLabel('Stories per page').selectOption(String(pageSize));
    await expect(page.locator('.summary')).toContainText('page 1 of');

    if (targetPage > 1) {
      await page.getByRole('button', { name: String(targetPage), exact: true }).click();
      await expect(page.locator('.summary')).toContainText(`page ${targetPage} of`);
    }

    const row = page.getByRole('listitem').filter({ hasText: target.title }).first();
    await expect(row.locator('.story__title--plain')).toHaveText(target.title);
    await expect(row.locator('a.story__title')).toHaveCount(0);
    await expect(row.locator('.story__badge')).toHaveText('no link');
  });

  test('a page size larger than the result set collapses to one page', async ({ page, request }) => {
    // Pick a term the feed currently answers with a small number of matches, rather than
    // hardcoding one whose popularity drifts.
    const pageSize = 50;
    let term = '';
    let total = 0;
    for (const candidate of ['rust', 'openai', 'python', 'kubernetes']) {
      const res = await request.get(`${apiBase}/api/stories/newest?pageSize=1&search=${candidate}`);
      const count = (await res.json()).totalCount as number;
      if (count > 0 && count <= pageSize) {
        term = candidate;
        total = count;
        break;
      }
    }
    test.skip(term === '', `No candidate term currently matches between 1 and ${pageSize} stories.`);

    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    await page.getByLabel('Stories per page').selectOption(String(pageSize));
    await page.locator('.search__input').fill(term);
    await expect(page.locator('.summary')).toContainText(`matching “${term}”`);

    // Everything fits on one page, so there is nothing to page through.
    await expect(page.locator('.summary')).toContainText('page 1 of 1');
    await expect(page.getByRole('listitem')).toHaveCount(total);
    await expect(page.locator('.pager')).toHaveCount(0);
  });

  test('total pages tracks the chosen page size across the real feed', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('listitem')).toHaveCount(20, { timeout: 30_000 });

    const summary = page.locator('.summary');
    const total = Number((await page.locator('.summary strong').textContent())?.replace(/\D/g, ''));

    for (const size of [10, 50]) {
      await page.getByLabel('Stories per page').selectOption(String(size));
      await expect(summary).toContainText(`page 1 of ${Math.ceil(total / size)}`);
      await expect(page.getByRole('listitem')).toHaveCount(Math.min(size, total));
    }
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
