/// <reference types="node" />
import { test, expect, type Page } from '@playwright/test'

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000'

function isLocalBaseUrl(url: string): boolean {
  try {
    const { hostname } = new URL(url)
    return hostname === 'localhost' || hostname === '127.0.0.1'
  } catch {
    return false
  }
}

/**
 * Remove a registered app user via the admin Users UI (Development only; requires Delete on user cards).
 * Skips quietly when E2E_ADMIN_EMAIL / UserAdminPassword are not set.
 */
async function deleteAppUserByEmailAsAdminUiIfConfigured(page: Page, targetEmail: string): Promise<void> {
  const adminEmail = (process.env.E2E_ADMIN_EMAIL ?? '').trim()
  const adminPassword = (
    process.env.UserAdminPassword ??
    process.env.E2E_ADMIN_PASSWORD ??
    ''
  ).trim()
  if (!adminEmail || !adminPassword) return

  const normalized = targetEmail.trim().toLowerCase()

  if (await page.getByRole('button', { name: 'Sign Out' }).count()) {
    await page.getByRole('button', { name: 'Sign Out' }).click()
    await expect(page.locator('.auth-prompt')).toBeVisible({ timeout: 15_000 })
  }

  await page.goto('/')
  await page.locator('.auth-mode-toggle').getByRole('button', { name: 'Email login' }).click()
  await page.getByPlaceholder('Email').fill(adminEmail)
  await page.getByPlaceholder('Password').fill(adminPassword)
  await page.locator('.email-auth-submit').click()
  await expect(page.locator('.dashboard')).toBeVisible({ timeout: 30_000 })

  await page.goto('/users')
  await expect(page.getByRole('heading', { name: 'All Users' })).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('.users-page .loading')).toBeHidden({ timeout: 30_000 })

  const card = page.locator('.user-card').filter({ hasText: normalized })
  if ((await card.count()) === 0) return

  page.once('dialog', (d) => d.accept())
  await card.getByRole('button', { name: 'Delete' }).click()
  await expect(card).toBeHidden({ timeout: 20_000 })
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

  try {
    await page.goto('/')
    await page.locator('.auth-mode-toggle').getByRole('button', { name: 'Create account' }).click()
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill(password)
    await page.locator('.email-auth-submit').click()

    await expect(page.locator('.pending-access')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { name: 'Account not yet activated' })).toBeVisible()
  } finally {
    await deleteAppUserByEmailAsAdminUiIfConfigured(page, email)
  }
})
