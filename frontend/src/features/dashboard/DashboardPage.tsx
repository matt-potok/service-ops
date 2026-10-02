import { useState, type FormEvent } from 'react'
import { Alert, Box, Button, CardActionArea, Chip, LinearProgress, Paper, Skeleton, Stack, TextField, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { getDashboard, shiftDate, type Dashboard } from './api'

const panel = { p: { xs: 2.5, md: 3 }, borderRadius: 2 }
const cards = { display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }

export function DashboardPage() {
  const [params, setParams] = useSearchParams()
  const dashboard = useQuery({ queryKey: ['dashboard', params.toString()],
    queryFn: ({ signal }) => getDashboard(params.toString(), signal), refetchInterval: 60_000, refetchOnWindowFocus: true })
  const heading = <><Typography variant="overline" color="text.secondary">MANAGER WORKSPACE</Typography>
    <Typography component="h1" variant="h1" sx={{ mt: 0.5 }}>Operations overview</Typography>
    <Typography color="text.secondary" sx={{ mt: 1 }}>A live view of the backlog, alongside completed-work performance.</Typography></>
  if (dashboard.isPending) return <>{heading}<Box role="status" aria-label="Loading dashboard" sx={{ mt: 4 }}><Typography>Loading dashboard…</Typography><Skeleton height={160} /><Skeleton height={260} /></Box></>
  if (dashboard.isError) return <>{heading}<Alert severity="error" sx={{ mt: 3 }}>{dashboard.error.message}</Alert>
    <Stack direction="row" spacing={2} sx={{ mt: 2 }}><Button variant="outlined" onClick={() => dashboard.refetch()}>Try again</Button>
      <Button onClick={() => setParams({})}>Use last 30 days</Button></Stack></>
  const data = dashboard.data
  const current = data.current
  const statuses = [ ['New', current.new, 'New'], ['Assigned', current.assigned, 'Assigned'],
    ['In progress', current.inProgress, 'InProgress'], ['On hold', current.onHold, 'OnHold'] ] as const
  return <>
    <Stack direction={{ xs: 'column', md: 'row' }} sx={{ justifyContent: 'space-between', alignItems: { md: 'center' }, gap: 2 }}>
      <Box>{heading}</Box><Button variant="outlined" onClick={() => dashboard.refetch()} disabled={dashboard.isFetching}>{dashboard.isFetching ? 'Refreshing…' : 'Refresh'}</Button>
    </Stack>
    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
      Evaluated {new Intl.DateTimeFormat('en-US', { timeZone: data.timeZone, dateStyle: 'medium', timeStyle: 'short' }).format(new Date(data.evaluatedAt))} · {data.timeZone} · Refreshes every minute while active
    </Typography>
    <Box component="section" aria-labelledby="current-heading" sx={{ mt: 4 }}>
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', mb: 0.5 }}><Typography component="h2" variant="h2" id="current-heading">Current Operations</Typography><Chip size="small" label="Live backlog" variant="outlined" /></Stack>
      <Typography color="text.secondary" sx={{ mb: 2.5 }}>All open work, regardless of age. The date range below does not change these counts.</Typography>
      {current.open === 0 && <Alert severity="info" sx={{ mb: 2 }}>No open work orders. There is no current backlog.</Alert>}
      <Box sx={cards}>
        <Metric label="Open work orders" value={current.open} to="/work-orders?openOnly=true" />
        <Metric label="Good" value={current.good} to="/work-orders?openOnly=true&slaStatus=Good" tone="#356746" />
        <Metric label="At risk" value={current.atRisk} to="/work-orders?openOnly=true&slaStatus=AtRisk" tone="#965709" />
        <Metric label="Breached" value={current.breached} to="/work-orders?openOnly=true&slaStatus=Breached" tone="#a3363b" />
      </Box>
      <Paper variant="outlined" sx={{ ...panel, mt: 2.5 }}>
        <Typography component="h3" variant="h6" sx={{ mb: 2 }}>Open work by status</Typography>
        <Box sx={cards}>{statuses.map(([label, count, code]) => <Button key={code} aria-label={`${label}: ${count}. View queue`} component={Link} to={`/work-orders?openOnly=true&status=${code}`} variant="outlined" sx={{ justifyContent: 'space-between', px: 2, py: 1.5 }}>
          <span>{label}</span><strong>{count}</strong></Button>)}</Box>
      </Paper>
      <Paper variant="outlined" sx={{ ...panel, mt: 2.5 }}>
        <Typography component="h3" variant="h6">Technician workload</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 2 }}>Open orders by current assignment. Counts show workload, not technician capacity.</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, columnGap: 4, rowGap: 1 }}>
          {data.workload.map(row => <Box key={row.technicianId ?? 'unassigned'} sx={{ pb: 1 }}>
            <Button aria-label={`${row.technicianName}: ${row.openCount}. View queue`} component={Link} to={`/work-orders?openOnly=true&${row.technicianId ? `technicianId=${row.technicianId}` : 'unassigned=true'}`} sx={{ width: '100%', justifyContent: 'space-between', px: 0, mb: 0.5 }}>
              <span>{row.technicianName}</span><strong>{row.openCount}</strong>
            </Button>
            <LinearProgress aria-label={`${row.technicianName} share of open work`} variant="determinate" value={current.open ? row.openCount / current.open * 100 : 0} sx={{ height: 6, borderRadius: 3, bgcolor: '#edf1ee', ...(row.technicianId ? {} : { '& .MuiLinearProgress-bar': { bgcolor: '#8c7248' } }) }} />
          </Box>)}
        </Box>
        <Typography variant="caption" color="text.secondary">Technicians with no open work are omitted. Unassigned work is always shown separately.</Typography>
      </Paper>
    </Box>
    <PeriodSection key={`${data.period.startDate}/${data.period.endDateExclusive}`} period={data.period} timeZone={data.timeZone}
      onApply={(start, end) => setParams({ startDate: start, endDateExclusive: end })} onReset={() => setParams({})} />
    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 3 }}>Queue links show live results; later edits or elapsed time can change the counts.</Typography>
  </>
}

