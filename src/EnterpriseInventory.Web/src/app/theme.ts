import { createTheme } from '@mui/material/styles'
import { trTR } from '@mui/material/locale'

export const brandColors = {
  navy: '#142D4E',
  blue: '#2563EB',
  white: '#FFFFFF',
  lightGray: '#F3F4F6',
} as const

export const theme = createTheme(
  {
    palette: {
      primary: { main: brandColors.navy },
      secondary: { main: brandColors.blue },
      background: { default: brandColors.lightGray, paper: brandColors.white },
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: ['"Segoe UI"', 'Roboto', '"Helvetica Neue"', 'Arial', 'sans-serif'].join(','),
      button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
      MuiAppBar: {
        defaultProps: { elevation: 0, color: 'inherit' },
      },
      MuiCard: {
        defaultProps: { variant: 'outlined' },
      },
    },
  },
  trTR,
)
