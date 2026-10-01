// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { WorkOrderDetailPage } from './WorkOrderDetailPage'
import { correctDetails, getWorkOrder, WorkOrderApiError, type WorkOrderDetail } from './api'

vi.mock('./api', async importOriginal => ({ ...await importOriginal<typeof import('./api')>(),
  getWorkOrder: vi.fn(), correctDetails: vi.fn(), getTechnicians: async () => [],
}))
const detail: WorkOrderDetail = { id: 'order', number: 'WO-10001', title: 'Cooling failure', description: 'East wing is warm.',
  customerId: 'customer', customerName: 'Harborstone Logistics', locationId: 'location', locationName: 'North Distribution Center', locationAddress: '100 Main Street',
  serviceType: 'HVAC', priority: 'High', status: 'New', createdAt: '2026-10-01T12:00:00Z', createdByUserId: 'actor',
  slaDurationMinutes: 240, slaAtRiskAt: '2026-10-01T15:00:00Z', slaDeadlineAt: '2026-10-01T16:00:00Z', slaState: 'Good', revision: 1,
  evaluatedAt: '2026-10-01T12:00:00Z', updatedAt: '2026-10-01T12:00:00Z', technicianId: null, technicianName: null,
  completedAt: null, cancelledAt: null, holdReason: null, resolutionSummary: null, cancellationReason: null }

afterEach(() => { cleanup(); vi.resetAllMocks() })
function setup(value = detail) {
  vi.mocked(getWorkOrder).mockResolvedValue(value)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/work-orders/order']}><Routes><Route path="/work-orders/:id" element={<WorkOrderDetailPage />} /></Routes></MemoryRouter></QueryClientProvider>)
  return client
}

it.each([
  ['New', ['Assign technician', 'Edit details', 'Cancel work order']],
  ['Assigned', ['Start work', 'Change technician', 'Unassign technician', 'Edit details', 'Cancel work order']],
  ['InProgress', ['Place on hold', 'Complete work order', 'Change technician', 'Edit details', 'Cancel work order']],
  ['OnHold', ['Resume work', 'Complete work order', 'Change technician', 'Edit details', 'Cancel work order']],
  ['Completed', []], ['Cancelled', []],
] as const)('shows only applicable %s actions', async (status, actions) => {
  const client = setup({ ...detail, status, technicianId: status === 'New' ? null : 'technician', technicianName: status === 'New' ? null : 'Alex Reed' })
  await screen.findByRole('heading', { name: detail.title })
  const available = screen.queryByLabelText('Work order actions')
  expect(available ? within(available).getAllByRole('button').map(x => x.textContent) : []).toEqual(actions)
  expect(screen.queryByRole('tab', { name: 'Notes' })).toBeNull()
  client.clear()
})

it('saves the displayed revision and refreshes detail, queue and activity after success', async () => {
  const client = setup()
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  await screen.findByRole('heading', { name: detail.title })
  const updated = { ...detail, title: 'Corrected title', revision: 2 }
  vi.mocked(correctDetails).mockResolvedValue(updated)
  vi.mocked(getWorkOrder).mockResolvedValue(updated)
  fireEvent.click(screen.getByRole('button', { name: 'Edit details' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Title' }), { target: { value: 'Corrected title' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save details' }))
  await screen.findByRole('heading', { name: 'Corrected title' })
  expect(correctDetails).toHaveBeenCalledWith('order', { title: 'Corrected title', description: detail.description, expectedRevision: 1 })
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ['work-orders'] })
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ['work-order-activity', 'order'] })
  await waitFor(() => expect(getWorkOrder).toHaveBeenCalledTimes(2))
  client.clear()
})

it('reloads on stale revision, preserves draft and requires explicit review before retry', async () => {
  const client = setup()
  await screen.findByRole('heading', { name: detail.title })
  fireEvent.click(screen.getByRole('button', { name: 'Edit details' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Title' }), { target: { value: 'My unsaved draft' } })
  vi.mocked(correctDetails).mockRejectedValueOnce(new WorkOrderApiError('Work order changed. Reload and review.', 409, {}, 'stale_revision'))
  vi.mocked(getWorkOrder).mockResolvedValue({ ...detail, title: 'A colleague changed this', revision: 2 })
  fireEvent.click(screen.getByRole('button', { name: 'Save details' }))
  await screen.findByText('Current title: A colleague changed this')
  expect((screen.getByRole('textbox', { name: 'Title' }) as HTMLInputElement).value).toBe('My unsaved draft')
  expect((screen.getByRole('button', { name: 'Save details' }) as HTMLButtonElement).disabled).toBe(true)
  expect(correctDetails).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'I reviewed the changes — keep my draft' }))
  vi.mocked(correctDetails).mockResolvedValue({ ...detail, title: 'My unsaved draft', revision: 3 })
  vi.mocked(getWorkOrder).mockResolvedValue({ ...detail, title: 'My unsaved draft', revision: 3 })
  fireEvent.click(screen.getByRole('button', { name: 'Save details' }))
  await screen.findByRole('heading', { name: 'My unsaved draft' })
  expect(correctDetails).toHaveBeenLastCalledWith('order', { title: 'My unsaved draft', description: detail.description, expectedRevision: 2 })
  client.clear()
})
