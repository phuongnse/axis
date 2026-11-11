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
