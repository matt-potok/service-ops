import { useState, type FormEvent } from 'react'
import { Alert, Box, Button, TextField, Typography } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { signIn } from '../../lib/api/client'
import { Brand } from '../../app/Brand'

export function LoginPage() {
  const queryClient = useQueryClient()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const login = useMutation({
    mutationFn: signIn,
    onSuccess: async () => {
      setPassword('')
      await queryClient.invalidateQueries({ queryKey: ['session'] })
    },
  })

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    login.mutate({ email: email.trim(), password })
  }

  return <main className="login-layout">
    <section className="login-story" aria-label="Atlas Facility Services">
      <Brand />
      <div className="story-copy">
        <span className="eyebrow">SERVICEOPS</span>
        <h1>Great service.<br />Solid foundations.</h1>
        <p>The internal workspace for the people<br className="desktop-break" /> behind Atlas Facility Services.</p>
      </div>
      <div className="architectural-lines" aria-hidden="true"><i /><i /><i /><i /></div>
      <span className="story-footer">ATLAS FACILITY SERVICES <span>INTERNAL ACCESS</span></span>
    </section>
    <section className="login-panel" aria-labelledby="login-title">
      <Box className="login-form">
        <span className="eyebrow">WELCOME TO SERVICEOPS</span>
        <Typography id="login-title" component="h2" variant="h1" sx={{ mt: 1.5 }}>Sign in to your workspace</Typography>
        <Typography color="text.secondary" sx={{ mt: 2, mb: 4 }}>Use your Atlas account to continue.</Typography>
        <Box component="form" onSubmit={submit} sx={{ display: 'grid', gap: 2.5 }}>
          {login.isError && <Alert severity="error">{login.error.message}</Alert>}
          <TextField label="Email address" type="email" name="email" autoComplete="username" required fullWidth
            value={email} onChange={event => setEmail(event.target.value)} slotProps={{ htmlInput: { maxLength: 254 } }} />
          <TextField label="Password" type="password" name="password" autoComplete="current-password" required fullWidth
            value={password} onChange={event => setPassword(event.target.value)} slotProps={{ htmlInput: { maxLength: 256 } }} />
          <Button type="submit" variant="contained" size="large" disabled={login.isPending} sx={{ mt: 0.5 }}>
            {login.isPending ? 'Signing in…' : 'Sign in'}
          </Button>
        </Box>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 3.5 }}>
          Access is limited to authorized Atlas team members.
        </Typography>
      </Box>
      <span className="login-footer">ServiceOps <span>Atlas Facility Services</span></span>
    </section>
  </main>
}
