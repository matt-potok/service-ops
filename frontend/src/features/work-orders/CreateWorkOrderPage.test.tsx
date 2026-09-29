// @vitest-environment jsdom
import { render, screen, fireEvent, waitFor, cleanup } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { CreateWorkOrderPage } from './CreateWorkOrderPage'

vi.mock('./api', async importOriginal => ({
  ...await importOriginal<typeof import('./api')>(),
  getCustomers: async () => [{ id: 'harbor', name: 'Harborstone Logistics' }, { id: 'cedar', name: 'Cedar Vale Offices' }],
  getLocations: async (id: string) => id === 'harbor'
    ? [{ id: 'distribution', name: 'North Distribution Center' }]
    : [{ id: 'office', name: 'Central Office' }],
  getCreationOptions: async () => ({ serviceTypes: [{ code: 'HVAC', label: 'HVAC' }], priorities: [{ code: 'Normal', label: 'Normal', slaDurationMinutes: 480 }] }),
}))

afterEach(cleanup)

it('disables location until a customer is selected and clears it when the customer changes', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter><CreateWorkOrderPage /></MemoryRouter></QueryClientProvider>)
  const customer = screen.getByRole('combobox', { name: /Customer/ })
  const location = screen.getByRole('combobox', { name: /Location/ }) as HTMLSelectElement
  expect(location.disabled).toBe(true)
  await screen.findByRole('option', { name: 'Harborstone Logistics' })
  fireEvent.change(customer, { target: { value: 'harbor' } })
  await screen.findByRole('option', { name: 'North Distribution Center' })
  fireEvent.change(location, { target: { value: 'distribution' } })
  expect(location.value).toBe('distribution')
  fireEvent.change(customer, { target: { value: 'cedar' } })
  expect(location.value).toBe('')
  await screen.findByRole('option', { name: 'Central Office' })
  await waitFor(() => expect(location.disabled).toBe(false))
  expect(screen.queryByRole('option', { name: 'North Distribution Center' })).toBeNull()
  client.clear()
})
