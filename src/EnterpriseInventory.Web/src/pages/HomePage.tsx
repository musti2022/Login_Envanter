import { Box, Container, Paper, Typography } from '@mui/material'

export function HomePage() {
  return (
    <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', bgcolor: 'background.default' }}>
      <Container maxWidth="sm">
        <Paper elevation={2} sx={{ p: 4, borderTop: 4, borderColor: 'secondary.main' }}>
          <Typography variant="h4" component="h1" color="primary" gutterBottom>
            Kurumsal Envanter Yönetim Sistemi
          </Typography>
          <Typography color="text.secondary">
            Uygulama altyapısı hazırlanıyor. Giriş ekranı ve envanter modülleri sonraki aşamalarda eklenecek.
          </Typography>
        </Paper>
      </Container>
    </Box>
  )
}
