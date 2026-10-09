import { defaultListParams, toQueryString } from './assetsApi'
import { parseListParams } from './listParams'

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
      cityId: 7,
      archived: true,
    }

    const query = toQueryString(params)

    expect(query).toBe(
      'search=dell+izmir&status=Faulty&status=Retired&assetType=Laptop&brandId=4&cityId=7&archived=true&sortBy=cityName&sortDirection=desc&page=3&pageSize=50',
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
})
