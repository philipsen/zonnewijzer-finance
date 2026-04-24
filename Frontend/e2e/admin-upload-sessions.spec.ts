/// <reference types="node" />
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

test.use({ viewport: { width: 1920, height: 1080 } })

test.describe('Admin charging sessions / legacy upload URL (E2E_ADMIN_EMAIL)', () => {
  test.beforeEach(async ({ page }) => {
    const email = (process.env.E2E_ADMIN_EMAIL ?? '').trim()
    const password = (
      process.env.UserAdminPassword ??
      process.env.E2E_ADMIN_PASSWORD ??
      ''
    ).trim()
    test.skip(!email || !password, 'Set E2E_ADMIN_EMAIL and UserAdminPassword (or E2E_ADMIN_PASSWORD) in repo .env.')
    test.skip(!isLocalBaseUrl(baseURL), 'These tests target local Playwright.')

    await page.goto('/')
    await page.locator('.auth-mode-toggle').getByRole('button', { name: 'Email login' }).click()
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill(password)
    await page.locator('.email-auth-submit').click()

    await expect(page.locator('.dashboard, .pending-access, .auth-error').first()).toBeVisible({
      timeout: 30_000,
    })

    if (await page.locator('.auth-error').isVisible()) {
      const msg = (await page.locator('.auth-error').innerText()).trim()
      test.skip(
        true,
        `Admin login failed: ${msg || 'Invalid credentials'}. Check E2E_ADMIN_EMAIL and UserAdminPassword match an Admin user in the DB.`,
      )
    }
    if (await page.locator('.pending-access').isVisible()) {
      test.skip(true, 'Admin account is pending activation (no dashboard).')
    }

    await expect(page.locator('.dashboard')).toBeVisible()

    const role = await page.evaluate(() => {
      const t = localStorage.getItem('token')
      if (!t) return null
      try {
        const payload = JSON.parse(atob(t.split('.')[1])) as Record<string, unknown>
        const key = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'
        const r = payload[key]
        return typeof r === 'string' ? r : null
      } catch {
        return null
      }
    })
    test.skip(
      role !== 'Admin',
      `Expected Admin after login; got role "${role}". Set E2E_ADMIN_EMAIL to an account with Role=Admin and matching UserAdminPassword.`,
    )
  })

  test('sessions page has Smappee sync and no CSV upload; legacy /upload-sessions redirects home', async ({ page }) => {
    await page.goto('/charging-sessions')
    await expect(page.getByRole('heading', { name: 'Charging Sessions' })).toBeVisible()
    await expect(page.getByRole('button', { name: /Sync from Smappee/i })).toBeVisible()
    await expect(page.locator('.sessions-page input[type="file"]')).toHaveCount(0)

    await page.goto('/upload-sessions')
    await expect(page).toHaveURL((url) => new URL(url).pathname === '/')
  })
})
