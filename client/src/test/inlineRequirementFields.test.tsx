import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { InlineRequirementFields } from '../components/InlineRequirementFields'
vi.mock('../store', () => ({ useAppStore: vi.fn() }))
const update = vi.fn()
const item = { ...initialData.requirements[0], priority: 'low' as const, module: '通用', dueDate: '2026-09-11' }
beforeEach(() => {
  update.mockReset().mockResolvedValue(true)
  vi.mocked(useAppStore).mockReturnValue({ modules: ['通用', 'UI'], updateRequirement: update, lastOperationError: '' } as unknown as ReturnType<typeof useAppStore>)
})
it('选择极高和模块使用正确字段并保存', async () => {
  render(<><InlineRequirementFields item={item} field="priority" /><InlineRequirementFields item={item} field="module" /></>)
  fireEvent.change(screen.getByLabelText(`${item.id} 优先级`), { target: { value: 'urgent' } })
  await waitFor(() => expect(update).toHaveBeenCalledWith(item.id, { priority: 'urgent' }, '更新了优先级'))
  fireEvent.change(screen.getByLabelText(`${item.id} 模块`), { target: { value: 'UI' } })
  await waitFor(() => expect(update).toHaveBeenCalledWith(item.id, { module: 'UI' }, '更新了模块'))
})
it('日期编辑后失焦保存，允许清空为null', async () => {
  render(<InlineRequirementFields item={item} field="dueDate" />)
  const input = screen.getByLabelText(`${item.id} 期望完成日期`)
  fireEvent.change(input, { target: { value: '2026-09-18' } })
  expect(update).not.toHaveBeenCalled()
  fireEvent.blur(input)
  await waitFor(() => expect(input).toBeEnabled())
  expect(update).toHaveBeenCalledWith(item.id, { dueDate: '2026-09-18' }, '更新了期望完成日期')
  fireEvent.change(input, { target: { value: '' } }); fireEvent.blur(input)
  await waitFor(() => expect(update).toHaveBeenCalledWith(item.id, { dueDate: null }, '更新了期望完成日期'))
})
it('保存失败恢复原值并提示，允许重试', async () => {
  update.mockResolvedValue(false)
  render(<InlineRequirementFields item={item} field="priority" />)
  const select = screen.getByLabelText(`${item.id} 优先级`)
  fireEvent.change(select, { target: { value: 'high' } })
  await waitFor(() => expect(select).toHaveValue('low'))
  expect(screen.getByRole('alert')).toHaveTextContent('保存失败')
  expect(select).toBeEnabled()
})
