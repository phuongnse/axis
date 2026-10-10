// Prints the application folders that the server activates at startup, one absolute path per
// line, for scripts/dev.sh to watch. It reads ActivateOnStartup from
// src/Axis.Server/appsettings.Development.json, then applies the ActivateOnStartup__N
// environment variables on top, as the server's configuration does. Relative paths are
// resolved from src/Axis.Server. Prints nothing when no folder is activated.
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const serverFolder = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../src/Axis.Server')

const settings = JSON.parse(readFileSync(path.join(serverFolder, 'appsettings.Development.json'), 'utf8'))
const values = new Map()
const configured = Array.isArray(settings.ActivateOnStartup) ? settings.ActivateOnStartup : []
configured.forEach((value, index) => values.set(index, value))

for (const [name, value] of Object.entries(process.env)) {
  const match = /^ActivateOnStartup__(\d+)$/i.exec(name)
  if (match) values.set(Number(match[1]), value)
}

for (const value of values.values()) {
  if (typeof value === 'string' && value !== '') console.log(path.resolve(serverFolder, value))
}
