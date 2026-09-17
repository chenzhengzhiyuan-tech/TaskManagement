import { ConfirmDialog } from './ConfirmDialog'
import { attachmentAccept, attachmentError, attachmentHint, attachmentType, isVideo } from '../attachmentMedia'
import { DraftMediaPreview } from './MediaContent'
import { uuid } from '../uuid'
import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { MessageSquare, Send, Upload, X } from 'lucide-react'
import type { Attachment, CommentMention, Requirement, User } from '../types'
import { useAppStore } from '../store'
import { adjustMentions } from '../commentMentions'
import { formatDate, getUser } from '../utils'
import { AttachmentThumbnail } from './AttachmentThumbnail'

interface DraftImage { id: string; file: File; url: string }
export function CommentText({ text, mentions = [] }: { text: string; mentions?: CommentMention[] }) {
  const parts: ReactNode[] = []; let cursor = 0
  for (const mention of [...mentions].sort((a, b) => a.start - b.start)) {
    if (mention.start < cursor || text.slice(mention.start, mention.start + mention.length) !== `@${mention.name}`) continue
    parts.push(text.slice(cursor, mention.start), <mark key={`${mention.start}:${mention.userId}`} className="comment-mention">{text.slice(mention.start, mention.start + mention.length)}</mark>)
    cursor = mention.start + mention.length
  }
  parts.push(text.slice(cursor))
  return <>{parts}</>
}

