import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { StudioRevisionChange } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioChanges } from './StudioChanges'

const texts = {
  'shell.studio.history.before': 'Before',
  'shell.studio.history.after': 'After',
  'shell.studio.overview.change.added': 'Added',
  'shell.studio.overview.change.changed': 'Changed',
  'shell.studio.overview.change.removed': 'Removed',
  'shell.studio.nav.kindOne.role': 'Role',
}
const catalogs = [{ texts, fallbackTexts: texts }]

const files: StudioRevisionChange[] = [
  {
    file: 'roles/approver.json',
    change: 'added',
    id: null,
    kind: 'role',
    name: 'Approver',
    before: null,
    after: '{"name":"Approver"}',
  },
  {
    file: 'roles/buyer.json',
    change: 'changed',
    id: null,
    kind: 'role',
    name: 'Buyer',
    before: '{"label":"first"}',
    after: '{"label":"second"}',
  },
  {
    file: 'notes/old.json',
    change: 'removed',
    id: null,
    kind: null,
    name: 'Old',
    before: '{"gone":true}',
    after: null,
  },
]

function renderChanges() {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <StudioChanges files={files} resources={[]} />
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

function fileSection(file: string): HTMLElement {
  const section = screen.getAllByTestId('studio-changes-file').find((candidate) => candidate.dataset.file === file)
  if (!section) {
    throw new Error(`No section for ${file}`)
  }
  return section
}

describe('StudioChanges', () => {
  it('shows each file with its name, kind, change and its JSON before and after', () => {
    renderChanges()

    expect(screen.getAllByTestId('studio-changes-file')).toHaveLength(3)

    const added = within(fileSection('roles/approver.json'))
    expect(added.getByText('Approver')).toBeVisible()
    expect(added.getByText('Role')).toBeVisible()
    expect(added.getByText('Added')).toBeVisible()
    expect(added.getByTestId('json-diff-before')).toHaveTextContent('—')
    expect(added.getByTestId('json-diff-after')).toHaveTextContent('"name": "Approver"')

    const changed = within(fileSection('roles/buyer.json'))
    expect(changed.getByText('Buyer')).toBeVisible()
    expect(changed.getByText('Role')).toBeVisible()
    expect(changed.getByText('Changed')).toBeVisible()
    expect(changed.getByTestId('json-diff-before')).toHaveTextContent('"label": "first"')
    expect(changed.getByTestId('json-diff-after')).toHaveTextContent('"label": "second"')

    const removed = within(fileSection('notes/old.json'))
    expect(removed.getByText('Removed')).toBeVisible()
    expect(removed.getByTestId('json-diff-before')).toHaveTextContent('"gone": true')
    expect(removed.getByTestId('json-diff-after')).toHaveTextContent('—')
  })

  it('shows the em dash as the kind of a file that has none', () => {
    renderChanges()

    const removed = fileSection('notes/old.json')
    expect(removed).toHaveTextContent(/^.*—.*Removed/)
    expect(within(removed).getByText('Before')).toBeVisible()
    expect(within(removed).getByText('After')).toBeVisible()
  })
})
