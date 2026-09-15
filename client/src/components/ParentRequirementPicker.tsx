import { CornerDownRight, Search, X } from 'lucide-react'
import { useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { useDropdown } from './useDropdown'
import type { Requirement } from '../types'

interface ParentRequirementPickerProps {
  requirements: Requirement[]
  value: string | null
  onChange: (id: string | null) => void
  excludeId?: string
  disabled?: boolean
}

export function ParentRequirementPicker({ requirements, value, onChange, excludeId, disabled }: ParentRequirementPickerProps) {
  const { open, setOpen, root, menu, position } = useDropdown(380)
  const [query, setQuery] = useState('')
  const selected = requirements.find((item) => item.id === value)
  const candidates = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    return requirements
      .filter((item) => !item.parentId && item.id !== excludeId)
      .filter((item) => !normalized || `${item.id} ${item.title} ${item.module}`.toLowerCase().includes(normalized))

  }, [excludeId, query, requirements])

  return (
    <div ref={root} className={`parent-picker ${disabled ? 'parent-picker--disabled' : ''}`}>
      {selected && <div className="parent-picker__selected"><CornerDownRight size={14} /><span><strong>{selected.id}</strong>{selected.title}</span><button type="button" aria-label="移除父需求" onClick={() => onChange(null)} disabled={disabled}><X size={13} /></button></div>}
      <div className="parent-picker__search"><Search size={14} /><input aria-label="搜索父需求" value={query} onClick={() => setOpen(true)} onFocus={() => setOpen(true)} onChange={(event) => { setQuery(event.target.value); setOpen(true) }} placeholder={selected ? '搜索并更换父需求' : '搜索需求编号、标题或模块'} disabled={disabled} /></div>
      {open && !disabled && createPortal(<div ref={menu} className="relation-options dropdown-menu" style={{ position: 'fixed', ...position, zIndex: 350 }} role="listbox" aria-label="父需求选项">{candidates.length ? candidates.map(item => <button type="button" role="option" aria-selected={item.id === value} key={item.id} onClick={() => { onChange(item.id); setQuery(''); setOpen(false) }}><span><strong>{item.id}</strong> {item.title}</span><small>{item.module} · {item.description.slice(0, 100)}</small></button>) : <p>没有匹配的顶级需求</p>}</div>, document.body)}
      {!selected && !query && <small className="parent-picker__hint">输入关键词查找顶级需求；留空表示当前需求为顶级需求。</small>}
    </div>
  )
}
