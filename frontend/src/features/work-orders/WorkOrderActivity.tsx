import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material'
import { useInfiniteQuery } from '@tanstack/react-query'
import { getActivity } from './api'

const labels: Record<string, string> = { Created: 'Work order created', Assigned: 'Technician assigned', Reassigned: 'Technician changed', Unassigned: 'Technician unassigned', DetailsCorrected: 'Details corrected', Started: 'Work started', PlacedOnHold: 'Placed on hold', Resumed: 'Work resumed', Completed: 'Work completed', Cancelled: 'Work cancelled' }
// A small renderer for the known Phase 4 activity fields; never display arbitrary metadata.
type Changes = { previousTechnicianName?: string; technicianName?: string; reason?: string; summary?: string; title?: { before: string; after: string }; description?: { before: string; after: string } }

export function WorkOrderActivity({ id }: { id: string }) {
  const activity = useInfiniteQuery({ queryKey: ['work-order-activity', id], initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) => getActivity(id, pageParam, signal), getNextPageParam: page => page.nextCursor ?? undefined })
  if (activity.isPending) return <CircularProgress aria-label="Loading activity" />
  return <Paper variant="outlined" sx={{ p: { xs: 2.5, sm: 4 } }}>
    <Typography component="h2" variant="h2" sx={{ fontSize: 20, mb: 1 }}>Activity</Typography>
    <Typography color="text.secondary" sx={{ mb: 3 }}>Oldest first · Changes recorded with the work order</Typography>
    {activity.isError && <Alert severity="error" action={<Button onClick={() => activity.refetch()}>Retry</Button>}>Activity could not be loaded.</Alert>}
    <Box component="ol" sx={{ listStyle: 'none', p: 0, m: 0 }}>
      {activity.data?.pages.flatMap(page => page.items).map(item => {
        const changes = item.changes as Changes | null
        return <Box component="li" key={item.id} sx={{ borderLeft: '2px solid #d9e5df', pl: 3, pb: 3, overflowWrap: 'anywhere' }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ justifyContent: 'space-between', gap: 0.5 }}><Typography sx={{ fontWeight: 600 }}>{labels[item.eventType]}</Typography><Typography component="time" dateTime={item.effectiveAt} variant="body2" color="text.secondary">{new Date(item.effectiveAt).toLocaleString()}</Typography></Stack>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{item.actorName}</Typography>
          {(changes?.technicianName || changes?.previousTechnicianName) && <Typography>{changes.previousTechnicianName ?? 'Unassigned'} → {changes.technicianName ?? 'Unassigned'}</Typography>}
          {(changes?.reason || changes?.summary) && <Typography sx={{ whiteSpace: 'pre-wrap' }}>{changes.reason ?? changes.summary}</Typography>}
          {(['title', 'description'] as const).map(field => changes?.[field] && <Box key={field} sx={{ mt: 1 }}><Typography variant="body2" sx={{ fontWeight: 600, textTransform: 'capitalize' }}>{field}</Typography><Typography sx={{ whiteSpace: 'pre-wrap', color: 'text.secondary' }}>Before: {changes[field].before}</Typography><Typography sx={{ whiteSpace: 'pre-wrap' }}>After: {changes[field].after}</Typography></Box>)}
        </Box>
      })}
    </Box>
    {activity.data?.pages[0].items.length === 0 && <Typography>No activity yet.</Typography>}
    {activity.hasNextPage && <Button onClick={() => activity.fetchNextPage()} disabled={activity.isFetchingNextPage}>{activity.isFetchingNextPage ? 'Loading…' : 'Load more activity'}</Button>}
  </Paper>
}
