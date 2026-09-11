import { useEffect, useState } from 'react'

export function useClock() {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    let timer: number
    function update() {
      window.clearTimeout(timer)
      setNow(new Date())
      timer = window.setTimeout(update, 60_000 - Date.now() % 60_000)
    }
    update()
    window.addEventListener('focus', update)
    document.addEventListener('visibilitychange', update)
    return () => {
      window.clearTimeout(timer)
      window.removeEventListener('focus', update)
      document.removeEventListener('visibilitychange', update)
    }
  }, [])
  return now
}