function Metric({ label, value, to, tone = '#245953' }: { label: string; value: number | string; to?: string; tone?: string }) {
  const body = <Box sx={{ p: { xs: 2, md: 2.5 } }}><Typography color="text.secondary" variant="body2">{label}</Typography>
    <Typography component="p" sx={{ fontSize: { xs: 30, md: 38 }, fontWeight: 650, color: tone, mt: 0.5, lineHeight: 1.2 }}>{value}</Typography>
    {to && <Typography variant="caption" color="primary" sx={{ display: 'block', mt: 1 }}>View queue →</Typography>}</Box>
  return <Paper variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>{to ? <CardActionArea component={Link} to={to} aria-label={`${label}: ${value}. View queue`}>{body}</CardActionArea> : body}</Paper>
}

function PeriodSection({ period, timeZone, onApply, onReset }: { period: Dashboard['period']; timeZone: string; onApply: (start: string, end: string) => void; onReset: () => void }) {
  const [start, setStart] = useState(period.startDate)
  const [end, setEnd] = useState(shiftDate(period.endDateExclusive, -1))
  const [error, setError] = useState('')
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!start || !end || start > end || start < '1900-01-01' || end >= '2100-12-31') { setError('Choose valid dates in order, between 1900 and 2100.'); return }
    const exclusive = shiftDate(end, 1)
    if ((Date.parse(`${exclusive}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / 86400000 > 366) { setError('Choose a period of at most 366 calendar days.'); return }
    setError(''); onApply(start, exclusive)
  }
  return <Box component="section" aria-labelledby="period-heading" sx={{ mt: 5 }}>
    <Typography component="h2" variant="h2" id="period-heading">Period Performance</Typography>
    <Typography color="text.secondary" sx={{ mt: 0.5 }}>Completed work selected by completion date, regardless of when it was created.</Typography>
    <Paper variant="outlined" sx={{ ...panel, mt: 2.5 }}>
      <Box component="form" onSubmit={submit} aria-label="Performance period">
        <Stack direction={{ xs: 'column', sm: 'row' }} useFlexGap sx={{ gap: 2, flexWrap: 'wrap', alignItems: { sm: 'flex-start' } }}>
          <TextField label="Start date" type="date" size="small" required value={start} onChange={e => setStart(e.target.value)} slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: '1900-01-01', max: '2100-12-30' } }} />
          <TextField label="End date (inclusive)" type="date" size="small" required value={end} onChange={e => setEnd(e.target.value)} slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: '1900-01-01', max: '2100-12-30' } }} />
          <Button type="submit" variant="contained">Apply period</Button><Button onClick={() => { setStart(period.startDate); setEnd(shiftDate(period.endDateExclusive, -1)); setError(''); onReset() }}>Last 30 days</Button>
        </Stack>
        {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
        <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>Calendar dates in {timeZone}. Both dates include the full calendar day. Maximum 366 days.</Typography>
      </Box>
      <Typography component="h3" variant="h6" sx={{ mt: 3, mb: 2 }}>Results: {period.startDate} through {shiftDate(period.endDateExclusive, -1)}</Typography>
      {period.completed === 0 && <Alert severity="info" sx={{ mb: 2 }}>No work orders were completed in this period. SLA compliance is not applicable.</Alert>}
      <Box sx={cards}>
        <Metric label="Completed work orders" value={period.completed} />
        <Metric label="SLA met" value={period.met} tone="#356746" />
        <Metric label="SLA missed" value={period.missed} tone="#a3363b" />
        <Metric label="SLA compliance" value={period.compliancePercent == null ? '—' : `${period.compliancePercent.toFixed(1)}%`} />
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>Met means completed at or before the deadline. Compliance is met ÷ completed; cancelled work is excluded.</Typography>
    </Paper>
  </Box>
}
