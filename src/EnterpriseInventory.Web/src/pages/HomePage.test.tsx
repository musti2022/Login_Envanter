import { render, screen } from '@testing-library/react'
import { AppProviders } from '../app/AppProviders'
import { HomePage } from './HomePage'

describe('HomePage', () => {
  it('renders the Turkish application title', () => {
    render(
      <AppProviders>
        <HomePage />
      </AppProviders>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Kurumsal Envanter Yönetim Sistemi' })).toBeInTheDocument()
  })
})
