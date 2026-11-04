/** Where a pointer of an entity shows: the Validations tab, the Indexes tab, or the Fields tab, keyed `form`, for every other. */
export function entityTabOf(path: string): 'validations' | 'indexes' | 'form' {
  return path.startsWith('/validations') ? 'validations' : path.startsWith('/indexes') ? 'indexes' : 'form'
}
