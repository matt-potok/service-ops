// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { WorkOrdersPage } from './WorkOrdersPage'
import { getWorkOrders } from './api'

vi.mock('./api', async importOriginal => ({
  ...await importOriginal<typeof import('./api')>(),
  getTechnicians: async () => [{ id: 'tech', displayName: 'Alex Reed', isActive: true }],
  getCustomers: async () => [{ id: 'harbor', name: 'Harborstone Logistics' }, { id: 'cedar', name: 'Cedar Vale Offices' }],
  getLocations: async (id: string) => [{ id: `${id}-site`, name: `${id} campus` }],
  getCreationOptions: async () => ({ serviceTypes: [{ code: 'HVAC', label: 'HVAC' }], priorities: [] }),
  getWorkOrders: vi.fn(async (query: string) => ({ items: [], page: Number(new URLSearchParams(query).get('page') || 1), pageSize: 25, totalCount: 0, evaluatedAt: '2026-09-29T12:00:00Z' })),
}))

function HistoryControls() {
  const location = useLocation()
  const navigate = useNavigate()
  return <><output aria-label="URL">{location.search}</output><button onClick={() => navigate(-1)}>History back</button><button onClick={() => navigate(1)}>History forward</button></>
}
afterEach(() => { cleanup(); vi.clearAllMocks() })

it('restores URL filters and page with history, and resets page/location when customer changes', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/work-orders?customerId=harbor&locationId=harbor-site&serviceType=HVAC&slaStatus=Breached&status=New&page=2&sort=priority']}>
    <HistoryControls /><WorkOrdersPage />
  </MemoryRouter></QueryClientProvider>)
  const customer = screen.getByRole('combobox', { name: 'Customer' }) as HTMLSelectElement
  const location = screen.getByRole('combobox', { name: 'Location' }) as HTMLSelectElement
  await screen.findByRole('option', { name: 'harbor campus' })
  expect(customer.value).toBe('harbor')
  expect(location.value).toBe('harbor-site')
  expect((screen.getByRole('combobox', { name: 'SLA status' }) as HTMLSelectElement).value).toBe('Breached')
  fireEvent.change(customer, { target: { value: 'cedar' } })
  await waitFor(() => expect(screen.getByLabelText('URL').textContent).toContain('customerId=cedar'))
  expect(screen.getByLabelText('URL').textContent).not.toContain('page=')
  expect(screen.getByLabelText('URL').textContent).not.toContain('locationId=')
  expect(screen.getByLabelText('URL').textContent).toContain('sort=priority')
  fireEvent.click(screen.getByRole('button', { name: 'History back' }))
  await waitFor(() => expect(location.value).toBe('harbor-site'))
  expect(screen.getByLabelText('URL').textContent).toContain('page=2')
  fireEvent.click(screen.getByRole('button', { name: 'History forward' }))
  await waitFor(() => expect(customer.value).toBe('cedar'))
  expect(location.value).toBe('')
  client.clear()
})

it('debounces search into the URL, resets page and clears filters', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/work-orders?page=3&search=roof']}><HistoryControls /><WorkOrdersPage /></MemoryRouter></QueryClientProvider>)
  const search = screen.getByRole('textbox', { name: 'Search work orders' }) as HTMLInputElement
  expect(search.value).toBe('roof')
  fireEvent.change(search, { target: { value: 'cool' } })
  fireEvent.change(search, { target: { value: 'cooling' } })
  expect(screen.getByLabelText('URL').textContent).toBe('?page=3&search=roof')
  await waitFor(() => expect(screen.getByLabelText('URL').textContent).toBe('?search=cooling'))
  expect(vi.mocked(getWorkOrders).mock.calls.some(([query]) => new URLSearchParams(query).get('search') === 'cool')).toBe(false)
  fireEvent.click(screen.getAllByRole('button', { name: 'Clear filters' })[0])
  await waitFor(() => expect(screen.getByLabelText('URL').textContent).toBe(''))
  expect(search.value).toBe('')
  await screen.findByRole('heading', { name: 'No work orders yet' })
  client.clear()
})

it('keeps page two through unrelated renders of the real grid', async () => {
  vi.mocked(getWorkOrders).mockImplementation(async query => ({ page: Number(new URLSearchParams(query).get('page') || 1), pageSize: 25,
    totalCount: new URLSearchParams(query).has('slaStatus') ? 3 : 60, evaluatedAt: '2026-09-29T12:00:00Z', items: [{
    id: 'order', number: 'WO-10026', title: 'Cooling unit rattling', customerId: 'harbor', customerName: 'Harborstone Logistics',
    locationId: 'harbor-site', locationName: 'North Distribution Center', serviceType: 'HVAC', priority: 'High', status: 'New',
    slaState: 'Good', deadline: '2026-09-29T16:00:00Z', createdAt: '2026-09-29T12:00:00Z',
  }] }))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/work-orders?page=2&sort=priority']}><HistoryControls /><WorkOrdersPage /></MemoryRouter></QueryClientProvider>)
  await screen.findByText('26–50 of 60')
  fireEvent.click(screen.getByRole('button', { name: 'More filters' }))
  // Allow the grid's own deferred sort/page reconciliation to run.
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 100)) })
  expect(screen.getByLabelText('URL').textContent).toBe('?page=2&sort=priority')
  expect(screen.getByText('26–50 of 60')).toBeTruthy()
  fireEvent.change(screen.getByRole('combobox', { name: 'SLA status' }), { target: { value: 'Good' } })
  await screen.findByText('1–3 of 3')
  fireEvent.click(screen.getByRole('button', { name: 'History back' }))
  await screen.findByText('26–50 of 60')
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 100)) })
  expect(screen.getByLabelText('URL').textContent).toBe('?page=2&sort=priority')
  fireEvent.click(screen.getByRole('button', { name: 'History forward' }))
  await screen.findByText('1–3 of 3')
  client.clear()
})


it('preserves multiple statuses and switches assignment filters without contradictions', async () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/work-orders?status=New&status=OnHold&unassigned=true&page=2']}><HistoryControls /><WorkOrdersPage /></MemoryRouter></QueryClientProvider>)
  await screen.findByRole('option', { name: 'Alex Reed' })
  expect(screen.getByRole('combobox', { name: 'Status' }).textContent).toContain('New, On hold')
  fireEvent.change(screen.getByRole('combobox', { name: 'Technician' }), { target: { value: 'tech' } })
  await waitFor(() => expect(screen.getByLabelText('URL').textContent).toContain('technicianId=tech'))
  expect(screen.getByLabelText('URL').textContent).toContain('status=New&status=OnHold')
  expect(screen.getByLabelText('URL').textContent).not.toContain('unassigned=')
  expect(screen.getByLabelText('URL').textContent).not.toContain('page=')
  fireEvent.click(screen.getByRole('button', { name: 'History back' }))
  await waitFor(() => expect((screen.getByRole('combobox', { name: 'Technician' }) as HTMLSelectElement).value).toBe('unassigned'))
  expect(screen.getByLabelText('URL').textContent).toContain('page=2')
  client.clear()
})
