// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router'
import { DashboardPage } from './DashboardPage'
import { getDashboard, type Dashboard } from './api'
import { App } from '../../app/App'
import { getSession } from '../../lib/api/client'

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), getDashboard: vi.fn() }))
vi.mock('../../lib/api/client', async original => ({ ...await original<typeof import('../../lib/api/client')>(), getSession: vi.fn() }))
const data: Dashboard = {
  evaluatedAt: '2026-10-02T12:00:00Z', timeZone: 'America/New_York',
  current: { open: 4, new: 1, assigned: 1, inProgress: 1, onHold: 1, good: 1, atRisk: 1, breached: 2 },
  workload: [{ technicianId: null, technicianName: 'Unassigned', openCount: 1 }, { technicianId: 'tech-1', technicianName: 'Leah Morgan', openCount: 3 }],
  period: { startDate: '2026-09-03', endDateExclusive: '2026-10-03', fromUtc: '2026-09-03T04:00:00Z', toUtc: '2026-10-03T04:00:00Z', completed: 3, met: 2, missed: 1, compliancePercent: 66.7 },
}
let client: QueryClient
beforeEach(() => {
  vi.mocked(getDashboard).mockReset().mockResolvedValue(data)
  vi.mocked(getSession).mockReset().mockResolvedValue({ id: 'manager', email: 'marcus.chen@atlas.example', displayName: 'Marcus Chen', roles: ['Manager'] })
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
})
afterEach(() => { cleanup(); client.clear() })
function Location() { return <output aria-label="URL">{useLocation().search}</output> }
function show(app = false) {
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/dashboard']}><Location />{app ? <App /> : <DashboardPage />}</MemoryRouter></QueryClientProvider>)
}

it('shows loading, separate cohorts, API compliance and only supported queue links', async () => {
  show()
  expect(screen.getByRole('status', { name: 'Loading dashboard' })).toBeTruthy()
  await screen.findByRole('heading', { name: 'Current Operations' })
  expect(screen.getByRole('heading', { name: 'Period Performance' })).toBeTruthy()
  expect(screen.getByText('66.7%')).toBeTruthy()
  expect(screen.getByText(/The date range below does not change/)).toBeTruthy()
  for (const [name, query] of [ ['Open work orders: 4. View queue', 'openOnly=true'], ['At risk: 1. View queue', 'openOnly=true&slaStatus=AtRisk'], ['Breached: 2. View queue', 'openOnly=true&slaStatus=Breached'] ]) {
    expect(screen.getByRole('link', { name }).getAttribute('href')).toBe(`/work-orders?${query}`)
  }
  expect(screen.getByRole('link', { name: /On hold: 1/ }).getAttribute('href')).toBe('/work-orders?openOnly=true&status=OnHold')
  expect(screen.getByRole('link', { name: /Unassigned: 1/ }).getAttribute('href')).toBe('/work-orders?openOnly=true&unassigned=true')
  expect(screen.getByRole('link', { name: /Leah Morgan: 3/ }).getAttribute('href')).toBe('/work-orders?openOnly=true&technicianId=tech-1')
  expect(screen.queryByRole('link', { name: /Completed work orders/ })).toBeNull()
})

it('validates dates and sends an exclusive next-day boundary across a month end', async () => {
  show()
  await screen.findByRole('heading', { name: 'Period Performance' })
  fireEvent.change(screen.getByLabelText('Start date', { exact: false }), { target: { value: '2026-09-01' } })
  fireEvent.change(screen.getByLabelText('End date (inclusive)', { exact: false }), { target: { value: '2026-08-31' } })
  fireEvent.submit(screen.getByRole('form', { name: 'Performance period' }))
  expect(screen.getByRole('alert').textContent).toContain('Choose valid dates in order')
  expect(getDashboard).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'Last 30 days' }))
  expect((screen.getByLabelText('End date (inclusive)', { exact: false }) as HTMLInputElement).value).toBe('2026-10-02')
  expect(screen.queryByRole('alert')).toBeNull()
  fireEvent.change(screen.getByLabelText('Start date', { exact: false }), { target: { value: '2026-09-01' } })
  fireEvent.change(screen.getByLabelText('End date (inclusive)', { exact: false }), { target: { value: '2026-09-30' } })
  fireEvent.submit(screen.getByRole('form', { name: 'Performance period' }))
  await waitFor(() => expect(screen.getByLabelText('URL').textContent).toBe('?startDate=2026-09-01&endDateExclusive=2026-10-01'))
  expect(vi.mocked(getDashboard).mock.calls.some(([query]) => query === 'startDate=2026-09-01&endDateExclusive=2026-10-01')).toBe(true)
})

it('shows no-completion and no-backlog states without a misleading percentage', async () => {
  vi.mocked(getDashboard).mockResolvedValue({ ...data, current: { open: 0, new: 0, assigned: 0, inProgress: 0, onHold: 0, good: 0, atRisk: 0, breached: 0 },
    workload: [{ technicianId: null, technicianName: 'Unassigned', openCount: 0 }], period: { ...data.period, completed: 0, met: 0, missed: 0, compliancePercent: null } })
  show()
  await screen.findByText('No work orders were completed in this period. SLA compliance is not applicable.')
  expect(screen.getByText('No open work orders. There is no current backlog.')).toBeTruthy()
  expect(screen.getByText('—')).toBeTruthy()
  expect(screen.queryByText('0.0%')).toBeNull()
})

it('offers a retry after a failed dashboard request', async () => {
  vi.mocked(getDashboard).mockRejectedValueOnce(new Error('Unable to load the dashboard.'))
  show()
  await screen.findByRole('alert')
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  await screen.findByRole('heading', { name: 'Current Operations' })
})

it('hides manager navigation and denies a direct dashboard route for Operations', async () => {
  vi.mocked(getSession).mockResolvedValue({ id: 'ops', email: 'elena.brooks@atlas.example', displayName: 'Elena Brooks', roles: ['Operations'] })
  show(true)
  await screen.findByText('Manager access is required to view the dashboard.')
  expect(screen.queryByRole('link', { name: 'Dashboard' })).toBeNull()
  expect(getDashboard).not.toHaveBeenCalled()
})

it('shows dashboard navigation and data for Manager', async () => {
  show(true)
  await screen.findByRole('heading', { name: 'Current Operations' })
  expect(screen.getByRole('link', { name: 'Dashboard' }).getAttribute('href')).toBe('/dashboard')
})
