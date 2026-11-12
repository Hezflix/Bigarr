import { useEffect, useState } from 'react'

/** A value that follows `value` after `delay` ms of quiet: the server is asked once per pause, not per keystroke. */
export function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(handle)
  }, [value, delay])
  return debounced
}
