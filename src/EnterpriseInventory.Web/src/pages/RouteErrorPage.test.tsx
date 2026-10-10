import { screen } from '@testing-library/react'
import { renderWithRouter } from '../test/renderWithRouter'
import { RouteErrorPage } from './RouteErrorPage'

function Broken(): never {
  throw new Error('secret stack detail')
}

describe('RouteErrorPage', () => {
  it('shows a Turkish error without leaking technical details', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    renderWithRouter('/', { routes: [{ path: '/', element: <Broken />, errorElement: <RouteErrorPage /> }] })

    expect(screen.getByRole('alert')).toHaveTextContent('Sayfa görüntülenemedi')
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeInTheDocument()
    expect(screen.queryByText(/secret stack detail/)).not.toBeInTheDocument()

    consoleError.mockRestore()
  })
})
