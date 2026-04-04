import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import axios from 'axios'
import EnergyPrices from '../components/EnergyPrices.vue'

vi.mock('axios')

const mockPrices = [
  { year: 2026, month: 1, pricePerKwh: 0.22, updatedAt: '2026-01-01T00:00:00Z' },
  { year: 2026, month: 2, pricePerKwh: 0.24, updatedAt: '2026-02-01T00:00:00Z' }
]

describe('EnergyPrices.vue', () => {
  beforeEach(() => {
    localStorage.setItem('token', 'test-token')
    vi.clearAllMocks()
  })

  it('shows loading state initially', () => {
    axios.get.mockResolvedValue({ data: [] })
    const wrapper = mount(EnergyPrices)
    expect(wrapper.find('.loading').exists()).toBe(true)
  })

  it('renders price rows after loading', async () => {
    axios.get.mockResolvedValue({ data: mockPrices })
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('January')
    expect(rows[1].text()).toContain('February')
  })

  it('shows no-data message when prices are empty', async () => {
    axios.get.mockResolvedValue({ data: [] })
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    expect(wrapper.find('.no-data').exists()).toBe(true)
  })

  it('shows error message when loading fails', async () => {
    axios.get.mockRejectedValue(new Error('Network error'))
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    expect(wrapper.find('.error').text()).toContain('Failed to load energy prices')
  })

  it('populates form when clicking Edit on a row', async () => {
    axios.get.mockResolvedValue({ data: mockPrices })
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    await wrapper.findAll('.btn-edit')[0].trigger('click')
    expect(wrapper.find('h2').text()).toBe('Edit Price')
    expect(wrapper.vm.form.year).toBe(2026)
    expect(wrapper.vm.form.month).toBe(1)
    expect(wrapper.vm.form.pricePerKwh).toBe(0.22)
  })

  it('resets form when clicking Cancel', async () => {
    axios.get.mockResolvedValue({ data: mockPrices })
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    await wrapper.findAll('.btn-edit')[0].trigger('click')
    expect(wrapper.find('h2').text()).toBe('Edit Price')
    await wrapper.find('.btn-secondary').trigger('click')
    expect(wrapper.find('h2').text()).toBe('Add Price')
  })

  it('calls POST on save and reloads prices', async () => {
    axios.get.mockResolvedValue({ data: mockPrices })
    axios.post.mockResolvedValue({})
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(axios.post).toHaveBeenCalledWith(
      '/api/energyprices',
      expect.objectContaining({ pricePerKwh: expect.any(Number) }),
      expect.objectContaining({ headers: expect.objectContaining({ Authorization: 'Bearer test-token' }) })
    )
    expect(axios.get).toHaveBeenCalledTimes(2) // initial load + reload after save
  })

  it('shows error when save fails', async () => {
    axios.get.mockResolvedValue({ data: mockPrices })
    axios.post.mockRejectedValue({ response: { data: 'Duplicate entry' } })
    const wrapper = mount(EnergyPrices)
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('.error').text()).toBe('Duplicate entry')
  })
})
