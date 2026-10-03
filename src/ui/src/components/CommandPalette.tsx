import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { invokeJson } from '../hooks/useTauri'
import {
  Activity,
  ArrowRight,
  Ban,
  Camera,
  Clock,
  FileText,
  Gamepad2,
  Globe,
  Loader2,
  Package,
  Palette,
  RefreshCw,
  ScanSearch,
  Search,
  Settings,
  ShieldOff,
  Timer,
  UserCog,
  Wrench,
  Zap,
} from 'lucide-react'

/**
 * Ctrl+K over everything the app knows about.
 *
 * Two rules shape what a result is allowed to do. A page navigates, because
 * finding "telemetry" should take you somewhere you can read before you
 * commit — nothing here applies a tweak. An action only runs when it is
 * harmless and self-contained, which in practice means flushing a cache or
 * opening a settings page. Everything else says where it lives instead of
 * pretending to do the work.
 */
type Group = 'Pages' | 'Actions' | 'Apps' | 'Tweaks'

interface Command {
  id: string
  label: string
  hint: string
  group: Group
  keywords: string
  icon: typeof Zap
  run: () => void
}

const PAGES: Omit<Command, 'run'>[] = [
  { id: 'page:dashboard', label: 'Dashboard', hint: 'Overview', group: 'Pages', keywords: 'home overview start', icon: Zap },
  { id: 'page:scan', label: 'Scan & Optimize', hint: 'Tweaks', group: 'Pages', keywords: 'tweaks optimize apply registry', icon: ScanSearch },
  { id: 'page:profiles', label: 'Profiles', hint: 'Presets', group: 'Pages', keywords: 'preset plan gaming silent', icon: UserCog },
  { id: 'page:gaming', label: 'Gaming Center', hint: 'Game mode', group: 'Pages', keywords: 'game mode fps exclusions', icon: Gamepad2 },
  { id: 'page:install', label: 'Install Apps', hint: 'winget catalogue', group: 'Pages', keywords: 'apps install uninstall winget packages', icon: Package },
  { id: 'page:appearance', label: 'Appearance', hint: 'Customization tools', group: 'Pages', keywords: 'theme wallpaper rainmeter customization', icon: Palette },
  { id: 'page:snapshots', label: 'Snapshots', hint: 'Before and after', group: 'Pages', keywords: 'rollback undo history changes', icon: Camera },
  { id: 'page:blocker', label: 'Blocker', hint: 'Hosts, firewall, blocklists', group: 'Pages', keywords: 'block ads trackers hosts firewall blocklist', icon: Ban },
  { id: 'page:network', label: 'Network', hint: 'DNS and tools', group: 'Pages', keywords: 'dns flush resolver ping traceroute adapter', icon: Globe },
  { id: 'page:power', label: 'Power Center', hint: 'Plans and settings', group: 'Pages', keywords: 'power plan battery performance sleep', icon: Zap },
  { id: 'page:startup', label: 'Startup', hint: 'Runs at sign-in', group: 'Pages', keywords: 'startup boot autostart enabled disabled', icon: Clock },
  { id: 'page:services', label: 'Services', hint: 'Windows services', group: 'Pages', keywords: 'service start stop manual automatic', icon: Settings },
  { id: 'page:tasks', label: 'Scheduled Tasks', hint: 'Triggers and runs', group: 'Pages', keywords: 'task scheduler trigger enable disable', icon: Timer },
  { id: 'page:debloat', label: 'Debloat', hint: 'Store packages', group: 'Pages', keywords: 'appx remove package store debloat', icon: FileText },
  { id: 'page:maintenance', label: 'Maintenance', hint: 'Caches and repairs', group: 'Pages', keywords: 'sfc dism temp recycle thumbnail disk cleanup', icon: Wrench },
  { id: 'page:updates', label: 'Windows Update', hint: 'Updates and restarts', group: 'Pages', keywords: 'update kb patch reboot restart', icon: RefreshCw },
  { id: 'page:exclusions', label: 'Defender Exclusions', hint: 'Not scanned', group: 'Pages', keywords: 'defender exclude folder antivirus', icon: ShieldOff },
  { id: 'page:diagnostics', label: 'Diagnostics', hint: 'Health report', group: 'Pages', keywords: 'health report activation doctor benchmark', icon: Activity },
  { id: 'page:settings', label: 'Settings', hint: 'App settings', group: 'Pages', keywords: 'preferences options', icon: Settings },
]

