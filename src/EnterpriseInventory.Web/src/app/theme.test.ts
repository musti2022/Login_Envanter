import { brandColors, theme } from './theme'

describe('theme', () => {
  it('uses the corporate colours from the design guide', () => {
    expect(theme.palette.primary.main).toBe('#142D4E')
    expect(theme.palette.secondary.main).toBe('#2563EB')
    expect(theme.palette.background.paper).toBe(brandColors.white)
    expect(theme.palette.background.default).toBe(brandColors.lightGray)
  })

  it('uses Turkish component texts', () => {
    expect(theme.components?.MuiTablePagination?.defaultProps?.labelRowsPerPage).toBe('Sayfa başına satır:')
  })
})
