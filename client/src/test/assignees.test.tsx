import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { assigneeIds, assigneeMatches } from '../assignees'
import { isMyTask } from '../components/RequirementFilters'
import { AssigneePicker } from '../components/AssigneePicker'
import { initialData } from '../data'

it('兼容旧处理人并识别第二位处理人，空列表明确取消归属', () => {
  expect(assigneeIds({ assigneeId: 'a' })).toEqual(['a'])
  expect(assigneeIds({ assigneeId: 'a', assigneeIds: [] })).toEqual([])
  const item = { assigneeId: 'a', assigneeIds: ['a', 'b'], statusId: 'todo', reviewerId: 'c' }
  expect(isMyTask(item, 'b')).toBe(true)
  expect(isMyTask(item, 'c')).toBe(false)
  expect(assigneeMatches('b,d', item)).toBe(true)
  expect(assigneeMatches('c', item)).toBe(false)
})

it('选择第二人保留第一人且点击外部关闭', async () => {
  const users = initialData.users.slice(0, 2); const change = vi.fn()
  render(<AssigneePicker users={users} value={[users[0].id]} onChange={change} />)
  fireEvent.click(screen.getByRole('button', { name: '处理人' }))
  fireEvent.click(screen.getAllByRole('checkbox')[1])
  await waitFor(() => expect(change).toHaveBeenCalledWith(users.map(user => user.id)))
  fireEvent.pointerDown(document.body)
  expect(screen.queryByRole('group')).not.toBeInTheDocument()
})
