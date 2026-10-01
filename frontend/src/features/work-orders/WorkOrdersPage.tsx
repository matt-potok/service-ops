import { statusLabels } from './statusLabels'
import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Button, Chip, Collapse, FormControlLabel, Checkbox, LinearProgress, Paper, Stack, MenuItem, TextField, Typography, useMediaQuery } from '@mui/material'
import { DataGrid, type GridColDef, type GridSortModel } from '@mui/x-data-grid'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useLocation, useSearchParams } from 'react-router'
import { getCreationOptions, getCustomers, getLocations, getTechnicians, getWorkOrders, WorkOrderApiError, type WorkOrderSummary } from './api'
import { changeQueueParams, readQueueState } from './queueState'

const slaLabels = { Good: 'Good', AtRisk: 'At risk', Breached: 'Breached' } as const
const slaColors = { Good: 'success', AtRisk: 'warning', Breached: 'error' } as const
const dateLabel = (value: string) => new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }).format(new Date(value))
const utcInput = (value: string) => value && Number.isFinite(Date.parse(value)) ? new Date(value).toISOString().slice(0, 19) : ''
const utcValue = (value: string) => value ? `${value.length === 16 ? `${value}:00` : value}Z` : ''

export function WorkOrdersPage() {
  const compact = useMediaQuery('(max-width:1000px)')
  const [params, setParams] = useSearchParams()
  const location = useLocation()
  const state = readQueueState(params)
  const [search, setSearch] = useState(state.search)
  const [moreFilters, setMoreFilters] = useState(false)
  const queryString = params.toString()
  // Stable model identity prevents the grid treating every render as a new sort and resetting its page.
  const sortModel = useMemo<GridSortModel>(() => [{ field: state.sort.replace(/^-/, ''), sort: state.sort.startsWith('-') ? 'desc' : 'asc' }], [state.sort])
  const paginationModel = useMemo(() => ({ page: state.page - 1, pageSize: state.pageSize }), [state.page, state.pageSize])
  const customers = useQuery({ queryKey: ['customers'], queryFn: ({ signal }) => getCustomers(signal) })
  const technicians = useQuery({ queryKey: ['technicians'], queryFn: ({ signal }) => getTechnicians(signal) })
  const options = useQuery({ queryKey: ['creation-options'], queryFn: ({ signal }) => getCreationOptions(signal) })
  const locations = useQuery({ queryKey: ['locations', state.customerId], queryFn: ({ signal }) => getLocations(state.customerId, signal), enabled: !!state.customerId })
  const orders = useQuery({ queryKey: ['work-orders', queryString], queryFn: ({ signal }) => getWorkOrders(queryString, signal), placeholderData: keepPreviousData, refetchInterval: 60_000 })

  useEffect(() => { setSearch(state.search) }, [state.search, location.key])
  useEffect(() => {
    if (search === state.search) return
    const timer = setTimeout(() => setParams(changeQueueParams(new URLSearchParams(queryString), { search: search.trim() })), 350)
    return () => clearTimeout(timer)
  }, [search, state.search, queryString, setParams, location.key])

  const change = (changes: Record<string, string>, resetPage = true) => setParams(changeQueueParams(params,
    { ...(search !== state.search ? { search: search.trim() } : {}), ...changes }, resetPage || search !== state.search))
  const clear = () => { setSearch(''); setParams(new URLSearchParams()) }
  const filterEntries = [...params.entries()].filter(([key, value]) => !['sort', 'page', 'pageSize'].includes(key) && value !== '' && !(key === 'openOnly' && value === 'false'))
  const hasFilters = filterEntries.length > 0
  // Only when a filtered query is empty: distinguish an empty database from no matches.
  const existence = useQuery({ queryKey: ['work-orders', ''], queryFn: ({ signal }) => getWorkOrders('', signal), enabled: hasFilters && orders.isSuccess && !orders.isPlaceholderData && orders.data.totalCount === 0 })
  const emptyDatabase = !hasFilters ? orders.data?.totalCount === 0 : existence.data?.totalCount === 0
  const returnTo = `/work-orders${location.search}`
  const columns = useMemo<GridColDef<WorkOrderSummary>[]>(() => {
    const fields: GridColDef<WorkOrderSummary>[] = [
    { field: 'number', headerName: 'Work order', width: 290, renderCell: ({ row, tabIndex }) => <Box className="queue-identity"><Link tabIndex={tabIndex} to={`/work-orders/${row.id}`} state={{ returnTo }}>{row.number}</Link><span title={row.title}>{row.title}</span></Box> },
    { field: 'customerName', headerName: 'Customer / location', width: 245, renderCell: ({ row }) => <Box className="queue-identity"><span title={row.customerName}>{row.customerName}</span><small title={row.locationName}>{row.locationName}</small></Box> },
    { field: 'priority', headerName: 'Priority', width: 105, renderCell: ({ row }) => <Chip size="small" variant="outlined" label={row.priority} color={row.priority === 'Critical' ? 'error' : row.priority === 'High' ? 'warning' : 'default'} /> },
    { field: 'status', headerName: 'Status', width: 125, renderCell: ({ row }) => <Chip size="small" label={statusLabels[row.status]} variant="outlined" /> },
    { field: 'slaState', headerName: 'SLA', width: 100, sortable: false, renderCell: ({ row }) => <Chip size="small" label={row.slaState ? slaLabels[row.slaState] : '—'} color={row.slaState ? slaColors[row.slaState] : 'default'} variant="outlined" /> },
    { field: 'deadline', headerName: 'Deadline', width: 170, valueFormatter: value => dateLabel(value as string) },
    { field: 'serviceType', headerName: 'Service', width: 150, sortable: false, valueFormatter: value => value === 'GeneralMaintenance' ? 'General Maintenance' : value },
    { field: 'createdAt', headerName: 'Created', width: 170, valueFormatter: value => dateLabel(value as string) },
    { field: 'technicianName', headerName: 'Technician', width: 180, sortable: false, valueFormatter: value => value ?? 'Unassigned' },
    ]
    if (compact) {
      fields[0].width = 240
      return [fields[0], fields[2], fields[4], fields[5], fields[1], fields[3], fields[8], fields[6], fields[7]]
    }
    return fields
  }, [returnTo, compact])
  const selectionProps = { select: { native: true }, inputLabel: { shrink: true } } as const
  const failure = orders.error instanceof WorkOrderApiError ? orders.error : null
  const invalidFields = failure ? Object.values(failure.fields).join(' ') : ''
  const zeroRows = orders.isSuccess && !orders.isPlaceholderData && orders.data.items.length === 0

  function chipLabel(key: string, value: string) {
    if (key === 'customerId') return `Customer: ${customers.data?.find(x => x.id === value)?.name ?? value}`
    if (key === 'locationId') return `Location: ${locations.data?.find(x => x.id === value)?.name ?? value}`
    if (key === 'slaStatus') return `SLA: ${slaLabels[value as keyof typeof slaLabels] ?? value}`
    if (key === 'technicianId') return "Technician: " + (technicians.data?.find(x => x.id === value)?.displayName ?? value)
    if (key === 'unassigned') return value === 'true' ? 'Unassigned only' : 'Assigned only'
    if (key === 'status') return 'Status: ' + (statusLabels[value as keyof typeof statusLabels] ?? value)
    if (key === 'openOnly') return 'Open only'
    return `${({ search: 'Search', serviceType: 'Service', status: 'Status', createdFrom: 'Created from', createdTo: 'Created before' } as Record<string, string>)[key] ?? key}: ${value}`
  }

  return <div className="queue-page">
    <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'start', gap: 2, mb: 3 }}>
      <div><Typography component="h1" variant="h1" sx={{ fontSize: 32 }}>Work orders</Typography>
        <Typography color="text.secondary" sx={{ mt: 1 }}>{orders.data ? `${orders.data.totalCount.toLocaleString()} ${hasFilters ? 'matching' : 'total'} work orders` : 'Find and inspect service requests.'}</Typography></div>
      <Button component={Link} to="/work-orders/new" variant="contained" sx={{ whiteSpace: 'nowrap' }}>Create work order</Button>
    </Stack>
    <Paper variant="outlined" sx={{ p: 2.5, mb: 2 }}>
      <Stack direction="row" sx={{ gap: 2, alignItems: 'center', mb: 2 }}>
        <TextField size="small" fullWidth label="Search work orders" placeholder="Work-order number or title" value={search} onChange={e => setSearch(e.target.value)} slotProps={{ htmlInput: { maxLength: 200 } }} />
        <Button variant="outlined" onClick={() => setMoreFilters(!moreFilters)} aria-expanded={moreFilters} sx={{ whiteSpace: 'nowrap' }}>More filters</Button>
      </Stack>
      <div className="queue-filters">
        <TextField size="small" select label="Customer" value={state.customerId} onChange={e => change({ customerId: e.target.value })} slotProps={selectionProps}>
          <option value="">All customers</option>{customers.data?.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
        </TextField>
        <TextField size="small" select label="Location" value={state.locationId} disabled={!state.customerId || locations.isPending} onChange={e => change({ locationId: e.target.value })} slotProps={selectionProps}>
          <option value="">{state.customerId ? 'All locations' : 'Choose customer first'}</option>{locations.data?.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
          {state.locationId && !locations.data?.some(x => x.id === state.locationId) && <option value={state.locationId}>Location from URL: {state.locationId}</option>}
        </TextField>
        <TextField size="small" select label="Service type" value={state.serviceType} onChange={e => change({ serviceType: e.target.value })} slotProps={selectionProps}>
          <option value="">All services</option>{options.data?.serviceTypes.map(x => <option key={x.code} value={x.code}>{x.label}</option>)}
        </TextField>
        <TextField size="small" select label="Status" value={state.status} slotProps={{ inputLabel: { shrink: true }, select: { multiple: true, displayEmpty: true, renderValue: value => (value as (keyof typeof statusLabels)[]).map(status => statusLabels[status] ?? status).join(', ') || 'All statuses' } }} onChange={e => {
          const values = typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value as string[]
          const next = changeQueueParams(params, { status: '', ...(search !== state.search ? { search: search.trim() } : {}) })
          values.forEach(value => next.append('status', value)); setParams(next)
        }}>{Object.entries(statusLabels).map(([value, label]) => <MenuItem key={value} value={value}><Checkbox checked={state.status.includes(value)} />{label}</MenuItem>)}</TextField>
        <TextField size="small" select label="Technician" value={state.technicianId || (state.unassigned === 'true' ? 'unassigned' : state.unassigned === 'false' ? 'assigned' : '')} onChange={e => change({ technicianId: ['unassigned', 'assigned'].includes(e.target.value) ? '' : e.target.value, unassigned: e.target.value === 'unassigned' ? 'true' : e.target.value === 'assigned' ? 'false' : '' })} slotProps={selectionProps}>
          <option value="">All technicians</option><option value="unassigned">Unassigned only</option><option value="assigned">Assigned only</option>{technicians.data?.map(x => <option key={x.id} value={x.id}>{x.displayName}{x.isActive ? '' : ' (inactive)'}</option>)}
          {state.technicianId && !technicians.data?.some(x => x.id === state.technicianId) && <option value={state.technicianId}>Technician from URL: {state.technicianId}</option>}
        </TextField>
        <TextField size="small" select label="SLA status" value={state.slaStatus} onChange={e => change({ slaStatus: e.target.value })} slotProps={selectionProps}><option value="">All SLA states</option>{Object.entries(slaLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</TextField>
      </div>
      {(customers.isError || technicians.isError || options.isError || (state.customerId && locations.isError)) && <Alert severity="warning" sx={{ mt: 2 }} action={<Button onClick={() => { void customers.refetch(); void technicians.refetch(); void options.refetch(); if (state.customerId) void locations.refetch() }}>Retry</Button>}>Some filter options could not be loaded.</Alert>}
      <Collapse in={moreFilters}>
        <Box className="queue-secondary" sx={{ pt: 2.5 }}>
          <TextField size="small" type="datetime-local" label="Created from (inclusive, UTC)" value={utcInput(state.createdFrom)} onChange={e => change({ createdFrom: utcValue(e.target.value) })} slotProps={{ inputLabel: { shrink: true }, htmlInput: { step: 1 } }} />
          <TextField size="small" type="datetime-local" label="Created before (exclusive, UTC)" value={utcInput(state.createdTo)} onChange={e => change({ createdTo: utcValue(e.target.value) })} slotProps={{ inputLabel: { shrink: true }, htmlInput: { step: 1 } }} />
          <FormControlLabel control={<Checkbox checked={state.openOnly} onChange={e => change({ openOnly: e.target.checked ? 'true' : '' })} />} label="Open only" />
        </Box>
      </Collapse>
      {hasFilters && <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1, mt: 2 }} aria-label="Active filters">
        {filterEntries.map(([key, value], index) => <Chip key={`${key}-${index}`} size="small" label={chipLabel(key, value)} onDelete={() => {
          if (key === 'status') {
            const next = changeQueueParams(params, search !== state.search ? { search: search.trim() } : {})
            next.delete('status', value); setParams(next)
          } else change({ [key]: '' })
        }} sx={{ maxWidth: '100%' }} />)}
        <Button size="small" onClick={clear} sx={{ minHeight: 28 }}>Clear filters</Button>
      </Stack>}
    </Paper>
    {orders.isError && <Alert severity="error" sx={{ mb: 2 }} action={failure?.status === 401 ? <Button href="/login">Sign in again</Button> : <Button onClick={() => orders.refetch()}>Retry</Button>}>
      {failure?.status === 400 ? `Check the queue URL. ${invalidFields}` : failure?.status === 401 ? failure.message : 'Work orders could not be loaded. Please retry.'}
      {orders.data && ' Showing previously loaded rows; they may not match the current filters.'}
      {failure?.status === 400 && <Button onClick={clear}>Reset queue</Button>}
    </Alert>}
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <Box sx={{ height: 4 }}>{orders.isFetching && <LinearProgress aria-label={orders.data ? 'Refreshing work orders' : 'Loading work orders'} />}</Box>
      {orders.isPlaceholderData && <Typography role="status" variant="body2" sx={{ px: 2, py: 1 }}>Updating results… Showing previous rows.</Typography>}
      {orders.isError && !orders.data ? <Box className="queue-empty"><Typography component="h2" variant="h2">Queue unavailable</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>Use the action above to continue.</Typography></Box> : zeroRows ? <Box className="queue-empty">
        <Typography component="h2" variant="h2">{orders.data.totalCount > 0 ? 'No work orders on this page' : emptyDatabase ? 'No work orders yet' : 'No matching work orders'}</Typography>
        <Typography color="text.secondary" sx={{ my: 1.5 }}>{emptyDatabase ? 'Create the first service request to get started.' : 'Try a different search or adjust your filters.'}</Typography>
        {orders.data.totalCount > 0 ? <Button onClick={() => change({ page: '1' }, false)}>Go to first page</Button> : emptyDatabase ? <Button component={Link} to="/work-orders/new" variant="contained">Create work order</Button> : <Button variant="outlined" onClick={clear}>Clear filters</Button>}
      </Box> : <Box sx={{ height: 640, width: '100%' }}>
        <DataGrid key={queryString} aria-label="Work orders" rows={orders.data?.items ?? []} columns={columns} rowCount={orders.isPlaceholderData ? -1 : orders.data?.totalCount ?? -1}
          paginationMode="server" sortingMode="server" filterMode="server" disableRowSelectionOnClick disableColumnMenu disableColumnFilter disableColumnSelector disableColumnResize
          loading={orders.isPending} rowHeight={62} columnHeaderHeight={46} pageSizeOptions={[25, 50, 100]}
          paginationModel={paginationModel} onPaginationModelChange={(model, details) => {
            // Ignore grid reconciliation while data is unknown/stale; the URL owns the requested page.
            if (details.reason !== 'setPaginationModel' || !orders.data || orders.isPlaceholderData || orders.data.totalCount === 0) return
            if (model.pageSize !== state.pageSize) change({ pageSize: String(model.pageSize) })
            else if (model.page + 1 !== state.page) change({ page: String(model.page + 1) }, false)
          }}
          sortModel={sortModel}
          onSortModelChange={model => {
            const sort = model[0] ? `${model[0].sort === 'desc' ? '-' : ''}${model[0].field}` : '-createdAt'
            if (sort !== state.sort) change({ sort })
          }}
          sx={{ border: 0, '& .MuiDataGrid-columnHeader': { bgcolor: '#f5f7f5' }, '& .MuiDataGrid-cell': { borderColor: '#edf0ed' }, '& .MuiDataGrid-row:hover': { bgcolor: '#f5f8f6' } }} />
      </Box>}
    </Paper>
    <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>{compact && 'Scroll horizontally for customer and additional columns. '}Times shown in {Intl.DateTimeFormat().resolvedOptions().timeZone}. {orders.data && `SLA evaluated ${dateLabel(orders.data.evaluatedAt)}.`} Refreshes every minute while active.</Typography>
  </div>
}
