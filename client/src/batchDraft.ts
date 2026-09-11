export const columns = [ ['title','标题'], ['description','描述'], ['assignee','处理人'], ['reviewer','验收人'], ['priority','优先级'], ['module','模块'], ['type','需求类型'], ['due','期望完成日期'], ['iteration','迭代'], ['status','初始状态'] ] as const
export type BatchField = typeof columns[number][0]
export type BatchRow = Record<BatchField, string> & { key: string }
// Excel clipboard TSV: quoted cells may contain tabs, line breaks and escaped quotes.
export function parseClipboard(text: string): string[][] {
  const rows: string[][] = []; let row: string[] = []; let cell = ''; let quoted = false
  const input = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  for (let i = 0; i < input.length; i++) {
    const c = input[i]
    if (c === '"' && (quoted || cell === '')) { if (quoted && input[i + 1] === '"') { cell += '"'; i++ } else quoted = !quoted }
    else if (!quoted && (c === '\t' || c === '\n')) { row.push(cell); cell = ''; if (c === '\n') { rows.push(row); row = [] } }
    else cell += c
  }
  if (quoted) throw new Error('粘贴内容引号不完整，请检查表格')
  row.push(cell); rows.push(row)
  return rows.filter(row => row.some(cell => cell.trim()))
}
