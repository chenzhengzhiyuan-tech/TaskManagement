import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { User } from '../types'

export function AssigneePicker({ value, users, onChange, label = '处理人' }: { value: string[]; users: User[]; onChange: (ids: string[]) => unknown; label?: string }) {
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const menu = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState({ top: 0, left: 0, maxHeight: 280 })
  function toggle() {
    const rect = root.current?.getBoundingClientRect()
    if (rect) {
      const below = window.innerHeight - rect.bottom - 12
      const height = Math.min(280, Math.max(below, rect.top - 12))
      setPosition({ top: below >= height ? rect.bottom + 4 : Math.max(8, rect.top - height - 4), left: Math.max(8, Math.min(rect.left, window.innerWidth - 268)), maxHeight: height })
    }
    setOpen(!open)
  }
  useEffect(() => {
    const outside = (event: PointerEvent) => { if (event.target instanceof Node && !root.current?.contains(event.target) && !menu.current?.contains(event.target)) setOpen(false) }
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false) }
    const move = (event: Event) => { if (!(event.target instanceof Node && menu.current?.contains(event.target))) setOpen(false) }
    document.addEventListener('pointerdown', outside); document.addEventListener('keydown', escape)
    window.addEventListener('scroll', move, true); window.addEventListener('resize', move)
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape); window.removeEventListener('scroll', move, true); window.removeEventListener('resize', move) }
  }, [])
  async function change(ids: string[]) { setBusy(true); try { await onChange(ids) } finally { setBusy(false) } }
  return <div className="assignee-picker" ref={root} onClick={event => event.stopPropagation()}>
    <button type="button" aria-label={label} aria-expanded={open} onClick={toggle}>{value.length ? value.map(id => users.find(user => user.id === id)?.name ?? '已删除成员').join('、') : '未分配'} ▾</button>
    {open && createPortal(<div ref={menu} className="assignee-picker__menu" style={{ position: 'fixed', ...position, width: 260, zIndex: 300 }} role="group" aria-label={`${label}选项`}>
      <button type="button" disabled={busy} onClick={() => void change([])}>清空处理人</button>
      {users.filter(user => user.active !== false || value.includes(user.id)).map(user => <label key={user.id}><input type="checkbox" disabled={busy} checked={value.includes(user.id)} onChange={event => void change(event.target.checked ? [...value, user.id] : value.filter(id => id !== user.id))} />{user.name}<small>{user.account}</small></label>)}
    </div>, document.body)}
  </div>
}
