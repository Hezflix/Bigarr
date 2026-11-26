/** The letter an avatar shows when there is no picture: the first character of the name, whole even when it is an emoji. */
export function userInitial(name: string): string {
  return [...name.trim()][0] ?? ''
}
