import { statusLabels } from './statusLabels'
import { Alert, Box, Button, Chip, Divider, Paper, Skeleton, Stack, Tab, Tabs, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams, useSearchParams } from 'react-router'
import { getWorkOrder, WorkOrderApiError } from './api'
import { WorkOrderActions } from './WorkOrderActions'
import { WorkOrderActivity } from './WorkOrderActivity'

function timestamp(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

export function WorkOrderDetailPage() {
  const { id = '' } = useParams()
  const location = useLocation()
  const [params, setParams] = useSearchParams()
  const tab = params.get('tab') === 'activity' ? 'activity' : 'overview'
  const order = useQuery({ queryKey: ['work-order', id], queryFn: ({ signal }) => getWorkOrder(id, signal), refetchInterval: 60_000 })
  if (order.isPending) return <Box aria-label="Loading work order" role="status"><Typography>Loading work order…</Typography><Skeleton height={80} /><Skeleton variant="rounded" height={280} /></Box>
  if (order.isError && !order.data) {
    const missing = order.error instanceof WorkOrderApiError && order.error.status === 404
    return <Paper variant="outlined" sx={{ p: 4 }}><Typography component="h1" variant="h2">{missing ? 'Work order not found' : 'Unable to load work order'}</Typography>
      <Typography color="text.secondary" sx={{ my: 2 }}>{missing ? 'Check the link or return to your workspace.' : 'The work order could not be retrieved. Please try again.'}</Typography>
      <Stack direction="row" spacing={2}>{!missing && <Button variant="contained" onClick={() => order.refetch()}>Try again</Button>}<Button component={Link} to="/">Return home</Button></Stack></Paper>
  }
  const detail = order.data!
  const state = { Good: { label: 'Good', color: 'success' }, AtRisk: { label: 'At risk', color: 'warning' }, Breached: { label: 'Breached', color: 'error' } } as const
  const badge = detail.slaState ? state[detail.slaState] : { label: detail.completedAt ? (Date.parse(detail.completedAt) <= Date.parse(detail.slaDeadlineAt) ? 'Met' : 'Missed') : 'Not applicable', color: 'default' as const }
  return <>
    <Button component={Link} to={typeof location.state?.returnTo === 'string' && /^\/work-orders(?:\?|$)/.test(location.state.returnTo) ? location.state.returnTo : '/work-orders'} sx={{ mb: 2 }}>← Back to work orders</Button>
    {location.state?.created && <Alert severity="success" sx={{ mb: 3 }}>Work order {detail.number} created.</Alert>}
    {order.isError && <Alert severity="warning" sx={{ mb: 3 }} action={<Button onClick={() => order.refetch()}>Retry</Button>}>Refresh failed. Showing the last loaded information; SLA status may be out of date.</Alert>}
    <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 1.5 }}><Typography className="eyebrow">{detail.number}</Typography><Chip label={statusLabels[detail.status]} size="small" variant="outlined" /></Stack>
    <Typography component="h1" variant="h1" sx={{ fontSize: 32, overflowWrap: 'anywhere', maxWidth: 900 }}>{detail.title}</Typography>
    <Typography color="text.secondary" sx={{ mt: 1, mb: 4 }}>{detail.customerName} · {detail.locationName}</Typography>
    <WorkOrderActions detail={detail} />
    <Tabs value={tab} onChange={(_, value: string) => { const next = new URLSearchParams(params); if (value === "overview") next.delete("tab"); else next.set("tab", value); setParams(next, { state: location.state }) }} aria-label="Work order views" sx={{ mb: 3 }}><Tab value="overview" label="Overview" id="overview-tab" aria-controls="overview-panel" /><Tab value="activity" label="Activity" id="activity-tab" aria-controls="activity-panel" /></Tabs>
    {tab === "activity" ? <div role="tabpanel" id="activity-panel" aria-labelledby="activity-tab"><WorkOrderActivity id={id} /></div> : <div className="work-order-layout" role="tabpanel" id="overview-panel" aria-labelledby="overview-tab">
      <Paper variant="outlined" sx={{ p: { xs: 2.5, sm: 4 } }}>
        <Typography component="h2" variant="h2" sx={{ fontSize: 19, mb: 3 }}>Work order overview</Typography>
        <Box component="dl" className="detail-grid">
          <div><dt>Customer</dt><dd>{detail.customerName}</dd></div>
          <div><dt>Location</dt><dd>{detail.locationName}<small>{detail.locationAddress}</small></dd></div>
          <div><dt>Service type</dt><dd>{detail.serviceType === 'GeneralMaintenance' ? 'General Maintenance' : detail.serviceType}</dd></div>
          <div><dt>Priority</dt><dd><Chip size="small" variant="outlined" label={detail.priority} color={detail.priority === 'Critical' ? 'error' : detail.priority === 'High' ? 'warning' : 'default'} /></dd></div>
          <div><dt>Created</dt><dd><time dateTime={detail.createdAt}>{timestamp(detail.createdAt)}</time></dd></div>
          <div><dt>Assigned technician</dt><dd>{detail.technicianName ?? "Unassigned"}</dd></div>
        </Box>
        {detail.holdReason && <Alert severity="warning" sx={{ mt: 3, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>On hold: {detail.holdReason}</Alert>}
        {detail.completedAt && <Alert severity="success" sx={{ mt: 3 }}>Completed {timestamp(detail.completedAt)}. This work order is closed.<Typography sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{detail.resolutionSummary}</Typography></Alert>}
        {detail.cancelledAt && <Alert severity="info" sx={{ mt: 3 }}>Cancelled {timestamp(detail.cancelledAt)}. This work order is closed.<Typography sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{detail.cancellationReason}</Typography></Alert>}
        <Divider sx={{ my: 3 }} />
        <Typography component="h2" variant="h2" sx={{ fontSize: 19, mb: 2 }}>Description</Typography>
        <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', lineHeight: 1.8 }}>{detail.description}</Typography>
      </Paper>
      <Paper component="aside" variant="outlined" sx={{ p: 3, alignSelf: 'start' }}>
        <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 3 }}><Typography component="h2" variant="h2" sx={{ fontSize: 18 }}>SLA status</Typography><Chip size="small" label={badge.label} color={badge.color} variant="outlined" /></Stack>
        <Typography variant="body2" color="text.secondary">Resolution deadline</Typography>
        <Typography sx={{ fontSize: 20, fontWeight: 600, mt: 0.5 }}><time dateTime={detail.slaDeadlineAt}>{timestamp(detail.slaDeadlineAt)}</time></Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>{detail.slaDurationMinutes / 60} hours from creation</Typography>
        <Divider sx={{ my: 2.5 }} />
        <Typography variant="body2" color="text.secondary">Evaluated {timestamp(detail.evaluatedAt)}.<br />Refreshes every minute while this page is active.</Typography>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>Times shown in {Intl.DateTimeFormat().resolvedOptions().timeZone}.</Typography>
      </Paper>
    </div>}
  </>
}
