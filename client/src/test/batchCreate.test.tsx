import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { BatchCreateModal } from '../components/BatchCreateModal'

vi.mock('../store', () => ({ useAppStore: vi.fn() }))
it('批量草稿校验、粘贴、覆盖保护和恢复', () => {
  localStorage.clear()
  vi.mocked(useAppStore).mockReturnValue({ ...initialData, currentUser: initialData.users[0], mode: 'api', refresh: vi.fn() } as unknown as ReturnType<typeof useAppStore>)
  const props = { onClose: vi.fn(), onOpen: vi.fn() }
  const view = render(<BatchCreateModal {...props} />)
  expect(screen.getByRole('button', { name: '检查并创建' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('粘贴批量需求'), { target: { value: '标题\t描述\n第一条\t内容一\n第二条\t内容二' } })
  fireEvent.click(screen.getByText('识别列'))
  fireEvent.click(screen.getByText('追加到表格'))
  expect(screen.getByLabelText('第2行标题')).toHaveValue('第二条')
  expect(screen.getByRole('button', { name: '检查并创建' })).toBeEnabled()
  fireEvent.click(screen.getByLabelText('选择全部草稿行'))
  fireEvent.change(screen.getByLabelText('统一设置字段'), { target: { value: 'priority' } })
  fireEvent.change(screen.getByLabelText('统一设置值'), { target: { value: '极高' } })
  fireEvent.click(screen.getByText('应用到选中行'))
  expect(screen.getByLabelText('第1行优先级')).toHaveValue('中')
  fireEvent.click(screen.getByLabelText('覆盖已有值'))
  fireEvent.click(screen.getByText('应用到选中行'))
  expect(screen.getByLabelText('第1行优先级')).toHaveValue('极高')
  view.unmount()
  render(<BatchCreateModal {...props} />)
  expect(screen.getByLabelText('第2行标题')).toHaveValue('第二条')
})
