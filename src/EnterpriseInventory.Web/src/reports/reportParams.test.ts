import { defaultListParams } from '../inventory/assetsApi'
import { monthPeriod, parseMovementParams, parseSummaryParams } from './reportParams'
import { inventoryLink, movementQueryString, summaryQueryString } from './reportsApi'

describe('report addresses', () => {
  it('reads the summary from the address, by city when the grouping is unknown, never with the archive', () => {
    const params = parseSummaryParams(new URLSearchParams('groupBy=model&status=Faulty&brandId=3&archived=true&page=4'))
    expect(params.groupBy).toBe('model')
    expect(params.filters).toMatchObject({ status: ['Faulty'], brandId: 3, archived: false })
    expect(parseSummaryParams(new URLSearchParams('groupBy=sehir')).groupBy).toBe('city')
    // Paging, sorting and the archive are not the summary's: they stay out of its address.
    expect(summaryQueryString(params)).toBe('status=Faulty&brandId=3&groupBy=model')
    expect(summaryQueryString(parseSummaryParams(new URLSearchParams('')))).toBe('')
  })

  it("opens a row's assets with the report's filters, the row's own filter in place of the same one", () => {
    const filters = {
      ...defaultListParams,
      search: 'pc',
      assetType: ['Laptop', 'Desktop'] as const,
      status: ['Assigned'] as const,
    }
    const report = { ...filters, assetType: [...filters.assetType], status: [...filters.status] }

    expect(inventoryLink(report, { assetType: 'Laptop' })).toBe('/envanter?search=pc&status=Assigned&assetType=Laptop')
    expect(inventoryLink(report, { brandId: '3', modelId: '31' })).toBe(
      '/envanter?search=pc&status=Assigned&assetType=Laptop&assetType=Desktop&brandId=3&modelId=31',
    )
    expect(inventoryLink({ ...defaultListParams, page: 3, sortBy: 'cityName' }, { cityId: '6', locationId: '60' })).toBe(
      '/envanter?cityId=6&locationId=60',
    )
  })

  it('reads the movement report from the address, dropping what the API would refuse', () => {
    const params = parseMovementParams(
      new URLSearchParams(
        'from=2026-10-01&to=2026-09-01&movement=Lost&assetType=Laptop&assetType=Kalem&cityId=0&page=2&pageSize=7',
      ),
    )
    expect(params).toMatchObject({
      from: '2026-10-01',
      to: '',
      movement: null,
      assetType: ['Laptop'],
      cityId: null,
      page: 2,
      pageSize: 25,
    })
    expect(parseMovementParams(new URLSearchParams('from=2026-02-30')).from).toBe('')
    expect(movementQueryString({ ...params, movement: 'Returned', search: ' ayse ' })).toBe(
      'from=2026-10-01&movement=Returned&search=ayse&assetType=Laptop&page=2',
    )
    expect(movementQueryString({ ...params, movement: 'Returned' }, { paging: false })).toBe(
      'from=2026-10-01&movement=Returned&assetType=Laptop',
    )
  })

  it('gives this month and last month as whole calendar months', () => {
    const today = new Date(2026, 0, 15, 9, 30)
    expect(monthPeriod(today, 0)).toEqual({ from: '2026-01-01', to: '2026-01-31' })
    expect(monthPeriod(today, 1)).toEqual({ from: '2025-12-01', to: '2025-12-31' })
    expect(monthPeriod(new Date(2028, 2, 1), 1)).toEqual({ from: '2028-02-01', to: '2028-02-29' })
  })
})
