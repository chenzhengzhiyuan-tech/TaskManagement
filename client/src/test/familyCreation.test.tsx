import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { AssigneePicker } from '../components/AssigneePicker'
import { NewRequirementModal } from '../components/NewRequirementModal'
import { ExistingChildren } from '../components/NewChildren'
vi.mock('../store', () => ({ useAppStore: vi.fn() }))
beforeEach(() => vi.mocked(useAppStore).mockReturnValue({...structuredClone(initialData), currentUser: initialData.users[0], mode: 'mock', createRequirement: vi.fn(), uploadAttachment: vi.fn()} as unknown as ReturnType<typeof useAppStore>))
it('member substring search ignores case, preserves selections, and supports keyboard single selection', async () => {
  const users = [{...initialData.users[0], id: 'a', name: 'Alice', account: 'ALPHA'}, {...initialData.users[1], id: 'b', name: 'Bob', account: 'bravo'}]
  const onChange = vi.fn()
  render(<AssigneePicker users={users} value={['b']} onChange={onChange} single label="验收人" />)
  fireEvent.click(screen.getByRole('button', {name: '验收人'}))
  fireEvent.change(screen.getByLabelText('搜索验收人'), {target: {value: 'l'}})
  expect(screen.queryByText('bravo')).not.toBeInTheDocument()
  expect(screen.getByText('ALPHA')).toBeInTheDocument()
  fireEvent.keyDown(screen.getByLabelText('搜索验收人'), {key: 'Enter'})
  await waitFor(() => expect(onChange).toHaveBeenCalledWith(['a']))
})
it('bound requirements remain visible but disabled', () => {
  const store = useAppStore(); const item = {...store.requirements[0], id: 'REQ-BOUND', title: '已绑定的测试', parentId: 'REQ-PARENT'}
  vi.mocked(useAppStore).mockReturnValue({...store, requirements: [item]})
  render(<ExistingChildren selected={[]} onChange={vi.fn()} />)
  fireEvent.focus(screen.getByLabelText('搜索已有子需求'))
  expect(screen.getByRole('checkbox')).toBeDisabled()
  expect(screen.getByText('该需求已有父需求')).toBeInTheDocument()
})
it('child rows inherit once, validate before submission, and retry the same family id', async () => {
  const create = vi.fn().mockRejectedValue(new Error('网络暂时断开'))
  vi.mocked(useAppStore).mockReturnValue({...useAppStore(), createRequirement: create})
  render(<NewRequirementModal open onClose={vi.fn()} onCreated={vi.fn()} />)
  fireEvent.change(screen.getByPlaceholderText('用一句话清晰描述需求'), {target: {value: '父需求'}})
  fireEvent.change(screen.getByPlaceholderText('输入需求背景、目标、范围和验收说明…'), {target: {value: '父描述'}})
  fireEvent.change(screen.getByLabelText('需求单类型 *'), {target: {value: initialData.requirementTypes.find(x => x.enabled)!.id}})
  fireEvent.click(screen.getByRole('button', {name: '新增子需求一行'}))
  fireEvent.click(screen.getByRole('button', {name: '创建需求'}))
  expect(create).not.toHaveBeenCalled()
  fireEvent.change(screen.getByLabelText('子需求 1 标题'), {target: {value: '子一'}})
  fireEvent.change(screen.getByLabelText('子需求 1 描述'), {target: {value: '子描述'}})
  const row = screen.getByRole('region', {name: '新子需求 1'})
  const priority = within(row).getByLabelText('优先级')
  const inherited = (priority as HTMLSelectElement).value
  fireEvent.change(screen.getAllByLabelText('优先级')[0], {target: {value: inherited === 'urgent' ? 'low' : 'urgent'}})
  expect(priority).toHaveValue(inherited)
  fireEvent.click(screen.getByRole('button', {name: '创建需求'}))
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('网络暂时断开'))
  fireEvent.click(screen.getByRole('button', {name: '创建需求'}))
  await waitFor(() => expect(create).toHaveBeenCalledTimes(2))
  expect(create.mock.calls[0][1].requestId).toBe(create.mock.calls[1][1].requestId)
  expect(create.mock.calls[0][1].children[0].title).toBe('子一')
})

it('dropdown opens upward near the bottom edge without inheriting top:100%', () => {
  const rect = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({top: window.innerHeight - 40, bottom: window.innerHeight - 20, left: window.innerWidth - 100, right: window.innerWidth, width: 100, height: 20} as DOMRect)
  try {
    render(<AssigneePicker users={initialData.users} value={[]} onChange={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', {name: '处理人'}))
    const menu = screen.getByRole('group')
    expect(menu.style.top).toBe('auto')
    expect(Number.parseFloat(menu.style.left) + Number.parseFloat(menu.style.width)).toBeLessThanOrEqual(window.innerWidth - 8)
  } finally { rect.mockRestore() }
})
