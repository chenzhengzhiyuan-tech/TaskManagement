import { act, fireEvent, render, screen, within, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { BoardPage } from '../pages/BoardPage'
import { RequirementsPage } from '../pages/RequirementsPage'
import { CommentsPanel } from '../components/CommentsPanel'
import { adjustMentions } from '../commentMentions'
import { compareTaskStatus } from '../requirementSort'
import App from '../App'

vi.mock('../store', () => ({ useAppStore: vi.fn() }))
vi.mock('../components/RequirementImportModal', () => ({ RequirementImportModal: () => null }))

beforeEach(() => {
  sessionStorage.clear(); window.history.replaceState({}, '', '/')
  vi.mocked(useAppStore).mockReturnValue({ ...structuredClone(initialData), currentUser: initialData.users[0], mode: 'mock',
    authenticated: true, initializing: false, queryRequirementTree: vi.fn(), createRequirement: vi.fn().mockResolvedValue('REQ-0048'),
    addComment: vi.fn().mockResolvedValue(undefined), uploadAttachment: vi.fn().mockResolvedValue({ id: 'image-1' }),
  } as unknown as ReturnType<typeof useAppStore>)
})

it('默认不筛选迭代，全选再排除，并保留手动清空选择', () => {
  const view = render(<BoardPage onOpenRequirement={() => {}} />)
  fireEvent.click(screen.getByLabelText('迭代筛选'))
  expect(screen.getByRole('checkbox', { name: '20260824-20260828' })).not.toBeChecked()
  fireEvent.click(within(screen.getByRole('group', { name: '迭代选项' })).getByRole('button', { name: '全选' }))
  expect(screen.getAllByRole('checkbox').every(box => (box as HTMLInputElement).checked)).toBe(true)
  fireEvent.click(screen.getByRole('checkbox', { name: '20260824-20260828' }))
  view.rerender(<RequirementsPage initialQuery="" onOpenRequirement={() => {}} />)
  expect(screen.getByLabelText('迭代筛选')).toHaveTextContent('(2)')
  fireEvent.click(screen.getByRole('button', { name: '清空筛选' }))
  view.rerender(<BoardPage onOpenRequirement={() => {}} />)
  expect(screen.getByLabelText('迭代筛选')).toHaveTextContent('全部迭代')
})

it('手动选择迭代后保持用户选择', () => {
  const store = useAppStore()
  const view = render(<BoardPage onOpenRequirement={() => {}} />)
  fireEvent.click(screen.getByLabelText('迭代筛选'))
  fireEvent.click(screen.getByRole('checkbox', { name: '20260817-20260821' }))
  const iterations = store.iterations.map(item => ({ ...item, state: (item.id === 'it-next' ? 'active' : 'completed') as 'active' | 'completed' }))
  vi.mocked(useAppStore).mockReturnValue({ ...store, iterations })
  view.rerender(<BoardPage onOpenRequirement={() => {}} />)
  expect(screen.getByRole('checkbox', { name: '20260824-20260828' })).not.toBeChecked()
  expect(screen.getByRole('checkbox', { name: '20260831-20260904' })).not.toBeChecked()
})

it('看板先按优先级再按数字单号，状态排序遵循业务顺序', () => {
  const store = useAppStore(), item = store.requirements[0]
  const requirements = [
    { ...item, id: 'REQ-10000', priority: 'high' as const, statusId: 'todo' },
    { ...item, id: 'REQ-0003', priority: 'urgent' as const, statusId: 'todo' },
    { ...item, id: 'REQ-9999', priority: 'high' as const, statusId: 'todo' },
  ]
  vi.mocked(useAppStore).mockReturnValue({ ...store, requirements })
  const view = render(<BoardPage onOpenRequirement={() => {}} />)
  expect([...view.container.querySelectorAll('.kanban-card .mono-id')].map(node => node.textContent)).toEqual(['REQ-0003', 'REQ-9999', 'REQ-10000'])
  const ids = ['closed', 'completed', 'backlog', 'paused', 'review', 'in_progress', 'todo']
  expect(ids.map(statusId => ({ ...item, statusId })).sort(compareTaskStatus).map(entry => entry.statusId)).toEqual([...ids].reverse())
})

it('看板新建后保留页面，成功提示三秒关闭且描述完整展示', async () => {
  vi.useFakeTimers()
  try {
    render(<App />)
    fireEvent.click(screen.getByRole('button', { name: '看板' }))
    fireEvent.click(screen.getByRole('button', { name: '新建需求' }))
    const dialog = within(screen.getByRole('dialog', { name: '新建需求' }))
    fireEvent.change(dialog.getByPlaceholderText('用一句话清晰描述需求'), { target: { value: '测试新建' } })
    fireEvent.change(dialog.getByPlaceholderText('输入需求背景、目标、范围和验收说明…'), { target: { value: '测试描述' } })
    await act(async () => { fireEvent.click(dialog.getByRole('button', { name: '创建需求' })) })
    expect(screen.getByRole('heading', { name: '看板' })).toBeInTheDocument()
    expect(screen.getByLabelText('REQ-0048 需求详情')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('创建成功')
    await act(async () => { await vi.advanceTimersByTimeAsync(3000) })
    expect(screen.queryByText(/创建成功/)).not.toBeInTheDocument()
    expect(screen.getByLabelText('REQ-0048 需求详情')).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: '需求描述' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '编辑描述' }))
    expect(screen.getByRole('textbox', { name: '需求描述' })).toBeInTheDocument()
  } finally { vi.useRealTimers() }
})