export function CommentsPanel({ requirement, visible, onDirtyChange, onOpenImage, onDownloadImage }: {
  requirement: Requirement; visible: boolean; onDirtyChange: (dirty: boolean) => void;
  onOpenImage: (attachment: Attachment) => void; onDownloadImage: (attachment: Attachment) => void;
}) {
  const { users, currentUser, addComment, deleteComment, uploadAttachment } = useAppStore()
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null)
  const deleting = useRef(false)
  const [text, setText] = useState('')
  const [mentions, setMentions] = useState<CommentMention[]>([])
  const [preview, setPreview] = useState<DraftImage | null>(null)
  const [images, setImages] = useState<DraftImage[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [progress, setProgress] = useState('')
  const [menu, setMenu] = useState<{ start: number; end: number; query: string } | null>(null)
  const [choice, setChoice] = useState(0)
  const input = useRef<HTMLTextAreaElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const imageRef = useRef(images)
  useEffect(() => { imageRef.current = images }, [images])
  const uploadedImages = useRef(new Map<string, Attachment>())
  const submission = useRef({ key: '', id: '' })
  const sending = useRef(false)
  const [target] = useState(() => new URLSearchParams(window.location.search).get('comment'))
  const candidates = users.filter(user => user.active !== false && user.wecomBound
    && (!menu?.query || `${user.name} ${user.account}`.toLocaleLowerCase().includes(menu.query.toLocaleLowerCase())))
  useEffect(() => { onDirtyChange(Boolean(text || images.length || busy)) }, [text, images.length, busy, onDirtyChange])
  useEffect(() => () => { imageRef.current.forEach(image => URL.revokeObjectURL(image.url)) }, [])
  useEffect(() => {
    if (!visible || !target) return
    document.getElementById(`comment-${target}`)?.scrollIntoView?.({ block: 'center' })
  }, [visible, target, requirement.comments.length])

  function findMention(value: string, caret: number) {
    const match = value.slice(0, caret).match(/@([^\s@]*)$/)
    const start = match ? caret - match[0].length : -1
    setMenu(match && !mentions.some(mention => start >= mention.start && start < mention.start + mention.length)
      ? { start, end: caret, query: match[1] } : null)
    setChoice(0)
  }
  function selectMember(user: User) {
    if (!menu) return
    const label = `@${user.name}`
    const next = text.slice(0, menu.start) + label + ' ' + text.slice(menu.end)
    setMentions([...adjustMentions(text, next, mentions), { userId: user.id, name: user.name, start: menu.start, length: label.length }])
    setText(next); setMenu(null)
    requestAnimationFrame(() => { input.current?.focus(); input.current?.setSelectionRange(menu.start + label.length + 1, menu.start + label.length + 1) })
  }
  function addImages(files: File[]) {
    const valid: DraftImage[] = []; let problem = ''
    for (const file of files) {
      if (attachmentError(file)) {
        problem = `${file.name}：${attachmentError(file)}`; continue
      }
      valid.push({ id: uuid(), file, url: URL.createObjectURL(file) })
    }
    setImages(previous => [...previous, ...valid]); setError(problem)
  }
  async function send() {
    if (sending.current || (!text.trim() && !images.length)) return
    sending.current = true; setBusy(true); setMenu(null); setError('')
    try {
      if (mentions.some(mention => !users.some(user => user.id === mention.userId && user.active !== false && user.wecomBound)))
        throw new Error('只能 @ 已启用且已绑定企微的成员，请移除失效的 @ 后重试')
      const attachmentIds: string[] = []
      for (const image of imageRef.current) {
        let uploaded = uploadedImages.current.get(image.id)
        if (!uploaded) {
          uploaded = await uploadAttachment(requirement.id, image.file, percent => setProgress(`${image.file.name} · ${percent}%`), true)
          uploadedImages.current.set(image.id, uploaded)
        }
        attachmentIds.push(uploaded.id)
      }
      const key = JSON.stringify({ text, mentions, attachmentIds })
      if (submission.current.key !== key) submission.current = { key, id: uuid() }
      await addComment(requirement.id, text, { requestId: submission.current.id, mentions, attachmentIds })
      setText(''); setMentions([]); images.forEach(image => URL.revokeObjectURL(image.url)); setImages([])
      submission.current = { key: '', id: '' }; uploadedImages.current.clear()
    } catch (failure) { setError(failure instanceof Error ? failure.message : '发送失败，请重试') }
    finally { sending.current = false; setBusy(false); setProgress('') }
  }
  return <section className="comments-panel" hidden={!visible}>
    <div className="comment-editor">
      <label htmlFor={`comment-input-${requirement.id}`}>发表评论</label>
      <div className="comment-editor__input">
        <textarea ref={input} id={`comment-input-${requirement.id}`} value={text} disabled={busy}
          placeholder="发表评论，使用 @ 提醒成员…" aria-expanded={Boolean(menu)} aria-controls={menu ? 'comment-members' : undefined}
          onChange={event => { setMentions(adjustMentions(text, event.target.value, mentions)); setText(event.target.value); findMention(event.target.value, event.target.selectionStart) }}
          onClick={event => findMention(text, event.currentTarget.selectionStart)}
          onBlur={() => setMenu(null)}
          onPaste={event => {
            const files = Array.from(event.clipboardData.files)
            if (files.length) { event.preventDefault(); event.stopPropagation(); addImages(files) }
          }}
          onKeyDown={event => {
            if (event.nativeEvent.isComposing) return
            if (menu && ['ArrowDown', 'ArrowUp', 'Enter', 'Escape'].includes(event.key) && !event.ctrlKey && !event.metaKey && !(event.key === 'Enter' && !candidates.length)) {
              event.preventDefault(); event.stopPropagation()
              if (event.key === 'Escape') setMenu(null)
              else if (event.key === 'Enter' && candidates.length) selectMember(candidates[Math.min(choice, candidates.length - 1)])
              else setChoice(previous => Math.max(0, Math.min(candidates.length - 1, previous + (event.key === 'ArrowDown' ? 1 : -1))))
            } else if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); void send() }
          }} />
        {menu && <div className="comment-members" id="comment-members" role="listbox" aria-label="可提醒的成员">
          <small>仅显示已启用且已绑定企微的成员</small>
          {candidates.length ? candidates.map((user, index) => <button type="button" key={user.id} role="option" aria-selected={index === choice}
            onMouseDown={event => event.preventDefault()} onClick={() => selectMember(user)}><strong>{user.name}</strong><span>{user.account}</span></button>)
            : <p>没有匹配的已绑定成员</p>}
        </div>}
      </div>
      {mentions.length > 0 && <div className="comment-editor__recipients">将提醒：{[...new Set(mentions.map(mention => mention.name))].join('、')}</div>}
      {images.length > 0 && <div className="comment-draft-images">{images.map(image => <figure key={image.id}>
        <button type="button" className="comment-draft-preview" onClick={() => setPreview(image)} aria-label={`预览待发送附件 ${image.file.name}`}>{isVideo(attachmentType(image.file)) ? <span className="video-placeholder">▶<small>视频</small></span> : <img src={image.url} alt={image.file.name} />}</button>
        <figcaption title={image.file.name}>{image.file.name}</figcaption><button type="button" disabled={busy} aria-label={`移除 ${image.file.name}`} onClick={() => { URL.revokeObjectURL(image.url); setImages(previous => previous.filter(entry => entry.id !== image.id)) }}><X size={14} /></button>
      </figure>)}</div>}
      <div className="comment-editor__actions"><button className="button button--ghost button--compact" type="button" disabled={busy} onClick={() => fileInput.current?.click()}><Upload size={14} />添加附件</button>
        <span>Enter 换行 · Ctrl+Enter 发送</span><button className="button button--primary button--compact" type="button" disabled={busy || (!text.trim() && !images.length)} onClick={() => void send()}><Send size={14} />{busy ? '发送中…' : '发送'}</button></div>
      <input hidden ref={fileInput} type="file" multiple accept={attachmentAccept} aria-label="评论附件" disabled={busy} onChange={event => { addImages(Array.from(event.target.files ?? [])); event.target.value = '' }} />
      <p className="attachment-hint">{attachmentHint}</p>
      {preview && <DraftMediaPreview url={preview.url} type={attachmentType(preview.file)} name={preview.file.name} onClose={() => setPreview(null)} />}
      {progress && <p role="status">{progress}</p>}{error && <p className="comment-error" role="alert">{error}</p>}
    </div>
    <ConfirmDialog open={Boolean(deleteTarget)} title="删除评论" description="评论和其中的附件将一起删除，确定继续吗？" danger onClose={() => setDeleteTarget(null)} onConfirm={() => {
      if (!deleteTarget || deleting.current) return
      deleting.current = true
      void deleteComment(requirement.id, deleteTarget).then(result => { if (result.ok) setDeleteTarget(null); else setError(result.reason ?? '删除失败') }).finally(() => { deleting.current = false })
    }} />
    <div className="comment-list">
      {target && !requirement.comments.some(item => item.id === target) && <p role="status">该评论已不存在，下面展示当前讨论。</p>}
      {requirement.comments.length ? requirement.comments.map(item => {
        const author = getUser(users, item.authorId)
        return <article id={`comment-${item.id}`} className={target === item.id ? 'comment-target' : ''} key={item.id}>
          <span className="avatar avatar--small" style={{ '--avatar-color': author?.color } as React.CSSProperties}>{author?.initials}</span>
          <div className="comment-content"><header><strong>{author?.name ?? '已删除成员'}</strong><time>{formatDate(item.createdAt, true)}</time>{(currentUser.role === 'admin' || currentUser.id === item.authorId) && <button type="button" className="button button--ghost button--compact" aria-label={`删除评论 ${item.id}`} onClick={() => setDeleteTarget(item.id)}>删除</button>}</header>
            <p className="comment-text"><CommentText text={item.content} mentions={item.mentions} /></p>
            {!!item.attachments?.length && <div className="attachment-grid">{item.attachments.map(image => <AttachmentThumbnail key={image.id} attachment={image} onOpen={() => onOpenImage(image)} onDownload={() => onDownloadImage(image)} />)}</div>}
          </div>
        </article>
      }) : <div className="empty-state empty-state--compact"><MessageSquare size={24} /><strong>还没有评论</strong><span>在上方输入框开始讨论</span></div>}
    </div>
  </section>
}
