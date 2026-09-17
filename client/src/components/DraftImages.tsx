import { attachmentAccept, attachmentError, attachmentHint, attachmentType, isVideo } from '../attachmentMedia'
import { DraftMediaPreview } from './MediaContent'
import { uuid } from '../uuid'
import { useEffect, useRef, useState } from 'react'

export interface DraftImage { id: string; file: File; url: string; progress: number; done: boolean; error?: string }
export function DraftImages({ items, setItems, disabled }: { items: DraftImage[]; setItems: React.Dispatch<React.SetStateAction<DraftImage[]>>; disabled: boolean }) {
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState('')
  const [preview, setPreview] = useState<DraftImage | null>(null)
  function add(files: File[]) {
    if (disabled) return
    const valid = files.filter(file => !attachmentError(file))
    setError(files.map(file => attachmentError(file) ? `${file.name}：${attachmentError(file)}` : '').filter(Boolean).join('；'))
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
  return <section className="detail-section" aria-label="新建需求附件">
    <div className="section-heading"><h3>附件</h3><button type="button" disabled={disabled} onClick={() => input.current?.click()}>添加附件</button></div>
    <input ref={input} type="file" hidden multiple accept={attachmentAccept} onChange={event => { add(Array.from(event.target.files ?? [])); event.target.value = '' }} />
    <p>{attachmentHint} 支持多选和粘贴图片，创建需求后开始上传。</p>
    {error && <div role="alert">{error}</div>}
    <div className="attachment-grid">{items.map(item => <article className="attachment-tile" key={item.id}>
      <button type="button" className="attachment-tile__image" onClick={() => setPreview(item)}>{isVideo(attachmentType(item.file)) ? <span className="video-placeholder">▶<small>点击预览视频</small></span> : <img src={item.url} alt={item.file.name} />}</button>
      <strong>{item.file.name}</strong><span>{item.done ? '已上传' : item.error ?? (item.progress ? `${item.progress}%` : '待上传')}</span>
      <progress max={100} value={item.progress} />
      {!item.done && <button type="button" disabled={disabled} onClick={() => { setItems(previous => previous.filter(entry => entry.id !== item.id)); URL.revokeObjectURL(item.url) }}>删除</button>}
    </article>)}</div>
    {preview && <DraftMediaPreview url={preview.url} type={attachmentType(preview.file)} name={preview.file.name} onClose={() => setPreview(null)} />}
  </section>
}
