import { AlertTriangle, X } from 'lucide-react'

interface ConfirmDialogProps {
  open: boolean
  title: string
  description: string
  confirmLabel?: string
  danger?: boolean
  onConfirm: () => void
  onClose: () => void
}

export function ConfirmDialog({ open, title, description, confirmLabel = '确认', danger, onConfirm, onClose }: ConfirmDialogProps) {
  if (!open) return null
  return (
    <div className="modal-layer" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
        <div className={`dialog__icon ${danger ? 'dialog__icon--danger' : ''}`}><AlertTriangle size={21} /></div>
        <div className="dialog__copy">
          <h2 id="confirm-title">{title}</h2>
          <p>{description}</p>
        </div>
        <button className="icon-button dialog__close" type="button" onClick={onClose}><X size={17} /></button>
        <div className="dialog__actions">
          <button className="button button--ghost" type="button" onClick={onClose}>取消</button>
          <button className={`button ${danger ? 'button--danger' : 'button--primary'}`} type="button" onClick={onConfirm}>{confirmLabel}</button>
        </div>
      </div>
    </div>
  )
}
