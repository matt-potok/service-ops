import { useRef, useState, type FormEvent } from 'react'
import { Alert, Box, Button, CircularProgress, Divider, Paper, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { createWorkOrder, getCreationOptions, getCustomers, getLocations, WorkOrderApiError, type CreateWorkOrderInput } from './api'

export function CreateWorkOrderPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const submitting = useRef(false)
  const [form, setForm] = useState({ customerId: '', locationId: '', serviceType: '', priority: 'Normal', title: '', description: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const customers = useQuery({ queryKey: ['customers'], queryFn: ({ signal }) => getCustomers(signal) })
  const options = useQuery({ queryKey: ['creation-options'], queryFn: ({ signal }) => getCreationOptions(signal) })
  const locations = useQuery({ queryKey: ['locations', form.customerId], queryFn: ({ signal }) => getLocations(form.customerId, signal), enabled: !!form.customerId })
  const create = useMutation({
    mutationFn: createWorkOrder,
    onSuccess: order => {
      queryClient.setQueryData(['work-order', order.id], order)
      navigate(`/work-orders/${order.id}`, { replace: true, state: { created: true } })
    },
    onError: error => { if (error instanceof WorkOrderApiError) setErrors(error.fields) },
  })

  function change(field: keyof typeof form, value: string) {
    setForm(previous => ({ ...previous, [field]: value, ...(field === 'customerId' ? { locationId: '' } : {}) }))
    setErrors(previous => ({ ...previous, [field]: '', ...(field === 'customerId' ? { locationId: '' } : {}) }))
    create.reset()
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting.current) return
    const next: Record<string, string> = {}
    if (!form.customerId) next.customerId = 'Choose a customer.'
    if (!form.locationId) next.locationId = 'Choose a location.'
    if (!form.serviceType) next.serviceType = 'Choose a service type.'
    if (!form.priority) next.priority = 'Choose a priority.'
    if (!form.title.trim()) next.title = 'Enter a short description of the issue.'
    if (!form.description.trim()) next.description = 'Describe the issue and the affected area.'
    setErrors(next)
    if (Object.keys(next).length) return
    submitting.current = true
    try {
      await create.mutateAsync({ ...form, title: form.title.trim(), description: form.description.trim(),
        serviceType: form.serviceType as CreateWorkOrderInput['serviceType'], priority: form.priority as CreateWorkOrderInput['priority'] })
    } catch { /* Mutation error is shown inline; never automatically retry a creation. */ }
    finally { submitting.current = false }
  }

  const selectedPriority = options.data?.priorities.find(priority => priority.code === form.priority)
  const unavailable = customers.isPending || options.isPending || customers.isError || options.isError || !customers.data?.length
  return <>
    <Typography component="h1" variant="h1" sx={{ fontSize: 32 }}>Create work order</Typography>
    <Typography color="text.secondary" sx={{ mt: 1, mb: 4 }}>Record a service issue and its location. Required fields are marked with an asterisk.</Typography>
    {(customers.isPending || options.isPending) && <Stack direction="row" spacing={1} sx={{ mb: 2 }} role="status"><CircularProgress size={20} /><span>Loading form options…</span></Stack>}
    {(customers.isError || options.isError) && <Alert severity="error" sx={{ mb: 3 }} action={<Button onClick={() => { void customers.refetch(); void options.refetch() }}>Retry</Button>}>Customer and service options could not be loaded.</Alert>}
    {customers.isSuccess && !customers.data.length && <Alert severity="info" sx={{ mb: 3 }}>No active customers are available. Contact your operations administrator.</Alert>}
    <Box component="form" noValidate onSubmit={submit} className="work-order-layout" aria-busy={create.isPending}>
      <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
        <Box component="fieldset" disabled={create.isPending} sx={{ border: 0, p: { xs: 2.5, sm: 4 }, m: 0, minWidth: 0 }}>
          <Typography component="h2" variant="h2" sx={{ fontSize: 19 }}>Customer & location</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 3 }}>Identify where the service is needed.</Typography>
          <div className="form-grid">
            <TextField select label="Customer" required fullWidth value={form.customerId} disabled={unavailable}
              onChange={event => change('customerId', event.target.value)} error={!!errors.customerId} helperText={errors.customerId || 'Select the customer account.'}
              slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
              <option value="">Select customer</option>{customers.data?.map(customer => <option key={customer.id} value={customer.id}>{customer.name}</option>)}
            </TextField>
            <TextField select label="Location" required fullWidth value={form.locationId}
              disabled={!form.customerId || locations.isPending || locations.isError || !locations.data?.length}
              onChange={event => change('locationId', event.target.value)} error={!!errors.locationId} helperText={errors.locationId || (!form.customerId ? 'Choose a customer first.' : locations.isFetching ? 'Loading locations…' : 'Locations belonging to this customer.')}
              slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
              <option value="">Select location</option>{locations.data?.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
            </TextField>
          </div>
          {locations.isError && <Alert severity="error" sx={{ mt: 2 }} action={<Button onClick={() => locations.refetch()}>Retry</Button>}>Locations could not be loaded.</Alert>}
          {form.customerId && locations.isSuccess && !locations.data.length && <Alert severity="info" sx={{ mt: 2 }}>This customer has no active locations.</Alert>}
          <Divider sx={{ my: 3.5 }} />
          <Typography component="h2" variant="h2" sx={{ fontSize: 19, mb: 3 }}>Service details</Typography>
          <div className="form-grid">
            <TextField select label="Service type" required fullWidth value={form.serviceType} disabled={options.isPending || options.isError}
              onChange={event => change('serviceType', event.target.value)} error={!!errors.serviceType} helperText={errors.serviceType || 'Choose the type of service required.'}
              slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
              <option value="">Select service type</option>{options.data?.serviceTypes.map(service => <option key={service.code} value={service.code}>{service.label}</option>)}
            </TextField>
            <TextField select label="Priority" required fullWidth value={form.priority} disabled={options.isPending || options.isError}
              onChange={event => change('priority', event.target.value)} error={!!errors.priority} helperText={errors.priority || 'Set the urgency of the issue.'}
              slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
              {!options.data && <option value="Normal">Normal</option>}{options.data?.priorities.map(priority => <option key={priority.code} value={priority.code}>{priority.label}</option>)}
            </TextField>
          </div>
          <TextField label="Title" required fullWidth value={form.title} onChange={event => change('title', event.target.value)}
            error={!!errors.title} helperText={errors.title || 'A short, specific summary of the issue.'} sx={{ mt: 3 }} slotProps={{ htmlInput: { maxLength: 200 } }} />
          <TextField label="Description" required fullWidth multiline minRows={4} value={form.description} onChange={event => change('description', event.target.value)}
            error={!!errors.description} helperText={errors.description || 'Include symptoms, affected areas, and any relevant access details.'} sx={{ mt: 3 }} slotProps={{ htmlInput: { maxLength: 10000 } }} />
        </Box>
        <Divider />
        <Box sx={{ p: { xs: 2.5, sm: 3 }, bgcolor: '#fafbf9' }}>
          {create.isError && <Alert severity="error" sx={{ mb: 2 }}>{create.error.message}</Alert>}
          <Stack direction="row" spacing={2} sx={{ justifyContent: 'flex-end' }}>
            <Button component={Link} to="/" disabled={create.isPending}>Cancel</Button>
            <Button type="submit" variant="contained" disabled={create.isPending || unavailable || locations.isError || !locations.data?.length}>
              {create.isPending ? 'Creating work order…' : 'Create work order'}
            </Button>
          </Stack>
        </Box>
      </Paper>
      <Paper component="aside" variant="outlined" sx={{ p: 3, alignSelf: 'start', bgcolor: '#edf3ef' }}>
        <Typography component="h2" variant="h2" sx={{ fontSize: 18 }}>Resolution target</Typography>
        <Typography sx={{ fontSize: 32, fontWeight: 650, mt: 2 }}>{selectedPriority ? `${selectedPriority.slaDurationMinutes / 60} hours` : '—'}</Typography>
        <Typography variant="body2" color="text.secondary">{form.priority} priority · continuous elapsed time</Typography>
        <Divider sx={{ my: 2.5 }} />
        <Typography variant="body2" color="text.secondary">The SLA clock starts when the work order is created. The confirmed deadline will appear on the work order.</Typography>
      </Paper>
    </Box>
  </>
}
