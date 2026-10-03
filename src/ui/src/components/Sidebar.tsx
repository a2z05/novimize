import { NavLink } from 'react-router-dom'
import {
  LayoutDashboard,
  ScanSearch,
  UserCog,
  Camera,
  Gamepad2,
  PackagePlus,
  Palette,
  ShieldOff,
  Ban,
  Network,
  Activity,
  Settings,
} from 'lucide-react'

const navItems = [
  { to: '/', icon: LayoutDashboard, label: 'Dashboard' },
  { to: '/scan', icon: ScanSearch, label: 'Scan & Optimize' },
  { to: '/profiles', icon: UserCog, label: 'Profiles' },
  { to: '/gaming', icon: Gamepad2, label: 'Gaming' },
  { to: '/install', icon: PackagePlus, label: 'Install Apps' },
  { to: '/appearance', icon: Palette, label: 'Appearance' },
  { to: '/snapshots', icon: Camera, label: 'Snapshots' },
  { to: '/exclusions', icon: ShieldOff, label: 'Exclusions' },
  { to: '/blocker', icon: Ban, label: 'Blocker' },
  { to: '/network', icon: Network, label: 'Network' },
  { to: '/diagnostics', icon: Activity, label: 'Diagnostics' },
  { to: '/settings', icon: Settings, label: 'Settings' },
]

function NovimizeLogo({ size = 36 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 200 200" fill="none" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="logoGrad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#00D4FF" />
          <stop offset="100%" stopColor="#2563EB" />
        </linearGradient>
        <linearGradient id="darkOverlay" x1="50%" y1="0%" x2="50%" y2="100%">
          <stop offset="0%" stopColor="rgba(0,0,0,0)" />
          <stop offset="100%" stopColor="rgba(0,0,0,0.35)" />
        </linearGradient>
      </defs>
      <circle cx="100" cy="100" r="100" fill="#0A0E1A" />
      <path
        d="M 55 150 L 55 50 L 90 50 L 135 115 L 135 50 L 155 50 L 155 150 L 120 150 L 75 85 L 75 150 Z"
        fill="url(#logoGrad)"
      />
      <path
        d="M 55 150 L 55 50 L 90 50 L 135 115 L 135 50 L 155 50 L 155 150 L 120 150 L 75 85 L 75 150 Z"
        fill="url(#darkOverlay)"
      />
    </svg>
  )
}

export default function Sidebar() {
  return (
    <aside className="w-56 flex-shrink-0 flex flex-col glass-strong" style={{ borderRadius: 0 }}>
      {/* Logo */}
      <div className="px-4 py-4 border-b border-[var(--color-glass-border)]">
        <div className="flex items-center gap-2.5">
          <div className="w-9 h-9 rounded-xl flex items-center justify-center overflow-hidden"
               style={{ background: '#0A0E1A', border: '1px solid rgba(0,212,255,0.2)' }}>
            <NovimizeLogo size={32} />
          </div>
          <div>
            <span className="text-lg font-bold tracking-tight" style={{
              background: 'linear-gradient(135deg, #00D4FF, #2563EB)',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
              backgroundClip: 'text',
            }}>Novimize</span>
            <span className="text-[9px] text-[var(--color-text-muted)] block -mt-0.5 tracking-wider uppercase">v0.1.0</span>
          </div>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 py-3 px-2 space-y-0.5">
        {navItems.map(({ to, icon: Icon, label }) => (
          <SidebarLink key={to} to={to} Icon={Icon} label={label} />
        ))}
      </nav>

      {/* Footer */}
      <div className="px-4 py-3 border-t border-[var(--color-glass-border)]">
        <div className="flex items-center gap-2 mb-2">
          <div className="w-2 h-2 rounded-full bg-[var(--color-success)]"
               style={{ boxShadow: '0 0 6px rgba(34,197,94,0.5)' }} />
          <span className="text-[11px] text-[var(--color-text-muted)]">System ready</span>
        </div>
        <div className="text-[10px] text-[var(--color-text-muted)] opacity-50">
          Built by <span style={{
            background: 'linear-gradient(135deg, #00D4FF, #2563EB)',
            WebkitBackgroundClip: 'text',
            WebkitTextFillColor: 'transparent',
            fontWeight: 700,
          }}>a2z</span>
        </div>
      </div>
    </aside>
  )
}

function SidebarLink({ to, Icon, label }: { to: string; Icon: any; label: string }) {
  return (
    <NavLink
      to={to}
      end={to === '/'}
      className={({ isActive }) =>
        `group relative flex items-center gap-3 px-3 py-2.5 rounded-lg text-[13px] font-medium transition-all duration-200 ${
          isActive
            ? 'bg-[rgba(99,102,241,0.12)] text-[var(--color-primary)] shadow-[inset_0_0_20px_rgba(99,102,241,0.06)]'
            : 'text-[var(--color-text-muted)] hover:text-[var(--color-text)] hover:bg-[rgba(255,255,255,0.04)]'
        }`
      }
    >
      {({ isActive }) => (
        <span className="flex items-center gap-3 w-full">
          {isActive && (
            <span className="absolute left-0 top-1/2 -translate-y-1/2 w-[3px] h-5 rounded-r-full transition-all duration-300"
                  style={{ background: 'linear-gradient(180deg, #6366F1, #A855F7)', boxShadow: '0 0 8px rgba(99,102,241,0.4)' }} />
          )}
          <Icon size={18} className={`transition-all duration-200 ${isActive ? 'drop-shadow-[0_0_6px_rgba(99,102,241,0.4)]' : 'group-hover:scale-110 group-hover:drop-shadow-[0_0_4px_rgba(99,102,241,0.2)]'}`} />
          <span className="transition-all duration-200">{label}</span>
          {isActive && (
            <span className="ml-auto w-1.5 h-1.5 rounded-full animate-pulse"
                  style={{ background: 'linear-gradient(135deg, #6366F1, #A855F7)', boxShadow: '0 0 8px rgba(99,102,241,0.5)' }} />
          )}
        </span>
      )}
    </NavLink>
  )
}
