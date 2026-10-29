import { afterEach, describe, expect, it } from 'vitest'
import { setHostSite, siteAddress } from './siteLinks'

describe('siteAddress', () => {
  afterEach(() => setHostSite(null))

  it('keeps the site path when the host belongs to no site', () => {
    expect(siteAddress('shop')).toBe('/shop')
    expect(siteAddress('shop', 'orders')).toBe('/shop/orders')
    expect(siteAddress('shop', '')).toBe('/shop/')
  })

  it('drops the site path on the host of that site, ignoring letter case', () => {
    setHostSite('Shop')
    expect(siteAddress('shop')).toBe('/')
    expect(siteAddress('SHOP', 'orders')).toBe('/orders')
    expect(siteAddress('shop', '')).toBe('/')
  })

  it('keeps the site path of another site', () => {
    setHostSite('shop')
    expect(siteAddress('support')).toBe('/support')
    expect(siteAddress('support', 'tickets')).toBe('/support/tickets')
  })
})
