import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { useAppStore } from '../store'
import { NewRequirementModal } from '../components/NewRequirementModal'

vi.mock('../store', () => ({ useAppStore: vi.fn() }))

it('附件失败重试不会重复创建需求，也不会重传成功图片', async () => {
  const create = vi.fn().mockResolvedValue('REQ-test')
  const upload = vi.fn().mockResolvedValueOnce({}).mockRejectedValueOnce(new Error('network')).mockResolvedValue({})
  vi.mocked(useAppStore).mockReturnValue({ ...initialData, currentUser: initialData.users[0], createRequirement: create, uploadAttachment: upload } as unknown as ReturnType<typeof useAppStore>)
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test')
  const created = vi.fn()
  const { container } = render(<NewRequirementModal open onClose={() => {}} onCreated={created} />)
  fireEvent.change(screen.getByPlaceholderText('用一句话清晰描述需求'), { target: { value: '附件测试' } })
  fireEvent.change(screen.getByPlaceholderText('输入需求背景、目标、范围和验收说明…'), { target: { value: '测试描述' } })
  fireEvent.change(screen.getByText('需求单类型', { exact: false }).closest('label')!.querySelector('select')!, { target: { value: initialData.requirementTypes[0].id } })
  fireEvent.change(container.querySelector('input[type="file"]')!, { target: { files: [new File(['a'], 'a.png', { type: 'image/png' }), new File(['b'], 'b.png', { type: 'image/png' })] } })
  fireEvent.click(screen.getByRole('button', { name: '创建需求' }))
  await screen.findByText(/部分附件上传失败/)
  fireEvent.click(screen.getByRole('button', { name: '重试附件上传' }))
  await waitFor(() => expect(created).toHaveBeenCalledWith('REQ-test'))
  expect(create).toHaveBeenCalledTimes(1)
  expect(upload).toHaveBeenCalledTimes(3)
})
