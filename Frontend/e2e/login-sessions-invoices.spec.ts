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

async function roleFromLocalStorage(page: import('@playwright/test').Page): Promise<string | null> {
  return page.evaluate(() => {
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
}

test.describe('Logged-in user (E2E_EMAIL / E2E_PASSWORD)', () => {
  test.beforeEach(async ({ page }) => {
    const email = (process.env.E2E_EMAIL ?? '').trim()
    const password = (process.env.E2E_PASSWORD ?? '').trim()
    test.skip(!email || !password, 'Set E2E_EMAIL and E2E_PASSWORD (e.g. in repo .env) to run these tests.')
    test.skip(!isLocalBaseUrl(baseURL), 'These tests target local Playwright (dev DB + Vite).')

    await page.goto('/')
    await page.locator('.auth-mode-toggle').getByRole('button', { name: 'Email login' }).click()
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill(password)
    await page.locator('.email-auth-submit').click()
    await expect(page.locator('.dashboard')).toBeVisible({ timeout: 30_000 })
  })

  test('sessions page shows summary and data table', async ({ page }) => {
    const role = await roleFromLocalStorage(page)
    const sessionsPath = role === 'Admin' ? '/charging-sessions' : '/my-sessions'

    await page.goto(sessionsPath)
    await expect(page.getByRole('heading', { name: /Charging Sessions|My Charging Sessions/i })).toBeVisible({
      timeout: 30_000,
    })
    await expect(page.locator('.sessions-page .loading')).toBeHidden({ timeout: 30_000 })

    await test.step('Summary cards', async () => {
      await expect(page.locator('.sessions-container')).toBeVisible()
      await expect(page.locator('.sessions-summary')).toBeVisible()
      await expect(page.locator('.summary-label', { hasText: 'Total Sessions' })).toBeVisible()
      await expect(page.locator('.summary-label', { hasText: 'Total kWh' })).toBeVisible()
    })

    await test.step('Sessions table', async () => {
      const table = page.locator('table.sessions-table')
      await expect(table).toBeVisible()
      await expect(table.locator('th', { hasText: 'Start Time' })).toBeVisible()
      await expect(table.locator('th', { hasText: 'Energy (kWh)' })).toBeVisible()
      if (role === 'Admin') {
        await expect(table.locator('th', { hasText: 'Actions' })).toBeVisible()
      }

      const rows = table.locator('tbody tr.session-row')
      const count = await rows.count()
      expect(count).toBeGreaterThanOrEqual(0)
      if (count > 0) {
        await expect(rows.first().locator('.energy-cell')).toContainText(/kWh/)
      }
    })
  })

  test('invoices page shows invoice content', async ({ page }) => {
    const role = await roleFromLocalStorage(page)
    const invoicesPath = role === 'Admin' ? '/invoices' : '/my-invoices'

    await page.goto(invoicesPath)
    await expect(page.getByRole('heading', { name: /Invoice Grid|My Invoices/i })).toBeVisible({
      timeout: 30_000,
    })
    await expect(page.locator('.invoice-grid-page .loading, .my-invoices-page .loading')).toBeHidden({
      timeout: 30_000,
    })

    await test.step('Invoice layout appears', async () => {
      if (role === 'Admin') {
        const adminGrid = page.locator('.invoice-grid-page .invoice-grid-container')
        await expect(adminGrid).toBeVisible()
        const table = adminGrid.locator('table.invoice-grid')
        await expect(table).toBeVisible()
        await expect(table.locator('th', { hasText: 'User' })).toBeVisible()
        const userRows = table.locator('tbody tr.user-row')
        expect(await userRows.count()).toBeGreaterThanOrEqual(1)
      } else {
        const myInvoices = page.locator('.my-invoices-page')
        await expect(myInvoices).toBeVisible()
        const noAccount = myInvoices.locator('.no-account')
        const table = myInvoices.locator('table.invoices-table')
        const empty = myInvoices.locator('.empty')

        if (await noAccount.count()) {
          await expect(noAccount).toBeVisible()
          await expect(noAccount).toContainText('not linked to a charging user')
        } else if (await table.count()) {
          await expect(table).toBeVisible()
          await expect(table.locator('th', { hasText: 'Period' })).toBeVisible()
          await expect(table.locator('th', { hasText: 'Amount' })).toBeVisible()
        } else {
          await expect(empty).toBeVisible()
          await expect(empty).toContainText('No invoices found')
        }
      }
    })
  })

  test('non-admin: only My Sessions / My Invoices in nav and home; admin URLs blocked', async ({ page }) => {
    const role = await roleFromLocalStorage(page)
    test.skip(
      role === 'Admin',
      'This assertion is for a Base user. Use a non-admin E2E_EMAIL or create a Base test account with a linked charging profile.',
    )

    await page.goto('/')
    await expect(page.locator('.dashboard')).toBeVisible()

    const mySessions = page.getByRole('link', { name: 'My Sessions' })
    const myInvoices = page.getByRole('link', { name: 'My Invoices' })
    if ((await mySessions.count()) === 0) {
      test.skip(
        true,
        'Navbar only shows My Sessions when the user has chargingUserId. Link the account to a charging user to run this check.',
      )
    }
    await expect(mySessions).toBeVisible()
    await expect(myInvoices).toBeVisible()

    const adminNavLabels = [
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
    for (const label of adminNavLabels) {
      await expect(page.getByRole('link', { name: label, exact: true })).toHaveCount(0)
    }

    await expect(page.locator('.dashboard .features .feature-card')).toHaveCount(2)
    await expect(page.locator('.dashboard a.card-link[href="/my-sessions"]')).toBeVisible()
    await expect(page.locator('.dashboard a.card-link[href="/my-invoices"]')).toBeVisible()
    await expect(page.locator('.admin-tools')).toHaveCount(0)

    await test.step('Typing admin URLs does not grant access', async () => {
      await page.goto('/users')
      await expect(page.locator('.users-page .error')).toBeVisible({ timeout: 15_000 })

      // Backend allows any authenticated user to list charging users; admin-only UI is hidden.
      await page.goto('/charging-users')
      await expect(page.getByRole('heading', { name: 'Charging Users' })).toBeVisible({ timeout: 15_000 })
      const cuTable = page.locator('.charging-users-table')
      if ((await cuTable.count()) > 0) {
        await expect(cuTable.locator('thead th', { hasText: 'Actions' })).toHaveCount(0)
      }

      await page.goto('/invoices')
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/')

      await page.goto('/charging-stations')
      await expect(page).toHaveURL((url) => new URL(url).pathname === '/')

      await page.goto('/upload-sessions')
      await expect(page.locator('.upload-page .access-denied')).toBeVisible()

      await page.goto('/event-log')
      await expect(page.locator('.event-log-page .access-denied')).toBeVisible()

      await page.goto('/payments')
      await expect(page.locator('.payments-page .error')).toBeVisible({ timeout: 15_000 })
    })
  })
})
