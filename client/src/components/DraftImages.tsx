import { uuid } from '../uuid'
import { useEffect, useRef, useState } from 'react'

export interface DraftImage { id: string; file: File; url: string; progress: number; done: boolean; error?: string }
export function DraftImages({ items, setItems, disabled }: { items: DraftImage[]; setItems: React.Dispatch<React.SetStateAction<DraftImage[]>>; disabled: boolean }) {
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState('')
  const [preview, setPreview] = useState<string | null>(null)
  function add(files: File[]) {
    if (disabled) return
    const valid = files.filter(file => ['image/png', 'image/jpeg', 'image/gif', 'image/webp'].includes(file.type) && file.size > 0 && file.size <= 500 * 1024 * 1024)
    setError(valid.length !== files.length ? '部分文件不符合要求：仅支持 JPG、PNG、GIF、WebP，每张最大 500MB' : '')
    setItems(previous => [...previous, ...valid.map(file => ({ id: uuid(), file, url: URL.createObjectURL(file), progress: 0, done: false }))])
  }
  useEffect(() => {
    const paste = (event: ClipboardEvent) => {
      if (disabled || (event.target instanceof Element && event.target.closest('input,textarea,select,[contenteditable]:not([contenteditable="false"]),[role="textbox"]'))) return
      const files = Array.from(event.clipboardData?.items ?? []).filter(item => item.kind === 'file' && item.type.startsWith('image/')).map(item => item.getAsFile()).filter((file): file is File => Boolean(file))
      if (files.length) { event.preventDefault(); event.stopImmediatePropagation(); add(files) }
    }
    document.addEventListener('paste', paste, true)
    return () => document.removeEventListener('paste', paste, true)
  })
  return <section className="detail-section" aria-label="新建需求图片附件">
    <div className="section-heading"><h3>图片附件</h3><button type="button" disabled={disabled} onClick={() => input.current?.click()}>添加图片</button></div>
    <input ref={input} type="file" hidden multiple accept="image/png,image/jpeg,image/gif,image/webp" onChange={event => { add(Array.from(event.target.files ?? [])); event.target.value = '' }} />
    <p>支持多选、追加和 Ctrl+V 粘贴图片，单张最大 500MB。创建需求后开始上传。</p>
    {error && <div role="alert">{error}</div>}
    <div className="attachment-grid">{items.map(item => <article className="attachment-tile" key={item.id}>
      <button type="button" onClick={() => setPreview(item.url)}><img src={item.url} alt={item.file.name} style={{ width: '100%', height: 120, objectFit: 'contain' }} /></button>
      <strong>{item.file.name}</strong><span>{item.done ? '已上传' : item.error ?? (item.progress ? `${item.progress}%` : '待上传')}</span>
      <progress max={100} value={item.progress} />
      {!item.done && <button type="button" disabled={disabled} onClick={() => { setItems(previous => previous.filter(entry => entry.id !== item.id)); URL.revokeObjectURL(item.url) }}>删除</button>}
    </article>)}</div>
    {preview && <div className="modal-layer attachment-preview-layer" onClick={() => setPreview(null)}><section className="attachment-preview" role="dialog" aria-label="新建附件大图"><button type="button" onClick={() => setPreview(null)}>关闭大图</button><img src={preview} alt="附件大图" style={{ maxWidth: '90vw', maxHeight: '80vh', objectFit: 'contain' }} /></section></div>}
  </section>
}
