import { AlertCircle, CheckCircle2, FileSpreadsheet, Upload, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useAppStore, type RequirementImportResult } from '../store'

interface RequirementImportModalProps { open: boolean; onClose: () => void; onImported: (count: number) => void }

export function RequirementImportModal({ open, onClose, onImported }: RequirementImportModalProps) {
  const { importRequirements } = useAppStore()
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<RequirementImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => { if (open) { setFile(null); setPreview(null); setError(''); setBusy(false) } }, [open])
  if (!open) return null

  async function previewFile(next: File) {
    setFile(next); setPreview(null); setError('')
    if (!/\.(xlsx|csv)$/i.test(next.name)) { setError('仅支持 .xlsx 和 .csv 文件'); return }
    setBusy(true)
    try { setPreview(await importRequirements(next, false)) }
    catch (reason) { setError(reason instanceof Error ? reason.message : '文件预检失败') }
    finally { setBusy(false) }
  }

  async function commit() {
    if (!file || !preview || preview.invalidRows > 0) return
    setBusy(true); setError('')
    try { const result = await importRequirements(file, true); onImported(result.importedRows) }
    catch (reason) { setError(reason instanceof Error ? reason.message : '批量导入失败') }
    finally { setBusy(false) }
  }

  return <div className="modal-layer" onMouseDown={(event) => event.target === event.currentTarget && !busy && onClose()}><div className="create-modal import-modal" role="dialog" aria-modal="true" aria-labelledby="import-title">
    <header><div><span className="eyebrow">BATCH IMPORT</span><h2 id="import-title">批量导入需求</h2></div><button className="icon-button" type="button" aria-label="关闭批量导入" disabled={busy} onClick={onClose}><X size={18}/></button></header>
    <div className="create-modal__body">
      <label className={`import-dropzone ${busy ? 'import-dropzone--busy' : ''}`}><FileSpreadsheet size={28}/><strong>{file?.name ?? '选择 Excel 或 CSV 文件'}</strong><span>格式参考当前需求导出表格；单次最多 500 条、10MB</span><input type="file" accept=".xlsx,.csv" disabled={busy} onChange={(event) => { const next = event.target.files?.[0]; if (next) void previewFile(next) }}/></label>
      <div className="import-note"><AlertCircle size={15}/><span>导入时会重新生成需求编号并忽略“父需求”列，所有新需求均为独立需求；整批校验通过后才会创建。</span></div>
      {busy && <div className="import-status">正在{preview ? '创建需求' : '预检文件'}…</div>}
      {error && <div className="form-error" role="alert">{error}</div>}
      {preview && <><div className={`import-summary ${preview.invalidRows ? 'import-summary--error' : 'import-summary--ok'}`}>{preview.invalidRows ? <AlertCircle size={18}/> : <CheckCircle2 size={18}/>}<div><strong>{preview.invalidRows ? `发现 ${preview.invalidRows} 行错误` : `${preview.validRows} 条需求可以导入`}</strong><span>共 {preview.totalRows} 行 · 有效 {preview.validRows} 行 · 无效 {preview.invalidRows} 行</span></div></div><div className="import-preview"><table><thead><tr><th>行</th><th>标题</th><th>类型</th><th>状态</th><th>处理人</th><th>模块</th><th>结果</th></tr></thead><tbody>{preview.rows.slice(0, 100).map((row) => <tr key={row.rowNumber} className={row.valid ? '' : 'import-row--error'}><td>{row.rowNumber}</td><td title={row.title}>{row.title || '—'}</td><td>{row.requirementType || '默认'}</td><td>{row.status}</td><td>{row.assignee || '未分配'}</td><td>{row.module}</td><td>{row.valid ? <span className="binding-ok">通过</span> : <span className="binding-missing" title={row.errors.join('；')}>{row.errors.join('；')}</span>}</td></tr>)}</tbody></table>{preview.rows.length > 100 && <small>仅展示前 100 行，提交时仍会处理全部 {preview.rows.length} 行。</small>}</div></>}
    </div>
    <footer><span>支持 UTF-8 CSV 和 Excel 2007+（.xlsx）</span><div><button className="button button--ghost" type="button" disabled={busy} onClick={onClose}>取消</button><button className="button button--primary" type="button" disabled={busy || !preview || preview.invalidRows > 0} onClick={() => void commit()}><Upload size={16}/>{busy ? '导入中…' : `确认导入${preview?.validRows ? ` ${preview.validRows} 条` : ''}`}</button></div></footer>
  </div></div>
}
