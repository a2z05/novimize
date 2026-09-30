import { Minus, Square, X } from 'lucide-react'
import { useState } from 'react'

export default function TitleBar() {
  async function getWindow() {
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    return getCurrentWindow()
  }

  async function handleMinimize() {
    const win = await getWindow()
    win.minimize()
  }

  async function handleMaximize() {
    const win = await getWindow()
    win.toggleMaximize()
  }

  async function handleClose() {
    const win = await getWindow()
    win.close()
  }

  return (
    <div
      data-tauri-drag-region
      className="h-10 flex items-center justify-between px-3 select-none"
      style={{
        background: 'linear-gradient(180deg, rgba(15,17,28,1) 0%, rgba(10,12,20,1) 100%)',
        borderBottom: '1px solid rgba(255,255,255,0.05)',
        WebkitAppRegion: 'drag',
      } as any}
    >
      {/* Left: App icon + name */}
      <div className="flex items-center gap-2.5" style={{ WebkitAppRegion: 'no-drag' } as any}>
        <div
          className="w-5 h-5 rounded-[4px] flex items-center justify-center"
          style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.12), rgba(37,99,235,0.12))' }}
        >
          <svg width="12" height="12" viewBox="0 0 32 32" fill="none">
            <path d="M10 8h3l5 10V8h3v16h-3l-5-10v10h-3V8z" fill="url(#tb-grad)"/>
            <defs>
              <linearGradient id="tb-grad" x1="10" y1="8" x2="22" y2="24" gradientUnits="userSpaceOnUse">
                <stop stopColor="#00D4FF"/>
                <stop offset="1" stopColor="#2563EB"/>
              </linearGradient>
            </defs>
          </svg>
        </div>
        <span className="text-[12px] font-semibold tracking-tight"
              style={{ background: 'linear-gradient(135deg, #00D4FF, #2563EB)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>
          Novimize
        </span>
      </div>

      {/* Right: Window controls */}
      <div className="flex items-center gap-0.5" style={{ WebkitAppRegion: 'no-drag' } as any}>
        <WindowBtn onClick={handleMinimize} label="Minimize">
          <Minus size={13} strokeWidth={2} />
        </WindowBtn>
        <WindowBtn onClick={handleMaximize} label="Maximize">
          <Square size={10} strokeWidth={2} />
        </WindowBtn>
        <WindowBtn onClick={handleClose} label="Close" isClose>
          <X size={13} strokeWidth={2} />
        </WindowBtn>
      </div>
    </div>
  )
}

function WindowBtn({ onClick, label, children, isClose = false }: {
  onClick: () => void; label: string; children: React.ReactNode; isClose?: boolean
}) {
  const [hovered, setHovered] = useState(false)

  return (
    <button
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      className="w-9 h-7 flex items-center justify-center rounded-[4px] transition-all duration-150"
      style={{
        background: isClose && hovered
          ? 'rgba(239,68,68,0.9)'
          : hovered
            ? 'rgba(255,255,255,0.08)'
            : 'transparent',
        color: isClose && hovered
          ? 'white'
          : hovered
            ? 'rgba(255,255,255,0.9)'
            : 'rgba(136,136,160,0.7)',
      }}
      aria-label={label}
    >
      {children}
    </button>
  )
}
