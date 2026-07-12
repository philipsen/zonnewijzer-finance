import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import axios from 'axios'
import EditChargingUser from '../components/EditChargingUser.vue'

vi.mock('axios')

// Minimal router with the route param :id
const makeRouter = (id = '5') => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/charge-cards/:id/edit', component: EditChargingUser },
      { path: '/charge-cards', component: { template: '<div />' } }
    ]
  })
  router.push(`/charge-cards/${id}/edit`)
  return router
}

const mockChargingUser = {
  id: 5,
  value: '047D9322B62290',
  comment: 'Alice',
  linkedUserId: null,
  street: 'Main St',
  houseNumber: '10',
  postalCode: '1234 AB',
  placeName: 'Amsterdam'
}

const mockAppUsers = [
  { id: 1, name: 'Bob', chargeCardIds: [] },
  { id: 2, name: 'Carol', chargeCardIds: [7] } // linked to another card
]

// Build a JWT-like token with Admin role
const makeAdminToken = () => {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const payload = btoa(JSON.stringify({
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Admin'
  }))
  return `${header}.${payload}.sig`
}

describe('EditChargingUser.vue', () => {
  beforeEach(() => {
    localStorage.setItem('token', makeAdminToken())
    vi.clearAllMocks()
  })

  it('shows loading state initially', async () => {
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    expect(wrapper.find('.loading').exists()).toBe(true)
    await flushPromises() // ensure pending promises resolve cleanly
  })

  it('renders form after data loads', async () => {
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    expect(wrapper.find('.edit-form').exists()).toBe(true)
    expect(wrapper.find('#displayName').element.value).toBe('Alice')
    expect(wrapper.text()).toContain('047D9322B62290')
  })

  it('shows 404 error when charging user not found', async () => {
    axios.get.mockRejectedValue({ response: { status: 404 } })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    expect(wrapper.text()).toContain('Charge card not found')
  })

  it('shows linked charge cards in the app user dropdown labels', async () => {
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    const options = wrapper.findAll('#linkedUserId option')
    const bobOption = options.find(o => o.text().includes('Bob'))
    const carolOption = options.find(o => o.text().includes('Carol'))
    expect(bobOption.text()).toBe('Bob')
    expect(carolOption.text()).toContain('card(s) 7')
  })

  it('Save button is disabled with no changes', async () => {
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    const saveBtn = wrapper.find('button[type="submit"]')
    expect(saveBtn.element.disabled).toBe(true)
  })

  it('Save button enables after changing display name', async () => {
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    await wrapper.find('#displayName').setValue('Alice Updated')
    const saveBtn = wrapper.find('button[type="submit"]')
    expect(saveBtn.element.disabled).toBe(false)
  })

  it('calls PUT with correct payload on save', async () => {
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    axios.put.mockResolvedValue({})
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    await wrapper.find('#displayName').setValue('Alice Updated')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(axios.put).toHaveBeenCalledWith(
      '/api/auth/charging-users/5',
      expect.objectContaining({ displayName: 'Alice Updated' }),
      expect.any(Object)
    )
  })

  it('shows access denied for non-admin users', async () => {
    // Override token with non-admin role
    const header = btoa(JSON.stringify({ alg: 'HS256' }))
    const payload = btoa(JSON.stringify({
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'User'
    }))
    localStorage.setItem('token', `${header}.${payload}.sig`)
    axios.get
      .mockResolvedValueOnce({ data: mockChargingUser })
      .mockResolvedValueOnce({ data: mockAppUsers })
    const router = makeRouter()
    await router.isReady()
    const wrapper = mount(EditChargingUser, { global: { plugins: [router] } })
    await flushPromises()
    expect(wrapper.find('.access-denied').exists()).toBe(true)
  })
})
