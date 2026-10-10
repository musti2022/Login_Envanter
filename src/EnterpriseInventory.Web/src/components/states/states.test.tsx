import { fireEvent, render, screen } from '@testing-library/react'
import { AppProviders } from '../../app/AppProviders'
import { EmptyState } from './EmptyState'
import { ErrorState } from './ErrorState'
import { LoadingState } from './LoadingState'

function renderInProviders(ui: React.ReactNode) {
  return render(<AppProviders>{ui}</AppProviders>)
}

describe('state components', () => {
  it('LoadingState announces a Turkish loading message', () => {
    renderInProviders(<LoadingState />)

    expect(screen.getByRole('status')).toHaveTextContent('Yükleniyor...')
  })

  it('EmptyState shows its title and description', () => {
    renderInProviders(<EmptyState title="Kayıt yok" description="Henüz eklenmiş kayıt bulunmuyor." />)

    expect(screen.getByText('Kayıt yok')).toBeInTheDocument()
    expect(screen.getByText('Henüz eklenmiş kayıt bulunmuyor.')).toBeInTheDocument()
  })

  it('ErrorState shows a Turkish default message and calls onRetry', () => {
    const onRetry = vi.fn()
    renderInProviders(<ErrorState onRetry={onRetry} />)

    expect(screen.getByRole('alert')).toHaveTextContent('Bir hata oluştu')
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('ErrorState hides the retry button when no handler is given', () => {
    renderInProviders(<ErrorState />)

    expect(screen.queryByRole('button', { name: 'Tekrar dene' })).not.toBeInTheDocument()
  })
})
