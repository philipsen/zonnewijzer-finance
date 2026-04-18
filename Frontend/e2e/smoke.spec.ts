import { test, expect } from '@playwright/test'

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000'

function isLocalBaseUrl(url: string): boolean {
  try {
    const { hostname } = new URL(url)
    return hostname === 'localhost' || hostname === '127.0.0.1'
  } catch {
    return false
  }
}

test('home shows sign-in when not authenticated', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('.auth-prompt')).toBeVisible()
  await expect(page.getByRole('heading', { name: /Welcome to the Zonnewijzer Finance App/i })).toBeVisible()
})

test('email register leads to pending access (local only)', async ({ page }) => {
  test.skip(!isLocalBaseUrl(baseURL), 'Register flow runs only against local dev (not production).')

  const devLoginConfigured =
    !!(process.env.E2E_EMAIL?.trim() && process.env.E2E_PASSWORD?.trim()) && isLocalBaseUrl(baseURL)
  test.skip(
    devLoginConfigured,
    'Skipped when E2E_EMAIL/E2E_PASSWORD are set: Playwright uses dev SQLite for login tests; avoids extra signups there.',
  )

  const email = `e2e-${Date.now()}@example.test`
  const password = 'Password123!'

  await page.goto('/')
  await page.locator('.auth-mode-toggle').getByRole('button', { name: 'Create account' }).click()
  await page.getByPlaceholder('Email').fill(email)
  await page.getByPlaceholder('Password').fill(password)
  await page.locator('.email-auth-submit').click()

  await expect(page.locator('.pending-access')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('heading', { name: 'Account not yet activated' })).toBeVisible()
})
