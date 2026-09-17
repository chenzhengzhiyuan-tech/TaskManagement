import { useState } from 'react'
import { isVideo } from '../attachmentMedia'

export function MediaContent({ url, type, name }: { url: string; type: string; name: string }) {
  const [failed, setFailed] = useState(false)
  if (failed) return <p role="alert">此文件无法在浏览器中预览，请下载后使用本地播放器打开。</p>
  return isVideo(type)
    ? <video src={url} controls playsInline preload="metadata" aria-label={name} onError={() => setFailed(true)} />
    : <img src={url} alt={name} onError={() => setFailed(true)} />
}

export function DraftMediaPreview({ url, type, name, onClose }: { url: string; type: string; name: string; onClose: () => void }) {
  return <div className="modal-layer attachment-preview-layer" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <section className="attachment-preview" role="dialog" aria-modal="true" aria-label={`预览 ${name}`}>
      <header><strong>{name}</strong><button type="button" className="button button--ghost button--compact" onClick={onClose}>关闭预览</button></header>
      <div className="attachment-preview__body"><MediaContent key={url} url={url} type={type} name={name} /></div>
    </section>
  </div>
}
