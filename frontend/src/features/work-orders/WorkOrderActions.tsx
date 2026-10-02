import { statusLabels } from './statusLabels'
import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { assignTechnician, correctDetails, getTechnicians, getWorkOrder, transitionWorkOrder, WorkOrderApiError, type WorkOrderDetail } from './api'

type Action = 'edit' | 'assign' | 'unassign' | 'start' | 'hold' | 'resume' | 'complete' | 'cancel'
const titles: Record<Action, string> = { edit: 'Edit details', assign: 'Assign technician', unassign: 'Unassign technician', start: 'Start work', hold: 'Place on hold', resume: 'Resume work', complete: 'Complete work order', cancel: 'Cancel work order' }

export function WorkOrderActions({ detail }: { detail: WorkOrderDetail }) {
  const [action, setAction] = useState<Action | null>(null)
  const [draft, setDraft] = useState({ title: '', description: '', technicianId: '', text: '', revision: 0 })
  const [feedback, setFeedback] = useState('')
  const [conflictReady, setConflictReady] = useState(false)
  const queryClient = useQueryClient()
  const technicians = useQuery({ queryKey: ['technicians'], queryFn: ({ signal }) => getTechnicians(signal), enabled: action === 'assign' })
  const mutation = useMutation({
    mutationFn: async () => {
      const expectedRevision = draft.revision
      if (action === 'edit') return correctDetails(detail.id, { title: draft.title, description: draft.description, expectedRevision })
      if (action === 'assign' || action === 'unassign') return assignTechnician(detail.id, { technicianId: action === 'unassign' ? null : draft.technicianId, expectedRevision })
      return transitionWorkOrder(detail.id, { expectedRevision,
        targetStatus: action === 'hold' ? 'OnHold' : action === 'complete' ? 'Completed' : action === 'cancel' ? 'Cancelled' : 'InProgress',
        ...(action === 'complete' ? { summary: draft.text } : action === 'hold' || action === 'cancel' ? { reason: draft.text } : {}) })
    },
    onSuccess: async result => {
      queryClient.setQueryData(['work-order', detail.id], result)
      setAction(null)
      setFeedback('Work order updated.')
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['work-order', detail.id] }),
        queryClient.invalidateQueries({ queryKey: ['work-orders'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
        queryClient.invalidateQueries({ queryKey: ['work-order-activity', detail.id] }),
      ])
    },
    onError: async error => {
      if (error instanceof WorkOrderApiError && error.errorCode === 'stale_revision') {
        setConflictReady(false)
        try {
          const latest = await getWorkOrder(detail.id)
          queryClient.setQueryData(['work-order', detail.id], latest)
          setConflictReady(true)
        } catch { /* Keep the draft and require an explicit reload before retry. */ }
        void queryClient.invalidateQueries({ queryKey: ['work-orders'] })
        void queryClient.invalidateQueries({ queryKey: ['work-order-activity', detail.id] })
      }
    },
  })
  const open = (next: Action) => {
    mutation.reset(); setFeedback(''); setConflictReady(false)
    setDraft({ title: detail.title, description: detail.description, technicianId: detail.technicianId ?? '', text: '', revision: detail.revision })
    setAction(next)
  }
  const terminal = detail.status === 'Completed' || detail.status === 'Cancelled'
  const stale = mutation.error instanceof WorkOrderApiError && mutation.error.errorCode === 'stale_revision'
  const fields = mutation.error instanceof WorkOrderApiError ? mutation.error.fields : {}
  const reasonLabel = action === 'complete' ? 'Completion summary' : action === 'cancel' ? 'Cancellation reason' : 'Hold reason'
  const close = () => { if (!mutation.isPending) setAction(null) }
  return <>
    {feedback && <Alert severity="success" sx={{ mb: 2 }}>{feedback}</Alert>}
    {!terminal && <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1, mb: 3 }} aria-label="Work order actions">
      {detail.status === 'Assigned' && <Button variant="contained" onClick={() => open('start')}>Start work</Button>}
      {detail.status === 'InProgress' && <Button variant="outlined" onClick={() => open('hold')}>Place on hold</Button>}
      {detail.status === 'OnHold' && <Button variant="contained" onClick={() => open('resume')}>Resume work</Button>}
      {(detail.status === 'InProgress' || detail.status === 'OnHold') && <Button variant="contained" onClick={() => open('complete')}>Complete work order</Button>}
      <Button variant={detail.status === 'New' ? 'contained' : 'outlined'} onClick={() => open('assign')}>{detail.technicianId ? 'Change technician' : 'Assign technician'}</Button>
      {detail.status === 'Assigned' && <Button onClick={() => open('unassign')}>Unassign technician</Button>}
      <Button variant="outlined" onClick={() => open('edit')}>Edit details</Button>
      <Button color="error" onClick={() => open('cancel')}>Cancel work order</Button>
    </Stack>}
    <Dialog open={action !== null} onClose={close} fullWidth maxWidth="sm" aria-labelledby="work-order-action-title">
      <form onSubmit={event => { event.preventDefault(); if (!mutation.isPending && !stale && !terminal) mutation.mutate() }}>
        <DialogTitle id="work-order-action-title">{action && titles[action]}</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>{detail.number} · {statusLabels[detail.status]} · {detail.technicianName ?? 'Unassigned'}</Typography>
          {mutation.isError && <Alert severity="error" sx={{ mb: 2 }}>{mutation.error.message}{Object.entries(fields).filter(([key]) => !['title', 'description', 'technicianId', 'reason', 'summary'].includes(key)).map(([key, value]) => <div key={key}>{value}</div>)}</Alert>}
          {stale && <Alert severity="warning" sx={{ mb: 2 }}>
            Your draft is retained. {conflictReady ? 'Latest work-order information is loaded. Review it before retrying.' : 'The latest information could not be loaded.'}
            {action === 'edit' && conflictReady && <><Typography sx={{ mt: 1, overflowWrap: 'anywhere' }}>Current title: {detail.title}</Typography><Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>Current description: {detail.description}</Typography></>}
            <Button onClick={async () => {
              try { const latest = await getWorkOrder(detail.id); queryClient.setQueryData(['work-order', detail.id], latest); setConflictReady(true) } catch { setConflictReady(false) }
            }}>Reload current information</Button>
            {conflictReady && !terminal && <Button onClick={() => { setDraft(value => ({ ...value, revision: detail.revision })); mutation.reset() }}>I reviewed the changes — keep my draft</Button>}
          </Alert>}
          {terminal && <Alert severity="info" sx={{ mb: 2 }}>This work order is now {detail.status.toLowerCase()} and cannot be changed. Your draft remains below for reference.</Alert>}
          <Stack spacing={2} sx={{ pt: 1 }}>
            {action === 'edit' && <>
              <TextField autoFocus required label="Title" value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })} error={!!fields.title} helperText={fields.title} slotProps={{ htmlInput: { maxLength: 200 } }} />
              <TextField required multiline minRows={5} label="Description" value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })} error={!!fields.description} helperText={fields.description} slotProps={{ htmlInput: { maxLength: 10000 } }} />
            </>}
            {action === 'assign' && <>
              {technicians.isError && <Alert severity="error" action={<Button onClick={() => technicians.refetch()}>Retry</Button>}>Technicians could not be loaded.</Alert>}
              <TextField autoFocus select required label="Technician" value={draft.technicianId} onChange={e => setDraft({ ...draft, technicianId: e.target.value })} error={!!fields.technicianId} helperText={fields.technicianId ?? 'Choose an active technician.'} slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
                <option value="">{technicians.isPending ? 'Loading technicians…' : 'Select technician'}</option>
                {technicians.data?.filter(x => x.isActive).map(x => <option key={x.id} value={x.id}>{x.displayName}</option>)}
                {draft.technicianId && !technicians.data?.some(x => x.id === draft.technicianId && x.isActive) && <option value={draft.technicianId} disabled>Current technician unavailable — choose another</option>}
              </TextField>
            </>}
            {(action === 'hold' || action === 'complete' || action === 'cancel') && <TextField autoFocus required multiline minRows={4} label={reasonLabel} value={draft.text} onChange={e => setDraft({ ...draft, text: e.target.value })} error={!!(fields.reason || fields.summary)} helperText={fields.reason || fields.summary} slotProps={{ htmlInput: { maxLength: 5000 } }} />}
            {(action === 'complete' || action === 'cancel') && <Typography color="text.secondary">This is a final action. The work order cannot be reopened or edited afterward.</Typography>}
            {action === 'unassign' && <Typography>Remove {detail.technicianName} and return this work order to New?</Typography>}
            {action === 'start' && <Typography>Start work with {detail.technicianName} assigned?</Typography>}
            {action === 'resume' && <Typography>Resume work and clear the current hold reason?</Typography>}
          </Stack>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}><Button disabled={mutation.isPending} onClick={close}>Close</Button>
          <Button type="submit" variant="contained" color={action === 'cancel' ? 'error' : 'primary'} disabled={mutation.isPending || stale || terminal || (action === 'assign' && !technicians.data?.some(x => x.id === draft.technicianId && x.isActive))}>
            {mutation.isPending ? 'Saving…' : action === 'edit' ? 'Save details' : action === 'cancel' ? 'Confirm cancellation' : action === 'complete' ? 'Confirm completion' : action && titles[action]}
          </Button></DialogActions>
      </form>
    </Dialog>
  </>
}
