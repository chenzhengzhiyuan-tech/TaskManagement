import { useRef, useState } from 'react'
import { ExistingChildren } from './NewChildren'
import { useAppStore } from '../store'
import { apiRequest } from '../api'
export function ChildRequirementPicker({ parentId }: { parentId: string }) {
  const { mode, refresh, updateRequirement } = useAppStore()
  const [selected, setSelected] = useState<string[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)
  async function link() {
    if (lock.current) return
    lock.current = true; setBusy(true); setError('')
    try {
      if (mode === 'api') { await apiRequest(`/requirements/${parentId}/children`, { method: 'POST', body: JSON.stringify({childIds: selected}) }); await refresh() }
      else for (const id of selected) if (!await updateRequirement(id, {parentId})) throw new Error('绑定失败')
      setSelected([])
    } catch (e) { setError(e instanceof Error ? e.message : '绑定失败，请重试') }
    finally { lock.current = false; setBusy(false) }
  }
  return <fieldset className="child-picker" disabled={busy} style={{border: 0, padding: 0, minWidth: 0}}><ExistingChildren selected={selected} onChange={setSelected} exclude={[parentId]}/><button className="button button--ghost button--compact" disabled={!selected.length || busy} type="button" onClick={() => void link()}>{busy ? '绑定中…' : `绑定选中的子需求（${selected.length}）`}</button>{error && <p className="form-error" role="alert">{error}</p>}</fieldset>
}
