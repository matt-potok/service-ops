import { getCsrfToken } from '../../lib/api/client'
import type { components } from '../../lib/api/schema'

export type CreateWorkOrderInput = components['schemas']['CreateWorkOrderRequest']
export type WorkOrderDetail = components['schemas']['WorkOrderDetail']
export type WorkOrderSummary = components['schemas']['WorkOrderSummary']
export type WorkOrderPage = components['schemas']['WorkOrderPage']
export type CustomerOption = components['schemas']['CustomerOption']
export type LocationOption = components['schemas']['LocationOption']
export type CreationOptions = components['schemas']['CreationOptions']

export class WorkOrderApiError extends Error {
  constructor(message: string, public readonly status: number, public readonly fields: Record<string, string> = {}) {
    super(message)
  }
}

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; errors?: Record<string, string[]> } | null
    const fields = Object.fromEntries(Object.entries(problem?.errors ?? {}).map(([key, messages]) => {
      const name = key.replace(/^\$\./, '')
      return [name.charAt(0).toLowerCase() + name.slice(1), messages.join(' ')]
    }))
    throw new WorkOrderApiError(response.status === 401 ? 'Your session has expired. Sign in again to continue.'
      : problem?.detail ?? (response.status === 400 ? 'Check the highlighted fields and try again.' : 'Unable to complete this request. Please try again.'),
    response.status, fields)
  }
  return response.json()
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { credentials: 'same-origin', signal })
  return read<T>(response)
}

export const getCustomers = (signal?: AbortSignal) => get<CustomerOption[]>('/api/v1/customers', signal)
export const getLocations = (id: string, signal?: AbortSignal) => get<LocationOption[]>(`/api/v1/customers/${id}/locations`, signal)
export const getCreationOptions = (signal?: AbortSignal) => get<CreationOptions>('/api/v1/reference-data', signal)
export const getWorkOrder = (id: string, signal?: AbortSignal) => get<WorkOrderDetail>(`/api/v1/work-orders/${id}`, signal)
export const getWorkOrders = (query: string, signal?: AbortSignal) => get<WorkOrderPage>(`/api/v1/work-orders?${query}`, signal)

export async function createWorkOrder(input: CreateWorkOrderInput): Promise<WorkOrderDetail> {
  const token = await getCsrfToken()
  let response: Response
  try {
    response = await fetch('/api/v1/work-orders', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(input),
    })
  } catch {
    throw new Error('The connection was interrupted. Creation could not be confirmed; the order may have been saved. Avoid resubmitting until you have checked the connection.')
  }
  return read<WorkOrderDetail>(response)
}