it('评论只选已绑定成员，回车选择成员，Ctrl+Enter提交多行文本', async () => {
  const store = useAppStore()
  const member = { ...store.users[1], wecomBound: true }
  vi.mocked(useAppStore).mockReturnValue({ ...store, users: [{ ...store.users[0], wecomBound: false }, member] })
  render(<CommentsPanel requirement={{ ...store.requirements[0], comments: [] }} visible onDirtyChange={() => {}} onOpenImage={() => {}} onDownloadImage={() => {}} />)
  const input = screen.getByRole('textbox')
  fireEvent.change(input, { target: { value: '@', selectionStart: 1 } })
  expect(screen.getAllByRole('option')).toHaveLength(1)
  expect(screen.getByRole('option')).toHaveTextContent(member.name)
  fireEvent.keyDown(input, { key: 'Enter' })
  const label = `@${member.name}`
  fireEvent.change(input, { target: { value: `${label} 请看\n第二行`, selectionStart: label.length + 7 } })
  fireEvent.keyDown(input, { key: 'Enter', ctrlKey: true })
  await waitFor(() => expect(store.addComment).toHaveBeenCalledWith(store.requirements[0].id, `${label} 请看\n第二行`, expect.objectContaining({ mentions: [{ userId: member.id, name: member.name, start: 0, length: label.length }] })))
  expect(input).toHaveValue('')
})

it('图片评论提交失败保留草稿，重试复用图片和提交标识', async () => {
  const store = useAppStore()
  vi.mocked(store.addComment).mockRejectedValueOnce(new Error('连接中断')).mockResolvedValue(undefined)
  render(<CommentsPanel requirement={{ ...store.requirements[0], comments: [] }} visible onDirtyChange={() => {}} onOpenImage={() => {}} onDownloadImage={() => {}} />)
  fireEvent.change(screen.getByLabelText('评论图片'), { target: { files: [new File(['test'], 'test.png', { type: 'image/png' })] } })
  fireEvent.click(screen.getByRole('button', { name: '发送' }))
  await screen.findByRole('alert')
  fireEvent.click(screen.getByRole('button', { name: '发送' }))
  await waitFor(() => expect(store.addComment).toHaveBeenCalledTimes(2))
  expect(store.uploadAttachment).toHaveBeenCalledTimes(1)
  expect(vi.mocked(store.addComment).mock.calls[0][2]?.requestId).toBe(vi.mocked(store.addComment).mock.calls[1][2]?.requestId)
})

it('修改或删除@标签后不会再提醒该成员，前面的文本编辑会移动位置', () => {
  const mention = { userId: 'a', name: '张三', start: 2, length: 3 }
  expect(adjustMentions('请 @张三 看', '请 @张四 看', [mention])).toEqual([])
  expect(adjustMentions('请 @张三 看', '麻烦请 @张三 看', [mention])[0].start).toBe(4)
})

it('@没有匹配成员时回车保持正常换行，不被空下拉拦截', () => {
  const store = useAppStore()
  render(<CommentsPanel requirement={{ ...store.requirements[0], comments: [] }} visible onDirtyChange={() => {}} onOpenImage={() => {}} onDownloadImage={() => {}} />)
  const input = screen.getByRole('textbox')
  fireEvent.change(input, { target: { value: '@没有匹配的成员', selectionStart: 8 } })
  expect(screen.getByText('没有匹配的已绑定成员')).toBeInTheDocument()
  expect(fireEvent.keyDown(input, { key: 'Enter' })).toBe(true)
  expect(store.addComment).not.toHaveBeenCalled()
})
