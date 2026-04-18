/// <reference types="node" />
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
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

function buildTwoSessionCsv(opts: {
  stationSerial: string
  stationName: string
  from1: string
  to1: string
  from2: string
  to2: string
}): string {
  const header =
    'Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token'
  const row1 = `${opts.stationSerial},${opts.stationName},${opts.from1},${opts.to1},00:10,"1,111",1,deadbeef0000e201 (E2E Import User A)`
  const row2 = `${opts.stationSerial},${opts.stationName},${opts.from2},${opts.to2},00:10,"2,222",1,deadbeef0000e202 (E2E Import User B)`
  return `${header}\n${row1}\n${row2}\n`
}

test.use({ viewport: { width: 1920, height: 1080 } })

test.describe('Admin CSV upload → sessions list → delete (E2E_ADMIN_EMAIL)', () => {
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

  test('imports 2 sessions from CSV, lists them, then deletes both', async ({ page }, testInfo) => {
    const runId = `${Date.now()}-${testInfo.workerIndex}-${Math.random().toString(36).slice(2, 8)}`
    const stationSerial = `9${runId.replace(/\D/g, '').slice(-12).padStart(12, '0')}`.slice(0, 13)
    const stationName = `E2E Import ${runId}`
    const from1 = '2030-07-15 09:00:00'
    const to1 = '2030-07-15 09:10:00'
    const from2 = '2030-07-15 10:00:00'
    const to2 = '2030-07-15 10:10:00'

    const csvPath = path.join(testInfo.outputDir, `e2e-upload-${runId}.csv`)
    await mkdir(testInfo.outputDir, { recursive: true })
    await writeFile(
      csvPath,
      buildTwoSessionCsv({ stationSerial, stationName, from1, to1, from2, to2 }),
      'utf8',
    )

    await page.goto('/upload-sessions')
    await expect(page.getByRole('heading', { name: 'Upload Charging Sessions' })).toBeVisible()

    await page.locator('.upload-page input[type="file"]').setInputFiles(csvPath)
    await expect(page.getByText('Selected file:', { exact: false })).toBeVisible()
    await page.getByRole('button', { name: 'Upload CSV' }).click()

    await expect(page.getByRole('heading', { name: 'Upload Successful!' })).toBeVisible({
      timeout: 60_000,
    })
    await expect(page.locator('.upload-result.success .stat-number').first()).toHaveText('2')

    await page.goto('/charging-sessions')
    await expect(page.getByRole('heading', { name: 'Charging Sessions' })).toBeVisible()
    await expect(page.locator('.loading')).toBeHidden({ timeout: 30_000 })

    const markerRows = page.locator('tr.session-row').filter({ hasText: stationName })
    await expect(markerRows).toHaveCount(2)

    const modalDelete = page
      .locator('.modal-overlay')
      .filter({ has: page.getByRole('heading', { name: 'Delete Session' }) })
      .getByRole('button', { name: 'Delete' })

    for (let i = 0; i < 2; i++) {
      await markerRows.first().locator('.btn-delete').click()
      await expect(page.getByRole('heading', { name: 'Delete Session' })).toBeVisible()
      await modalDelete.click()
      await expect(page.getByRole('heading', { name: 'Delete Session' })).toBeHidden()
    }

    await expect(markerRows).toHaveCount(0)
  })
})
