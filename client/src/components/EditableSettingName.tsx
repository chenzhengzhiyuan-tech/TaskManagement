import { useState } from 'react'

export function EditableSettingName({ name, onSave }: { name: string; onSave: (name: string) => Promise<boolean> }) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(name)
  const [saving, setSaving] = useState(false)
  async function save() {
    setSaving(true)
    try { if (await onSave(draft.trim())) setEditing(false) } finally { setSaving(false) }
  }
  return <div className="setting-name-editor">{editing ? <>
    <input aria-label={`修改${name}`} autoFocus value={draft} disabled={saving} onChange={event => setDraft(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !saving && draft.trim()) void save(); if (event.key === 'Escape') setEditing(false) }} />
    <button className="button button--ghost button--compact" disabled={saving || !draft.trim()} onClick={() => void save()}>保存</button>
    <button className="button button--ghost button--compact" disabled={saving} onClick={() => setEditing(false)}>取消</button>
  </> : <><strong>{name}</strong><button className="button button--ghost button--compact" onClick={() => { setDraft(name); setEditing(true) }}>修改</button></>}</div>
}
