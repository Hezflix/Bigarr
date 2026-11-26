import type { QualityDefinition } from '@/api/client'

/**
 * The qualities a picker lists: best first, where a file's or a release's quality usually is (the server lists them
 * worst first). Unknown, the parser's placeholder, only with `allowUnknown`.
 */
export function qualityOptions(definitions: readonly QualityDefinition[], allowUnknown: boolean): QualityDefinition[] {
  return [...definitions].reverse().filter((q) => allowUnknown || q.id !== 0)
}

/**
 * The quality names a filter lists, each once: best first like a picker, by their place in the definitions (the
 * server lists them worst first). Names the definitions do not know come last, in alphabetical order.
 */
export function qualityNamesBestFirst(names: Iterable<string>, definitions: readonly QualityDefinition[]): string[] {
  const rank = new Map(definitions.map((d, i) => [d.name, i]))
  return [...new Set(names)].sort((a, b) => {
    const left = rank.get(a)
    const right = rank.get(b)
    if (left === undefined || right === undefined) {
      return left === right ? a.localeCompare(b) : left === undefined ? 1 : -1
    }
    return right - left
  })
}
