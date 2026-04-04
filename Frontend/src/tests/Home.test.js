import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import Home from '../components/Home.vue'

const router = createRouter({
  history: createMemoryHistory(),
  routes: [
    { path: '/', component: Home },
    { path: '/charging-sessions', component: { template: '<div />' } },
    { path: '/invoices', component: { template: '<div />' } }
  ]
})

describe('Home.vue', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('shows sign-in prompt when not authenticated', () => {
    const wrapper = mount(Home, { global: { plugins: [router] } })
    expect(wrapper.find('.auth-prompt').exists()).toBe(true)
    expect(wrapper.find('.dashboard').exists()).toBe(false)
  })

  it('shows dashboard when authenticated', async () => {
    localStorage.setItem('token', 'fake-token')
    const wrapper = mount(Home, { global: { plugins: [router] } })
    expect(wrapper.find('.auth-prompt').exists()).toBe(false)
    expect(wrapper.find('.dashboard').exists()).toBe(true)
  })

  it('shows Charging Sessions feature card when authenticated', async () => {
    localStorage.setItem('token', 'fake-token')
    const wrapper = mount(Home, { global: { plugins: [router] } })
    expect(wrapper.text()).toContain('Charging Sessions')
  })

  it('shows Invoices feature card when authenticated', async () => {
    localStorage.setItem('token', 'fake-token')
    const wrapper = mount(Home, { global: { plugins: [router] } })
    expect(wrapper.text()).toContain('Invoices')
  })
})
