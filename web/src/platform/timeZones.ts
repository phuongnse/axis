/** A time zone as the select shows it, such as "(UTC+07:00) Asia/Ho Chi Minh". `offset` is minutes from UTC. */
export function timeZoneText(zone: { id: string; offset: number }): string {
  const minutes = Math.abs(zone.offset)
  const clock = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
  return `(UTC${zone.offset < 0 ? '-' : '+'}${clock}) ${zone.id.replaceAll('_', ' ')}`
}

/** The browser's time zone: its IANA id and its current offset in minutes from UTC. */
export function browserTimeZone(): { id: string; offset: number } {
  const format = new Intl.DateTimeFormat('en', { timeZoneName: 'longOffset' })
  const name = format.formatToParts(new Date()).find((part) => part.type === 'timeZoneName')?.value ?? 'GMT'
  const match = /^GMT(?:([+-])(\d{1,2})(?::(\d{2}))?)?$/.exec(name)
  const minutes = match?.[2] === undefined ? 0 : Number(match[2]) * 60 + Number(match[3] ?? 0)
  return { id: format.resolvedOptions().timeZone, offset: match?.[1] === '-' ? -minutes : minutes }
}
