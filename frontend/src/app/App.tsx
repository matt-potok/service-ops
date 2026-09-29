import { Alert, Avatar, Box, Button, Chip, CircularProgress, Divider, Paper, Stack, Typography } from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, Navigate, Route, Routes } from 'react-router'
import { getSession, signOut, type Session } from '../lib/api/client'
import { LoginPage } from '../features/auth/LoginPage'
import { Brand } from './Brand'

export function App() {
  const session = useQuery({ queryKey: ['session'], queryFn: getSession })
  if (session.isPending) return <main className="session-state" aria-label="Loading session"><CircularProgress /><p>Loading your workspace…</p></main>
  if (session.isError) return <main className="session-state"><Alert severity="error">{session.error.message}</Alert><Button onClick={() => session.refetch()}>Try again</Button></main>

  return <Routes>
    <Route path="/login" element={session.data ? <Navigate to="/" replace /> : <LoginPage />} />
    <Route path="/" element={session.data ? <Home session={session.data} /> : <Navigate to="/login" replace />} />
    <Route path="*" element={<main className="session-state"><Typography variant="h2">Page not found</Typography><Button component={Link} to="/">Return home</Button></main>} />
  </Routes>
}

function Home({ session }: { session: Session }) {
  const queryClient = useQueryClient()
  const logout = useMutation({ mutationFn: signOut, onSuccess: () => queryClient.setQueryData(['session'], null) })
  const name = session.displayName ?? 'Atlas team member'
  const initials = name.split(' ').map(part => part[0]).slice(0, 2).join('')
  return <div className="workspace">
    <a className="skip-link" href="#main">Skip to main content</a>
    <header className="workspace-header">
      <Brand />
      <span className="product-name">ServiceOps</span>
      <Button variant="outlined" onClick={() => logout.mutate()} disabled={logout.isPending}>
        {logout.isPending ? 'Signing out…' : 'Sign out'}
      </Button>
    </header>
    <main id="main" className="home-content">
      <span className="eyebrow">YOUR WORKSPACE</span>
      <Typography component="h1" variant="h1" sx={{ mt: 1, mb: 1.5 }}>Welcome, {name.split(' ')[0]}.</Typography>
      <Typography color="text.secondary">You’re signed in to Atlas Facility Services.</Typography>
      {logout.isError && <Alert severity="error" sx={{ mt: 3 }}>{logout.error.message}</Alert>}
      <Paper variant="outlined" sx={{ mt: 5, maxWidth: 680, overflow: 'hidden' }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', p: { xs: 2.5, sm: 4 } }}>
          <Avatar sx={{ bgcolor: '#e1ece6', color: 'primary.main', width: 56, height: 56 }}>{initials}</Avatar>
          <Box><Typography component="h2" variant="h2">{name}</Typography><Typography color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{session.email}</Typography></Box>
        </Stack>
        <Divider />
        <Box sx={{ p: { xs: 2.5, sm: 4 }, display: 'grid', gap: 2 }}>
          <Typography variant="overline" color="text.secondary">ACCOUNT ACCESS</Typography>
          <Stack direction="row" spacing={1}>{session.roles?.map(role => <Chip key={role} label={role} variant="outlined" color="primary" />)}</Stack>
          <Typography variant="body2" color="text.secondary">Your access is assigned by Atlas Facility Services.</Typography>
        </Box>
      </Paper>
    </main>
    <footer className="workspace-footer">Atlas Facility Services <span>ServiceOps · Internal workspace</span></footer>
  </div>
}
