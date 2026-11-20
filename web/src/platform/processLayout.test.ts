import { describe, expect, it } from 'vitest'
import { layoutGraph, roundedPath, routeBetween } from './processLayout'

describe('roundedPath', () => {
  it('is a straight line for two points', () => {
    expect(
      roundedPath([
        { x: 0, y: 0 },
        { x: 40, y: 0 },
      ]),
    ).toBe('M 0 0 L 40 0')
  })

  it('rounds each bend with a curve that stops short of it by the radius', () => {
    const path = roundedPath(
      [
        { x: 0, y: 0 },
        { x: 40, y: 0 },
        { x: 40, y: 40 },
      ],
      8,
    )

    expect(path).toBe('M 0 0 L 32 0 Q 40 0 40 8 L 40 40')
  })

  it('clamps the radius to half the shorter segment', () => {
    const path = roundedPath(
      [
        { x: 0, y: 0 },
        { x: 10, y: 0 },
        { x: 10, y: 40 },
      ],
      8,
    )

    expect(path).toBe('M 0 0 L 5 0 Q 10 0 10 5 L 10 40')
  })

  it('is empty for no points', () => {
    expect(roundedPath([])).toBe('')
  })
})

describe('layoutGraph', () => {
  it('places the nodes from left to right and routes each edge from its source to its target', async () => {
    const { positions, routes } = await layoutGraph({
      nodes: [
        { id: 'a', width: 200, height: 64 },
        { id: 'b', width: 200, height: 64 },
        { id: 'c', width: 200, height: 64 },
      ],
      edges: [
        { id: 'a-b', source: 'a', target: 'b' },
        { id: 'b-c', source: 'b', target: 'c' },
        { id: 'c-a', source: 'c', target: 'a' },
        { id: 'a-missing', source: 'a', target: 'missing' },
      ],
    })

    expect(positions.get('b')!.x).toBeGreaterThan(positions.get('a')!.x)
    expect(positions.get('c')!.x).toBeGreaterThan(positions.get('b')!.x)
    expect(routes.has('a-missing')).toBe(false)
    expect(routes.get('a-b')).toMatchObject({ source: 'a', target: 'b' })
    expect(routes.get('c-a')).toMatchObject({ source: 'c', target: 'a' })
    const forward = routes.get('a-b')!.points
    expect(forward[0]!.x).toBeCloseTo(positions.get('a')!.x + 200)
    expect(forward.at(-1)!.x).toBeCloseTo(positions.get('b')!.x)
    expect(routes.get('c-a')!.points.length).toBeGreaterThan(2)
  })
})

describe('routeBetween', () => {
  it('returns the points only while the ends are those the route was laid out for', async () => {
    const layout = await layoutGraph({
      nodes: [
        { id: 'a', width: 200, height: 64 },
        { id: 'b', width: 200, height: 64 },
      ],
      edges: [{ id: 'a-b', source: 'a', target: 'b' }],
    })

    expect(routeBetween(layout, 'a-b', 'a', 'b')).toBe(layout.routes.get('a-b')!.points)
    expect(routeBetween(layout, 'a-b', 'a', 'c')).toBeUndefined()
    expect(routeBetween(layout, 'a-b', 'c', 'b')).toBeUndefined()
    expect(routeBetween(layout, 'unknown', 'a', 'b')).toBeUndefined()
  })
})
