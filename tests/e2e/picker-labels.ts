import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'

interface Labelled {
  name?: string
  label?: { textKey?: string }
  title?: { textKey?: string }
  fields?: Labelled[]
}

/**
 * The label that a Studio reference picker shows for a resource of an application folder, or for one of its fields: the
 * English text of its `label`, or of a page's `title`, else its name, see
 * [reference pickers](docs/reference/studio.md#reference-pickers). `folder` is the resource's folder, such as `roles`
 * or `pages`. A spec picks the choice by this text and types the name to find it.
 */
export async function pickerLabel(app: string, folder: string, name: string, field?: string): Promise<string> {
  const { texts } = JSON.parse(await readFile(path.join(app, 'texts', 'en.json'), 'utf8')) as {
    texts: Record<string, string>
  }
  for (const file of await readdir(path.join(app, folder))) {
    const resource = JSON.parse(await readFile(path.join(app, folder, file), 'utf8')) as Labelled
    if (resource.name !== name) {
      continue
    }
    const item = field === undefined ? resource : resource.fields?.find((candidate) => candidate.name === field)
    const key = (item?.label ?? item?.title)?.textKey
    return (key === undefined ? undefined : texts[key]) || (field ?? name)
  }
  throw new Error(`The folder ${app} has no ${folder} resource named ${name}.`)
}
