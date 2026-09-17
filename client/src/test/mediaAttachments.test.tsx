import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { attachmentError, attachmentType } from '../attachmentMedia'
import { DraftImages, type DraftImage } from '../components/DraftImages'
import { MediaContent } from '../components/MediaContent'

it('enforces media limits including exact boundaries and empty MIME fallback', () => {
  for (const [name, type, mb] of [['sample.MP4', 'video/mp4', 100], ['sample.webm', 'video/webm', 100], ['sample.gif', 'image/gif', 20]] as const) {
    expect(attachmentError({ name, type, size: mb * 1024 * 1024 })).toBe('')
    expect(attachmentError({ name, type, size: mb * 1024 * 1024 + 1 })).toContain(`${mb} MB`)
    expect(attachmentType({ name, type: '' })).toBe(type)
    expect(attachmentError({ name, type, size: 0 })).toContain('空文件')
  }
  expect(attachmentError({ name: 'sample.mov', type: 'video/quicktime', size: 10 })).toContain('仅支持')
  expect(attachmentError({ name: 'sample.mp4', type: 'image/png', size: 10 })).toContain('类型需一致')
})

it('previews draft videos in a controlled player and removes drafts', () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:video')
  const revoke = vi.spyOn(URL, 'revokeObjectURL')
  function Draft() { const [items, setItems] = useState<DraftImage[]>([]); return <DraftImages items={items} setItems={setItems} disabled={false} /> }
  const { container } = render(<Draft />)
  fireEvent.change(container.querySelector('input[type=file]')!, { target: { files: [new File(['test'], 'sample.mp4', { type: 'video/mp4' })] } })
  expect(container.querySelector('video')).toBeNull()
  fireEvent.click(screen.getByText('点击预览视频'))
  const video = container.querySelector('video')!
  expect(video.controls).toBe(true); expect(video.autoplay).toBe(false)
  fireEvent.click(video)
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  fireEvent.click(screen.getByText('关闭预览'))
  fireEvent.click(screen.getByRole('button', { name: '删除' }))
  expect(screen.queryByText('sample.mp4')).not.toBeInTheDocument()
  expect(revoke).toHaveBeenCalledWith('blob:video')
})

it('keeps GIF animated via its original image URL and explains unsupported codecs', () => {
  const view = render(<MediaContent url="blob:gif" type="image/gif" name="sample.gif" />)
  expect(screen.getByRole('img')).toHaveAttribute('src', 'blob:gif')
  view.rerender(<MediaContent key="video" url="blob:video" type="video/webm" name="sample.webm" />)
  fireEvent.error(view.container.querySelector('video')!)
  expect(screen.getByRole('alert')).toHaveTextContent('本地播放器')
})

