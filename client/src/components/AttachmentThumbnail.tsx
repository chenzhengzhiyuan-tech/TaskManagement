import { useEffect, useRef, useState } from 'react'
import type { Attachment } from '../types'
import { useAppStore } from '../store'

export function AttachmentThumbnail({ attachment, onOpen, onDownload, onDelete }: { attachment: Attachment; onOpen: () => void; onDownload: () => void; onDelete?: () => void }) {
  const root = useRef<HTMLElement>(null)
  const { loadAttachmentBlob } = useAppStore()
  const [url, setUrl] = useState('')
  const [error, setError] = useState(false)
  useEffect(() => {
    let cancelled = false; let objectUrl = ''; let started = false
    const load = async () => {
      if (started) return; started = true
      try {
        const blob = await loadAttachmentBlob(attachment)
        if (cancelled) return
        if (!blob || !blob.type.startsWith('image/')) { setError(true); return }
        objectUrl = URL.createObjectURL(blob); setUrl(objectUrl)
      } catch { if (!cancelled) setError(true) }
    }
    const observer = new IntersectionObserver(entries => { if (entries.some(entry => entry.isIntersecting)) { void load(); observer.disconnect() } }, { rootMargin: '200px' })
    if (root.current) observer.observe(root.current)
    return () => { cancelled = true; observer.disconnect(); if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [attachment, loadAttachmentBlob])
  return <article ref={root} className="attachment-tile">
    <button className="attachment-tile__image" type="button" onClick={onOpen} aria-label={`预览 ${attachment.name}`}>
      {error ? <span>图片读取失败，点击重试预览</span> : url ? <img src={url} alt={attachment.name} loading="lazy" onError={() => setError(true)} /> : <span>正在加载图片…</span>}
    </button>
    <strong title={attachment.name}>{attachment.name}</strong>
    <footer><span>{(attachment.size / 1024 / 1024).toFixed(1)} MB</span><button type="button" onClick={onDownload}>下载</button>{onDelete && <button type="button" onClick={onDelete} aria-label={`删除图片 ${attachment.name}`}>删除</button>}</footer>
  </article>
}
