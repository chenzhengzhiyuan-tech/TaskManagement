import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import type { User } from '../types'
import { useDropdown } from './useDropdown'

export function AssigneePicker({ value, users, onChange, label = '处理人', single = false }: { value: string[]; users: User[]; onChange: (ids: string[]) => unknown; label?: string; single?: boolean }) {
  const { open, setOpen, root, menu, position } = useDropdown()
  const [busy, setBusy] = useState(false)
  const [query, setQuery] = useState('')
  const [choice, setChoice] = useState(0)
  const matches = users.filter(user => (user.active !== false || value.includes(user.id)) && `${user.name} ${user.account}`.toLowerCase().includes(query.trim().toLowerCase()))
  useEffect(() => { menu.current?.querySelector('.is-focused')?.scrollIntoView?.({block: 'nearest'}) }, [choice, menu])
  async function change(ids: string[]) { setBusy(true); try { await onChange(ids); if (single) setOpen(false) } finally { setBusy(false) } }
  function select(id: string) { if (!busy) void change(single ? [id] : value.includes(id) ? value.filter(x => x !== id) : [...value, id]) }
  return <div className="assignee-picker" ref={root} onClick={event => event.stopPropagation()}>
    <button type="button" aria-label={label} aria-expanded={open} onClick={() => { setQuery(''); setChoice(0); setOpen(!open) }}>{value.length ? value.map(id => users.find(user => user.id === id)?.name ?? '已删除成员').join('、') : '未指定'} ▾</button>
    {open && createPortal(<div ref={menu} className="assignee-picker__menu dropdown-menu" style={{ position: 'fixed', ...position, zIndex: 350 }} role="group" aria-label={`${label}选项`}>
      <input className="member-search" autoFocus aria-label={`搜索${label}`} placeholder="输入姓名或账号搜索" value={query} onChange={e => { setQuery(e.target.value); setChoice(0) }} onKeyDown={e => {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); setChoice(i => Math.max(0, Math.min(matches.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))) }
        if (e.key === 'Enter') { e.preventDefault(); if (matches[choice]) select(matches[choice].id) }
      }} />
      <button type="button" disabled={busy} onClick={() => void change([])}>清空{label}</button>
      {matches.map((user, index) => <label className={`member-option ${index === choice ? 'is-focused' : ''}`} key={user.id}><input type={single ? 'radio' : 'checkbox'} checked={value.includes(user.id)} disabled={busy} onChange={() => select(user.id)} /><span>{user.name}<small>{user.account}</small></span></label>)}
      {!matches.length && <p className="dropdown-empty">没有匹配的成员</p>}
    </div>, document.body)}
  </div>
}
