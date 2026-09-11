import { Link2, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import type { Requirement } from '../types'

interface ChildRequirementPickerProps {
  requirements: Requirement[]
  parentId: string
  onLink: (childId: string) => void
}

export function ChildRequirementPicker({ requirements, parentId, onLink }: ChildRequirementPickerProps) {
  const [query, setQuery] = useState('')
  const candidates = useMemo(() => {
    const normalized = query.trim().toLowerCase()
    const idsWithChildren = new Set(requirements.filter((item) => item.parentId).map((item) => item.parentId))
    return requirements
      .filter((item) => item.id !== parentId && !item.parentId && !idsWithChildren.has(item.id))
      .filter((item) => !normalized || `${item.id} ${item.title} ${item.module}`.toLowerCase().includes(normalized))
      .slice(0, 10)
  }, [parentId, query, requirements])

  return (
    <div className="child-picker">
      <div className="parent-picker__search"><Search size={14} /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索需要绑定的子需求编号、标题或模块" /></div>
      {query && <div className="parent-picker__results child-picker__results" role="listbox">{candidates.length ? candidates.map((item) => <button type="button" role="option" aria-selected="false" key={item.id} onClick={() => { onLink(item.id); setQuery('') }}><Link2 size={13} /><span className="mono-id">{item.id}</span><span>{item.title}</span><small>{item.module}</small></button>) : <span>没有可绑定的顶级需求。包含子需求的父需求不能再绑定为子需求。</span>}</div>}
      {!query && <small className="parent-picker__hint">输入关键词，将一个现有顶级需求绑定为当前需求的子需求。</small>}
    </div>
  )
}