/** Actions that are safe to run from a palette: they change no configuration. */
const ACTIONS: Omit<Command, 'run'>[] = [
  { id: 'act:flush-dns', label: 'Flush DNS cache', hint: 'Runs now', group: 'Actions', keywords: 'dns flush resolver clear cache', icon: Globe },
  { id: 'act:open-update', label: 'Open Windows Update', hint: 'Opens Settings', group: 'Actions', keywords: 'windows update settings patch', icon: RefreshCw },
  { id: 'act:health', label: 'Run the health report', hint: 'Writes JSON, text or HTML', group: 'Actions', keywords: 'health report export json html', icon: Activity },
]

interface LazyIndex {
  tweaks: { id: string; name: string; category: string }[]
  apps: { id: string; name: string; category: string }[]
}

export default function CommandPalette() {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [loading, setLoading] = useState(false)
  const [lazy, setLazy] = useState<LazyIndex | null>(null)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()

  /**
   * Tweaks and the app catalogue are fetched the first time the palette is
   * opened and then kept — a shortcut that costs a network round trip on
   * every keystroke is a shortcut nobody will use twice.
   */
  const loadLazy = useCallback(async () => {
    if (lazy || loading) return
    setLoading(true)
    setError(null)
    try {
      const [tweaks, apps] = await Promise.all([
        invokeJson<{ id: string; name: string; category: string }[]>('list_tweaks', {}).catch(
          () => [],
        ),
        invokeJson<{ categories: { id: string; label: string }[]; apps: { id: string; name: string; category: string; description: string | null }[] }>(
          'apps_catalog',
        ).catch(() => ({ categories: [], apps: [] })),
      ])
      setLazy({
        tweaks: (tweaks || []).map(t => ({ id: t.id, name: t.name, category: t.category })),
        apps: (apps.apps || []).map(a => ({ id: a.id, name: a.name, category: a.category })),
      })
    } catch (err) {
      setError(typeof err === 'string' ? err : (err as Error).message)
    } finally {
      setLoading(false)
    }
  }, [lazy, loading])

  const run = useCallback((cmd: Command) => {
    setOpen(false)
    setQuery('')
    cmd.run()
  }, [])

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        setOpen(o => !o)
        return
      }
      if (e.key === 'Escape') setOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  useEffect(() => {
    if (open) {
      setActive(0)
      void loadLazy()
      // Focus after the dialog has painted, or the caret lands nowhere.
      setTimeout(() => inputRef.current?.focus(), 0)
    }
  }, [open, loadLazy])

  const commands = useMemo<Command[]>(() => {
    const pages: Command[] = PAGES.map(p => ({
      ...p,
      run: () => navigate(p.id.replace('page:', '/')),
    }))
    const actions: Command[] = ACTIONS.map(a => ({
      ...a,
      run: () => {
        if (a.id === 'act:flush-dns') {
          void invokeJson('dns_action', { action: 'flush' }).catch(err =>
            setError(typeof err === 'string' ? err : (err as Error).message),
          )
        } else if (a.id === 'act:open-update') {
          void invokeJson('update_action', { action: 'open' }).catch(() => undefined)
        } else {
          navigate('/diagnostics')
        }
      },
    }))
    const apps: Command[] = (lazy?.apps ?? []).map(a => ({
      id: `app:${a.id}`,
      label: a.name,
      hint: `Install Apps · ${a.category}`,
      group: 'Apps' as const,
      keywords: `${a.id} ${a.category}`,
      icon: Package,
      run: () => navigate(`/install?q=${encodeURIComponent(a.name)}`),
    }))
    const tweaks: Command[] = (lazy?.tweaks ?? []).map(t => ({
      id: `tweak:${t.id}`,
      label: t.name,
      hint: `Tweak · ${t.category}`,
      group: 'Tweaks' as const,
      keywords: `${t.id} ${t.category}`,
      icon: ScanSearch,
      // Navigates with the term rather than applying: the palette finds
      // things, it does not change the machine.
      run: () => navigate(`/scan?q=${encodeURIComponent(t.name)}`),
    }))
    return [...pages, ...actions, ...apps, ...tweaks]
  }, [lazy, navigate])

  const results = useMemo(() => {
    const q = query.trim().toLowerCase()
    const scored = commands
      .map(c => {
        if (!q) return { c, score: c.group === 'Pages' ? 2 : c.group === 'Actions' ? 1 : 0 }
        const hay = `${c.label} ${c.keywords} ${c.hint}`.toLowerCase()
        if (hay === q) return { c, score: 100 }
        if (c.label.toLowerCase().startsWith(q)) return { c, score: 60 }
        if (c.label.toLowerCase().includes(q)) return { c, score: 40 }
        if (hay.includes(q)) return { c, score: 20 }
        return null
      })
      .filter((x): x is { c: Command; score: number } => x !== null)
      .sort((a, b) => b.score - a.score || a.c.label.localeCompare(b.c.label))

    return scored.slice(0, 40).map(s => s.c)
  }, [commands, query])

  useEffect(() => {
    setActive(0)
  }, [query])

  if (!open) {
    return (
      <button
        className="btn btn-ghost btn-sm fixed bottom-4 right-4 z-40 opacity-60 hover:opacity-100"
        onClick={() => setOpen(true)}
        title="Search everything (Ctrl+K)"
      >
        <Search size={13} />
        Ctrl K
      </button>
    )
  }

  return (
    <div className="modal-backdrop" onClick={() => setOpen(false)}>
      <div
        className="modal-content animate-scale-in !max-w-xl"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center gap-2 border-b border-[var(--color-border)] pb-2 mb-2">
          <Search size={16} className="text-[var(--color-text-muted)]" />
          <input
            ref={inputRef}
            className="flex-1 bg-transparent text-[15px] outline-none py-1"
            placeholder="Search tweaks, apps, pages and actions…"
            value={query}
            onChange={e => setQuery(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'ArrowDown') {
                e.preventDefault()
                setActive(a => Math.min(a + 1, results.length - 1))
              } else if (e.key === 'ArrowUp') {
                e.preventDefault()
                setActive(a => Math.max(a - 1, 0))
              } else if (e.key === 'Enter' && results[active]) {
                e.preventDefault()
                run(results[active])
              }
            }}
          />
          {loading && <Loader2 size={14} className="animate-spin text-[var(--color-text-muted)]" />}
          <button
            className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] text-[11px]"
            onClick={() => setOpen(false)}
          >
            Esc
          </button>
        </div>

        {error && (
          <div className="text-[12px] text-[var(--color-danger)] pb-2">{error}</div>
        )}

        <div className="max-h-[24rem] overflow-y-auto -mx-1">
          {results.map((cmd, i) => {
            const Icon = cmd.icon
            return (
              <button
                key={cmd.id}
                onMouseEnter={() => setActive(i)}
                onClick={() => run(cmd)}
                className={`w-full flex items-center gap-3 px-2 py-2 rounded-lg text-left transition-colors ${
                  i === active ? 'bg-[rgba(99,102,241,0.16)]' : ''
                }`}
              >
                <Icon size={15} className="text-[var(--color-text-muted)] flex-shrink-0" />
                <span className="text-[13px] flex-1 min-w-0 truncate">{cmd.label}</span>
                <span className="text-[11px] text-[var(--color-text-muted)] flex-shrink-0 truncate max-w-[45%]">
                  {cmd.hint}
                </span>
                <span className="text-[10px] uppercase tracking-wider text-[var(--color-text-muted)] opacity-70 flex-shrink-0">
                  {cmd.group}
                </span>
                {i === active && <ArrowRight size={13} className="text-[var(--color-primary)]" />}
              </button>
            )
          })}

          {results.length === 0 && !loading && (
            <div className="text-[13px] text-[var(--color-text-muted)] py-6 text-center">
              Nothing matches “{query}”.
            </div>
          )}
        </div>

        <div className="text-[11px] text-[var(--color-text-muted)] pt-2 border-t border-[var(--color-border)]">
          ↑↓ to move · Enter to open · Esc to close. Tweaks and apps are found here and opened in
          their own section — nothing is applied from this box.
        </div>
      </div>
    </div>
  )
}
