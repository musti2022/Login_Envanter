import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { renderWithRouter } from '../test/renderWithRouter'
import { mockViewport } from '../test/viewport'
import { navigationItems } from './navigation'

function mainMenus() {
  return screen.queryAllByRole('navigation', { name: 'Ana menü' })
}

describe('AppLayout', () => {
  it('shows the Turkish header and every main menu item', () => {
    renderWithRouter('/')

    expect(screen.getByText('Kurumsal Envanter Yönetim Sistemi')).toBeInTheDocument()

    const [menu] = mainMenus()
    const labels = within(menu).getAllByRole('link').map((link) => link.textContent)
    expect(labels).toEqual(navigationItems.map((item) => item.label))
  })

  it('opens the dashboard on the root path and marks it as the current page', () => {
    renderWithRouter('/')

    expect(screen.getByRole('heading', { level: 1, name: 'Gösterge Paneli' })).toBeInTheDocument()
    const [menu] = mainMenus()
    expect(within(menu).getByRole('link', { name: 'Gösterge Paneli' })).toHaveAttribute('aria-current', 'page')
  })

  it.each(navigationItems.map((item) => [item.path, item.label]))('renders %s with the heading "%s"', async (path, label) => {
    renderWithRouter(path)

    expect(screen.getByRole('heading', { level: 1, name: label })).toBeInTheDocument()
    await waitFor(() => expect(document.title).toBe(`${label} | Kurumsal Envanter`))
  })

  it('moves the current page marker when a menu item is clicked', () => {
    const { router } = renderWithRouter('/')
    const [menu] = mainMenus()

    fireEvent.click(within(menu).getByRole('link', { name: 'Envanter' }))

    expect(router.state.location.pathname).toBe('/envanter')
    expect(screen.getByRole('heading', { level: 1, name: 'Envanter' })).toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'Envanter' })).toHaveAttribute('aria-current', 'page')
    expect(within(menu).getByRole('link', { name: 'Gösterge Paneli' })).not.toHaveAttribute('aria-current')
  })

  it('opens the mobile menu as a named dialog and closes it after navigating', async () => {
    const { router } = renderWithRouter('/')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Menüyü aç' }))
    const mobileDrawer = await screen.findByRole('dialog', { name: 'Menü' })

    fireEvent.click(within(mobileDrawer).getByRole('link', { name: 'Zimmetler' }))

    expect(router.state.location.pathname).toBe('/zimmetler')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('closes the mobile menu when the screen grows to desktop width', async () => {
    const viewport = mockViewport(false)
    try {
      renderWithRouter('/')
      fireEvent.click(screen.getByRole('button', { name: 'Menüyü aç' }))
      await screen.findByRole('dialog', { name: 'Menü' })

      viewport.setDesktop(true)

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(document.body.style.overflow).not.toBe('hidden')
    } finally {
      viewport.restore()
    }
  })

  it('does not expose an empty complementary landmark', () => {
    renderWithRouter('/')

    expect(screen.queryByRole('complementary')).not.toBeInTheDocument()
  })

  it('shows a Turkish not-found page for unknown addresses', () => {
    renderWithRouter('/olmayan-sayfa')

    expect(screen.getByRole('heading', { level: 1, name: 'Sayfa bulunamadı' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Gösterge Paneline dön' })).toHaveAttribute('href', '/')
  })
})
