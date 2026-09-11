import { useEffect, useRef, useState } from 'react'
import { useAppStore } from '../store'
import type { Priority, Requirement } from '../types'
import { priorityLabel } from '../utils'

export function InlineRequirementFields({ item, field }: { item: Requirement; field: 'priority' | 'module' | 'dueDate' }) {
  const { modules, updateRequirement, lastOperationError } = useAppStore()
  const value = item[field] ?? ''
  const [draft, setDraft] = useState(value)
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)
  const locked = useRef(false)
  useEffect(() => setDraft(value), [value, item.version])
  const label = field === 'priority' ? '优先级' : field === 'module' ? '模块' : '期望完成日期'
  const save = async (next: string) => {
    if (locked.current || next === value) return
    locked.current = true; setBusy(true); setFailed(false)
    try {
      const patch = field === 'priority' ? { priority: next as Priority } : field === 'module' ? { module: next } : { dueDate: next || null }
      const ok = await updateRequirement(item.id, patch, `更新了${label}`)
      if (!ok) { setDraft(value); setFailed(true) }
    } catch { setDraft(value); setFailed(true) }
    finally { locked.current = false; setBusy(false) }
  }
  return <div className="inline-requirement-field">
    {field === 'dueDate'
      ? <input type="date" aria-label={`${item.id} ${label}`} value={draft} disabled={busy}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={() => void save(draft)}
          onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur(); if (event.key === 'Escape') setDraft(value) }} />
      : <select data-priority={field === 'priority' ? draft : undefined} aria-label={`${item.id} ${label}`} value={draft} disabled={busy} onChange={(event) => { setDraft(event.target.value); void save(event.target.value) }}>
          {field === 'priority' ? (Object.keys(priorityLabel) as Priority[]).map((key) => <option data-priority={key} key={key} value={key}>{priorityLabel[key]}</option>) : Array.from(new Set([item.module, ...modules])).map((name) => <option key={name} value={name}>{name}</option>)}
        </select>}
    {busy && <small role="status">保存中…</small>}
    {failed && <small role="alert">{lastOperationError || '保存失败，请重试'}</small>}
  </div>
}
