import type { components } from '../../lib/api/schema'

export type Dashboard = components['schemas']['DashboardResponse']

export async function getDashboard(query: string, signal?: AbortSignal): Promise<Dashboard> {
  const response = await fetch(`/api/v1/dashboard${query ? `?${query}` : ''}`, { credentials: 'same-origin', signal })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; errors?: Record<string, string[]> } | null
    throw new Error(response.status === 401 ? 'Your session has expired. Sign in again to continue.'
      : response.status === 403 ? 'Manager access is required to view the dashboard.'
      : Object.values(problem?.errors ?? {}).flat().join(' ') || problem?.detail || 'Unable to load the dashboard. Please try again.')
  }
  return response.json()
}

// Calendar arithmetic only. Reporting-zone conversion belongs to the server, not the browser's zone.
export function shiftDate(date: string, days: number) {
  const value = new Date(`${date}T00:00:00Z`)
  value.setUTCDate(value.getUTCDate() + days)
  return value.toISOString().slice(0, 10)
}
