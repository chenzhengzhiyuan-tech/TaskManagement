import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { RequirementsPage } from '../pages/RequirementsPage'
import { BoardPage } from '../pages/BoardPage'

vi.mock('../store', () => ({ useAppStore: vi.fn() }))
vi.mock('../components/RequirementImportModal', () => ({ RequirementImportModal: () => null }))
beforeEach(() => {
  sessionStorage.clear()
  vi.mocked(useAppStore).mockReturnValue({ ...initialData, currentUser: initialData.users[0], mode: 'mock', queryRequirementTree: vi.fn() } as unknown as ReturnType<typeof useAppStore>)
})

it('需求与看板双向保留优先级和我的任务，清空筛选也同步', () => {
  const view = render(<RequirementsPage initialQuery="" onOpenRequirement={() => {}} />)
  fireEvent.click(screen.getByLabelText('优先级筛选'))
  fireEvent.click(screen.getByRole('checkbox', { name: '高' }))
  fireEvent.click(screen.getByRole('checkbox', { name: '极高' }))
  view.rerender(<BoardPage onOpenRequirement={() => {}} />)
  expect(screen.getByLabelText('优先级筛选')).toHaveTextContent('(2)')
  fireEvent.click(screen.getByRole('button', { name: '我的任务' }))
  expect(screen.getByLabelText('优先级筛选')).toHaveTextContent('全部优先级')
  expect(screen.getByRole('button', { name: '我的任务' })).toHaveAttribute('aria-pressed', 'true')
  view.rerender(<RequirementsPage initialQuery="" onOpenRequirement={() => {}} />)
  expect(screen.getByLabelText('处理人筛选')).toHaveTextContent('全部处理人')
  expect(screen.getByRole('button', { name: '我的任务' })).toHaveAttribute('aria-pressed', 'true')
  fireEvent.click(screen.getByRole('button', { name: '我的任务' }))
  expect(screen.getByRole('button', { name: '我的任务' })).toHaveAttribute('aria-pressed', 'false')
  view.rerender(<BoardPage onOpenRequirement={() => {}} />)
  expect(screen.getByRole('button', { name: '我的任务' })).toHaveAttribute('aria-pressed', 'false')
  fireEvent.click(screen.getByRole('button', { name: '我的任务' }))
  expect(screen.getByRole('button', { name: '我的任务' })).toHaveAttribute('aria-pressed', 'true')
  fireEvent.click(screen.getByRole('button', { name: '我的任务' }))
  expect(screen.getByRole('button', { name: '我的任务' })).toHaveAttribute('aria-pressed', 'false')
  fireEvent.click(screen.getByRole('button', { name: '清空筛选' }))
  view.rerender(<RequirementsPage initialQuery="" onOpenRequirement={() => {}} />)
  expect(screen.getByLabelText('优先级筛选')).toHaveTextContent('全部优先级')
  expect(screen.getByLabelText('处理人筛选')).toHaveTextContent('全部处理人')
})
