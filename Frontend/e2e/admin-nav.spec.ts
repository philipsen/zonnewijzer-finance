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

/** Admin nav labels in `App.vue` (main-nav). */
const adminNavLinks = [
  'Home',
  'Users',
  'Charging Users',
  'Sessions',
  'Stations',
  'Upload',
  'Invoices',
  'Payments',
  'Prices',
  'Events',
] as const

test.use({ viewport: { width: 1920, height: 1080 } })

test.describe('Admin navbar (E2E_ADMIN_EMAIL + UserAdminPassword)', () => {
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

  test('click every admin tab, then User view / Admin view toggle', async ({ page }) => {
    const nav = page.locator('nav.main-nav')
    await expect(nav).toBeVisible()

    for (const label of adminNavLinks) {
      await expect(nav.getByRole('link', { name: label, exact: true })).toBeVisible()
    }
    await expect(page.getByRole('button', { name: 'User view' })).toBeVisible()

    await test.step('Click each nav tab and assert page', async () => {
      await nav.getByRole('link', { name: 'Home', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/')
      await expect(page.getByRole('heading', { name: /Welcome to the Zonnewijzer Finance App/i })).toBeVisible({
        timeout: 20_000,
      })

      await nav.getByRole('link', { name: 'Users', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/users')
      await expect(page.getByRole('heading', { name: 'All Users' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Charging Users', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/charging-users')
      await expect(page.getByRole('heading', { name: 'Charging Users' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Sessions', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/charging-sessions')
      await expect(page.getByRole('heading', { name: 'Charging Sessions' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Stations', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/charging-stations')
      await expect(page.getByRole('heading', { name: /Charging stations/i })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Upload', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/upload-sessions')
      await expect(page.getByRole('heading', { name: 'Upload Charging Sessions' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Invoices', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/invoices')
      await expect(page.getByRole('heading', { name: 'Invoice Grid' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Payments', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/payments')
      await expect(page.getByRole('heading', { name: 'Payments' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Prices', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/energy-prices')
      await expect(page.getByRole('heading', { name: 'Energy Prices' })).toBeVisible({ timeout: 20_000 })

      await nav.getByRole('link', { name: 'Events', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/event-log')
      await expect(page.getByRole('heading', { name: 'Events' })).toBeVisible({ timeout: 20_000 })
    })

    await test.step('User view: admin nav hidden, toggle shows Admin view', async () => {
      await page.getByRole('button', { name: 'User view' }).click()
      await expect(page.getByRole('button', { name: 'Admin view' })).toBeVisible()
      await expect(nav.getByRole('link', { name: 'Users', exact: true })).toHaveCount(0)
      await expect(nav.getByRole('link', { name: 'Sessions', exact: true })).toHaveCount(0)

      const mySessions = nav.getByRole('link', { name: 'My Sessions', exact: true })
      if ((await mySessions.count()) > 0) {
        await expect(mySessions).toBeVisible()
        await mySessions.click()
        await expect(page.getByRole('heading', { name: /My Charging Sessions/i })).toBeVisible({
          timeout: 20_000,
        })
        await nav.getByRole('link', { name: 'My Invoices', exact: true }).click()
        await expect(page).toHaveURL((url) => new URL(url).pathname === '/my-invoices')
        await expect(page.getByRole('heading', { name: 'My Invoices' })).toBeVisible({ timeout: 20_000 })
      }
    })

    await test.step('Admin view: full admin tabs return', async () => {
      await page.getByRole('button', { name: 'Admin view' }).click()
      await expect(page.getByRole('button', { name: 'User view' })).toBeVisible()
      await expect(nav.getByRole('link', { name: 'Users', exact: true })).toBeVisible()
      await expect(nav.getByRole('link', { name: 'Sessions', exact: true })).toBeVisible()
      await nav.getByRole('link', { name: 'Home', exact: true }).click()
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/')
    })
  })
})
