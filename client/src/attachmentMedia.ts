const types: Record<string, string> = { jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp', mp4: 'video/mp4', webm: 'video/webm' }
export const attachmentAccept = '.jpg,.jpeg,.png,.gif,.webp,.mp4,.webm'
export const attachmentHint = '支持 JPG、PNG、WebP、GIF、MP4、WebM；视频最大 100 MB，GIF 最大 20 MB，其他图片最大 500 MB。'
export const isVideo = (type: string) => type === 'video/mp4' || type === 'video/webm'
export function attachmentType(file: Pick<File, 'name' | 'type'>) {
  const expected = types[file.name.split('.').pop()?.toLowerCase() ?? '']
  // Browsers may leave the MIME type empty for a known file extension.
  return expected && (!file.type || file.type === 'application/octet-stream' || file.type.toLowerCase() === expected) ? expected : ''
}
export function attachmentError(file: Pick<File, 'name' | 'type' | 'size'>) {
  const type = attachmentType(file)
  if (!type) return '仅支持 JPG、PNG、WebP、GIF、MP4、WebM，文件扩展名与类型需一致'
  const limit = isVideo(type) ? 100 : type === 'image/gif' ? 20 : 500
  return file.size <= 0 ? '不能上传空文件' : file.size > limit * 1024 * 1024 ? `该文件最大允许 ${limit} MB` : ''
}
