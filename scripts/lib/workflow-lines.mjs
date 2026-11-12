// Reads a GitHub workflow line by line, since the scripts' tests have no YAML dependency. The tests that check a
// workflow's jobs and steps share these helpers.

// The number of spaces before a line's first character.
export const indentOf = (line) => line.length - line.trimStart().length

// Whether a line holds more than blanks or a comment.
export const isContent = (line) => line.trim() !== '' && !line.trimStart().startsWith('#')

// The lines of the block that starts after `start` and ends before the next content line at `indent` or less.
export const blockAfter = (lines, start, indent) => {
  const block = []
  for (const line of lines.slice(start + 1)) {
    if (isContent(line) && indentOf(line) <= indent) break
    block.push(line)
  }
  return block
}

// A YAML scalar without the quotes around it.
export const unquote = (value) => value.trim().replace(/^(['"])(.*)\1$/, '$2')

// The step blocks of a workflow: each runs from a `- ` line to the next `- ` line at the same or lower indentation.
export const stepsOf = (lines) => {
  const steps = []
  lines.forEach((line, index) => {
    if (!/^\s*-\s/.test(line)) return
    const indent = indentOf(line)
    const rest = []
    for (const next of lines.slice(index + 1)) {
      if (isContent(next) && indentOf(next) <= indent && (/^\s*-\s/.test(next) || indentOf(next) < indent)) break
      rest.push(next)
    }
    steps.push([line.replace('-', ' '), ...rest])
  })
  return steps
}

// The value of `key` in a step block, or undefined when the step has no such key.
export const valueOf = (step, key) => {
  const line = step.find((candidate) => new RegExp(`^\\s*${key}:`).test(candidate))
  return line === undefined ? undefined : unquote(line.replace(new RegExp(`^\\s*${key}:`), ''))
}
