import { describe, expect, it } from 'vitest'
import { labelKey } from './labelKeys'

describe('labelKey', () => {
  it('builds the folder, the name and the path', () => {
    expect(labelKey('pages/my-tasks.json', 'MyTasks', '/title/textKey', new Set())).toBe('pages.myTasks.title')
  })

  it('numbers the key from 2 while it is taken', () => {
    expect(labelKey('pages/my-tasks.json', 'MyTasks', '/title/textKey', new Set(['pages.myTasks.title']))).toBe(
      'pages.myTasks.title2',
    )
    expect(
      labelKey(
        'pages/my-tasks.json',
        'MyTasks',
        '/title/textKey',
        new Set(['pages.myTasks.title', 'pages.myTasks.title2']),
      ),
    ).toBe('pages.myTasks.title3')
  })

  it('turns a kebab-case folder and name into camel case', () => {
    expect(labelKey('data-sources/open-orders.json', 'open-orders', '/title/textKey', new Set())).toBe(
      'dataSources.openOrders.title',
    )
    expect(labelKey('roles/department-manager.json', 'department-manager', '/label/textKey', new Set())).toBe(
      'roles.departmentManager.label',
    )
  })

  it('uses application for the manifest at the root', () => {
    expect(labelKey('application.json', 'Shop', '/title/textKey', new Set())).toBe('application.shop.title')
  })

  it('leaves the name out when the file has none', () => {
    expect(labelKey('application.json', null, '/title/textKey', new Set())).toBe('application.title')
  })

  it('keeps the index of an array item and unescapes the pointer', () => {
    expect(labelKey('pages/my-tasks.json', 'MyTasks', '/widgets/0/title/textKey', new Set())).toBe(
      'pages.myTasks.widgets.0.title',
    )
    expect(labelKey('pages/my-tasks.json', 'MyTasks', '/a~1b/textKey', new Set())).toBe('pages.myTasks.a/b')
  })
})
