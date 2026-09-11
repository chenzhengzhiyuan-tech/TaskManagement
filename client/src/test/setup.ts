import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

afterEach(() => cleanup())
Object.defineProperty(URL, 'createObjectURL', { value: vi.fn(() => 'blob:test') })
Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn() })
