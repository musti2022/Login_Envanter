import { screen } from '@testing-library/react'
import { renderWithRouter } from '../test/renderWithRouter'

describe('DashboardPage', () => {
  it('shows the four KPI cards with a readable "no data" text instead of numbers', () => {
    renderWithRouter('/')

    for (const label of ['Toplam Demirbaş', 'Zimmetli', 'Boşta', 'Arızalı']) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
    expect(screen.getAllByText('Veri yok')).toHaveLength(4)
  })
})
