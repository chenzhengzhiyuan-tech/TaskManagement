import { useEffect, useRef, useState } from 'react'
export function useDropdown(width = 280) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const menu = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ top?: number | 'auto'; bottom?: number; left: number; width: number; maxHeight: number }>({ top: 0, left: 8, width, maxHeight: 320 })
  useEffect(() => {
    if (!open) return
    const place = () => {
      const rect = root.current?.getBoundingClientRect(); if (!rect) return
      const w = Math.min(width, window.innerWidth - 16)
      const below = window.innerHeight - rect.bottom - 12
      const height = Math.max(80, Math.min(320, Math.max(below, rect.top - 12)))
      setPosition({ top: below >= height ? rect.bottom + 4 : 'auto', bottom: below >= height ? undefined : window.innerHeight - rect.top + 4, left: Math.max(8, Math.min(rect.left, window.innerWidth - w - 8)), width: w, maxHeight: height })
    }
    place()
    const outside = (e: PointerEvent) => { if (e.target instanceof Node && !root.current?.contains(e.target) && !menu.current?.contains(e.target)) setOpen(false) }
    const key = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); setOpen(false); root.current?.querySelector('button')?.focus() } }
    const scroll = (e: Event) => { if (!(e.target instanceof Node && menu.current?.contains(e.target))) place() }
    document.addEventListener('pointerdown', outside); document.addEventListener('keydown', key, true)
    window.addEventListener('resize', place); window.addEventListener('scroll', scroll, true)
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', key, true); window.removeEventListener('resize', place); window.removeEventListener('scroll', scroll, true) }
  }, [open, width])
  return { open, setOpen, root, menu, position }
}
