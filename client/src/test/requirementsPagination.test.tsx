import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { RequirementsPage } from '../pages/RequirementsPage'

vi.mock('../store', () => ({ useAppStore: vi.fn() }))
vi.mock('../components/RequirementImportModal', () => ({ RequirementImportModal: () => null }))

beforeEach(() => {
  vi.mocked(useAppStore).mockReturnValue({
    ...initialData,
    currentUser: initialData.users[0],
    mode: 'mock',
    requirements: Array.from({ length: 51 }, (_, i) => ({
      ...initialData.requirements[0], id: `PAGE-${i + 1}`, title: `分页任务 ${i + 1}`, parentId: null,
    })),
    queryRequirementTree: vi.fn().mockResolvedValue({ groups: [], rootCount: 0, requirementCount: 0 }),
  } as unknown as ReturnType<typeof useAppStore>)
})

it('每页显示50个顶层需求，剩余需求放在第二页', () => {
  const { container } = render(<RequirementsPage initialQuery="" onOpenRequirement={() => {}} />)
  expect(container.querySelectorAll('tbody tr')).toHaveLength(50)
  expect(screen.getByText(/第 1\/2 页/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /^2$/ }))
  expect(container.querySelectorAll('tbody tr')).toHaveLength(1)
  expect(screen.getByText('分页任务 51')).toBeInTheDocument()
})

it('服务端查询也使用每页50条', async () => {
  const store = useAppStore()
  vi.mocked(useAppStore).mockReturnValue({ ...store, mode: 'api' })
  render(<RequirementsPage initialQuery="" onOpenRequirement={() => {}} />)
  await waitFor(() => expect(store.queryRequirementTree).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 50, sort: 'priority' })))
})
