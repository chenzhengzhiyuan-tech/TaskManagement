import { assigneeIds } from '../assignees'
import { useEffect, useRef, useState } from 'react'
import { useAppStore } from '../store'
import { priorityLabel } from '../utils'

export const selectedValues = (value: string) => value.split(',').filter(Boolean)
export const filterMatches = (filter: string, value: string | null | undefined) => !filter || selectedValues(filter).includes(value ?? '')
export const isMyTask = (item: { assigneeIds?: string[]; assigneeId: string | null; reviewerId?: string | null; statusId: string }, userId: string) => assigneeIds(item).includes(userId) || (item.reviewerId === userId && item.statusId === 'review')

function MultiFilter({ label, value, options, onChange }: { label: string; value: string; options: { id: string; name: string }[]; onChange: (value: string) => void }) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const selected = selectedValues(value)
  useEffect(() => {
    if (!open) return
    const outside = (event: PointerEvent) => { if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false) }
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false) }
    document.addEventListener('pointerdown', outside); document.addEventListener('keydown', escape)
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape) }
  }, [open])
  return <div ref={root} className="multi-filter">
    <button className="button button--ghost button--compact" type="button" aria-expanded={open} aria-label={`${label}筛选`} onClick={() => setOpen(!open)}>{selected.length ? `${label} (${selected.length})` : `全部${label}`} ▾</button>
    {open && <div className="multi-filter__menu" role="group" aria-label={`${label}选项`}>
      <button type="button" onClick={() => onChange('')}>清空（全部）</button>
      {options.map(option => <label key={option.id}><input type="checkbox" checked={selected.includes(option.id)} onChange={event => onChange(event.target.checked ? [...selected, option.id].join(',') : selected.filter(id => id !== option.id).join(','))} />{option.name}</label>)}
    </div>}
  </div>
}

export function RequirementFilters({ query, setQuery, values, setters, children, mine, setMine }: {
  query: string; setQuery: (value: string) => void; values: string[]; setters: ((value: string) => void)[]; children?: React.ReactNode; mine: string; setMine: (value: string) => void
}) {
  const { statuses, users, iterations, requirementTypes } = useAppStore()
  const clear = () => { setQuery(''); setters.forEach(set => set('')); setMine('') }
  const options = [statuses, users.filter(user => user.active !== false), Object.entries(priorityLabel).map(([id, name]) => ({ id, name })), iterations, requirementTypes.filter(type => type.enabled)]
  return <section className="requirements-toolbar">
    <div className="list-search"><input aria-label="搜索需求" placeholder="搜索编号、标题或模块" value={query} onChange={event => setQuery(event.target.value)} /></div>
    {['状态', '处理人', '优先级', '迭代', '需求单类型'].map((label, index) => <MultiFilter key={label} label={label} value={values[index]} options={options[index]} onChange={setters[index]} />)}
    <button className="button button--ghost button--compact" type="button" onClick={clear}>清空筛选</button>
    {children}
    <button className="button button--ghost button--compact" style={{ marginLeft: 'auto' }} type="button" aria-pressed={Boolean(mine)} onClick={() => { if (mine) setMine(''); else { clear(); setMine('true') } }}>我的任务</button>
  </section>
}
