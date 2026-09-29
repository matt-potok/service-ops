import { createTheme } from '@mui/material/styles'

export const theme = createTheme({
  palette: {
    primary: { main: '#205d56', dark: '#16463f' },
    background: { default: '#f4f6f4', paper: '#ffffff' },
    text: { primary: '#1b302e', secondary: '#566763' },
  },
  typography: {
    fontFamily: '"Segoe UI", Arial, sans-serif',
    h1: { fontSize: '2.5rem', fontWeight: 650, letterSpacing: '-0.045em' },
    h2: { fontSize: '1.5rem', fontWeight: 650, letterSpacing: '-0.025em' },
    button: { textTransform: 'none', fontWeight: 600 },
  },
  shape: { borderRadius: 10 },
  components: {
    MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { minHeight: 44 } } },
    MuiOutlinedInput: { styleOverrides: { root: { backgroundColor: '#fff' } } },
  },
})
