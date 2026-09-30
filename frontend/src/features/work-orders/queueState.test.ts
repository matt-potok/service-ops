import { expect, it } from 'vitest'
import { changeQueueParams, readQueueState } from './queueState'

it('reads repeated statuses and defaults without losing the original URL', () => {
  const params = new URLSearchParams('status=New&status=New&createdFrom=2026-09-29T00%3A00%3A00Z')
  expect(readQueueState(params)).toMatchObject({ status: ['New', 'New'], page: 1, pageSize: 25, sort: '-createdAt', createdFrom: '2026-09-29T00:00:00Z' })
  expect(params.getAll('status')).toHaveLength(2)
})

it('keeps invalid pagination in the request URL while giving the grid safe display values', () => {
  const params = new URLSearchParams('page=NaN&pageSize=999')
  expect(readQueueState(params)).toMatchObject({ page: 1, pageSize: 25 })
  expect(params.toString()).toBe('page=NaN&pageSize=999')
})

it('changes sort/filter/page sizes with a page reset but preserves pagination changes', () => {
  const start = new URLSearchParams('search=unit&slaStatus=AtRisk&page=4&pageSize=25')
  expect(changeQueueParams(start, { sort: 'priority' }).get('page')).toBeNull()
  const resized = changeQueueParams(start, { pageSize: '50' })
  expect(readQueueState(resized)).toMatchObject({ search: 'unit', slaStatus: 'AtRisk', page: 1, pageSize: 50 })
  expect(changeQueueParams(start, { page: '5' }, false).get('page')).toBe('5')
  expect(start.get('page')).toBe('4')
})
