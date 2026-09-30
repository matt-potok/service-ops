// URL parameters are the queue's source of truth. Invalid API values remain visible as errors.
export function readQueueState(params: URLSearchParams) {
  const page = Number(params.get('page'))
  const pageSize = Number(params.get('pageSize'))
  return {
    search: params.get('search') ?? '', customerId: params.get('customerId') ?? '',
    locationId: params.get('locationId') ?? '', serviceType: params.get('serviceType') ?? '',
    status: params.getAll('status'), slaStatus: params.get('slaStatus') ?? '',
    createdFrom: params.get('createdFrom') ?? '', createdTo: params.get('createdTo') ?? '',
    openOnly: params.get('openOnly') === 'true', sort: params.get('sort') || '-createdAt',
    // Safe presentation values for the grid; the unchanged URL is still validated by the API.
    page: Number.isInteger(page) && page >= 1 && page <= 2147483647 ? page : 1,
    pageSize: [25, 50, 100].includes(pageSize) ? pageSize : 25,
  }
}

export function changeQueueParams(current: URLSearchParams, changes: Record<string, string>, resetPage = true) {
  const next = new URLSearchParams(current)
  for (const [key, value] of Object.entries(changes)) {
    if (value) next.set(key, value)
    else next.delete(key)
    if (key === 'customerId') next.delete('locationId')
  }
  if (resetPage) next.delete('page')
  return next
}
