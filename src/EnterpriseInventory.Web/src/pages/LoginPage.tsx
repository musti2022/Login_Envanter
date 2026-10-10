import { zodResolver } from '@hookform/resolvers/zod'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'
import Visibility from '@mui/icons-material/Visibility'
import VisibilityOff from '@mui/icons-material/VisibilityOff'
import {
  Alert,
  AlertTitle,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  IconButton,
  InputAdornment,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, type Location } from 'react-router'
import { z } from 'zod'
import { ApiError } from '../api/http'
import { describeSignInError, type SignInError } from '../auth/signInErrors'
import { useCurrentUser, useSessionEnded, useSignIn } from '../auth/useAuth'
import { brandColors } from '../app/theme'

const maxLength = 256

const signInSchema = z.object({
  userName: z
    .string()
    .trim()
    .min(1, 'Kullanıcı adı zorunludur.')
    .max(maxLength, `Kullanıcı adı en fazla ${maxLength} karakter olabilir.`),
  password: z
    .string()
    .refine((value) => value.trim().length > 0, 'Parola zorunludur.')
    .refine((value) => value.length <= maxLength, `Parola en fazla ${maxLength} karakter olabilir.`),
})

type SignInForm = z.infer<typeof signInSchema>

export function LoginPage() {
  const location = useLocation()
  const currentUser = useCurrentUser()
  const sessionEnded = useSessionEnded()
  const signIn = useSignIn()
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState<SignInError | null>(null)

  const {
    register,
    handleSubmit,
    setError: setFieldError,
    resetField,
    setFocus,
    formState: { errors },
  } = useForm<SignInForm>({ resolver: zodResolver(signInSchema), defaultValues: { userName: '', password: '' } })

  const from = (location.state as { from?: Location } | null)?.from
  const destination = from && from.pathname !== location.pathname ? `${from.pathname}${from.search}${from.hash}` : '/'

  if (currentUser.data) {
    return <Navigate to={destination} replace />
  }

  const onSubmit = handleSubmit(async (values) => {
    setError(null)
    try {
      await signIn.mutateAsync(values)
    } catch (failure) {
      const fieldErrors = failure instanceof ApiError && failure.status === 400 ? failure.problem?.errors : undefined
      if (fieldErrors) {
        for (const field of ['userName', 'password'] as const) {
          const message = fieldErrors[field]?.[0]
          if (message) {
            setFieldError(field, { message })
          }
        }
      } else {
        setError(describeSignInError(failure))
      }

      // Never leave a rejected password in the form.
      resetField('password')
      setFocus('password')
    }
  })

  return (
    <Box
      component="main"
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        p: 2,
        bgcolor: brandColors.navy,
      }}
    >
      <title>Giriş | Kurumsal Envanter</title>
      <Card sx={{ width: '100%', maxWidth: 420 }} variant="elevation" elevation={8}>
        <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', mb: 3, textAlign: 'center' }}>
            <Avatar sx={{ bgcolor: 'secondary.main', mb: 1.5 }}>
              <LockOutlinedIcon aria-hidden="true" />
            </Avatar>
            <Typography variant="h5" component="h1" color="primary" sx={{ fontWeight: 700 }}>
              Kurumsal Envanter Yönetim Sistemi
            </Typography>
            <Typography color="textSecondary" sx={{ mt: 0.5 }}>
              Kurum (Active Directory) hesabınızla giriş yapın.
            </Typography>
          </Box>

          {sessionEnded && !error && (
            <Alert severity="info" sx={{ mb: 2 }}>
              Oturumunuz sona erdi. Devam etmek için tekrar giriş yapın.
            </Alert>
          )}

          {error && (
            <Alert severity="error" sx={{ mb: 2 }} role="alert">
              <AlertTitle sx={{ mb: error.detail ? 0.5 : 0 }}>{error.title}</AlertTitle>
              {error.detail}
            </Alert>
          )}

          <Box component="form" noValidate onSubmit={onSubmit} aria-label="Giriş formu">
            <TextField
              {...register('userName')}
              label="Kullanıcı adı"
              autoComplete="username"
              autoFocus
              fullWidth
              margin="normal"
              error={Boolean(errors.userName)}
              helperText={errors.userName?.message ?? 'Örnek: ad.soyad'}
              slotProps={{ htmlInput: { maxLength, autoCapitalize: 'none', spellCheck: false } }}
            />
            <TextField
              {...register('password')}
              label="Parola"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              fullWidth
              margin="normal"
              error={Boolean(errors.password)}
              helperText={errors.password?.message}
              slotProps={{
                htmlInput: { maxLength },
                input: {
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton
                        aria-label={showPassword ? 'Parolayı gizle' : 'Parolayı göster'}
                        onClick={() => setShowPassword((shown) => !shown)}
                        edge="end"
                      >
                        {showPassword ? <VisibilityOff /> : <Visibility />}
                      </IconButton>
                    </InputAdornment>
                  ),
                },
              }}
            />
            <Button
              type="submit"
              variant="contained"
              color="secondary"
              size="large"
              fullWidth
              disabled={signIn.isPending}
              startIcon={signIn.isPending ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
              sx={{ mt: 3 }}
            >
              {signIn.isPending ? 'Giriş yapılıyor...' : 'Giriş Yap'}
            </Button>
          </Box>
        </CardContent>
      </Card>
    </Box>
  )
}
