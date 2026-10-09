import { defaultListParams, toQueryString } from './assetsApi'
import { activeFilterCount, clearedFilters, parseListParams } from './listParams'

describe('inventory list parameters', () => {
  it('leave defaults out of the address and the API request', () => {
    expect(toQueryString(defaultListParams)).toBe('')
  })

  it('survive a round trip through the address', () => {
    const params = {
      ...defaultListParams,
      page: 3,
      pageSize: 50,
      sortBy: 'cityName' as const,
      sortDirection: 'desc' as const,
      search: 'dell izmir',
      status: ['Faulty' as const, 'Retired' as const],
      assetType: ['Laptop' as const],
      brandId: 4,
      modelId: 12,
      cityId: 7,
      locationId: 30,
      departmentId: 2,
      archived: true,
    }

    const query = toQueryString(params)

    expect(query).toBe(
      'search=dell+izmir&status=Faulty&status=Retired&assetType=Laptop&brandId=4&modelId=12&cityId=7&departmentId=2&locationId=30&archived=true&sortBy=cityName&sortDirection=desc&page=3&pageSize=50',
    )
    expect(parseListParams(new URLSearchParams(query))).toEqual(params)
  })

  it('fall back to the defaults for hand-edited values the API would refuse', () => {
    const params = parseListParams(
      new URLSearchParams('page=0&pageSize=7&sortBy=rowVersion&sortDirection=up&status=Kayıp&status=Faulty&assetType=2&brandId=-1&cityId=abc&archived=yes'),
    )

    expect(params).toEqual({ ...defaultListParams, status: ['Faulty'] })
  })

  it('drop repeated values and trim the search sent to the API', () => {
    const params = parseListParams(new URLSearchParams('status=Faulty&status=Faulty&search=%20%20dell%20'))

    expect(params.status).toEqual(['Faulty'])
    expect(toQueryString(params)).toBe('search=dell&status=Faulty')
  })

  it('apply a model only with its brand and a location only with its city', () => {
    const params = parseListParams(new URLSearchParams('modelId=12&locationId=30&departmentId=2'))

    expect(params).toEqual({ ...defaultListParams, departmentId: 2 })
  })

  it('count the filters that are on and clear them without touching sorting or page size', () => {
    const params = parseListParams(
      new URLSearchParams('search=dell&status=Faulty&brandId=4&modelId=12&archived=true&sortBy=cityName&pageSize=50&page=3'),
    )

    expect(activeFilterCount(params)).toBe(5)
    const cleared = { ...params, ...clearedFilters }
    expect(activeFilterCount(cleared)).toBe(0)
    expect(toQueryString(cleared)).toBe('sortBy=cityName&pageSize=50')
  })
})
