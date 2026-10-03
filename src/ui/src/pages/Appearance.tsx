import { useCallback, useEffect, useMemo, useState } from 'react'
import { invoke } from '@tauri-apps/api/core'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  Code,
  Download,
  Globe,
  LayoutGrid,
  Loader2,
  Palette,
  Play,
  RefreshCw,
  Search,
  Trash2,
  Upload,
  X,
} from 'lucide-react'
import type {
  AppCatalogue,
  AppChange,
  AppEntry,
  AppStatus,
  AppStatusResult,
  LaunchResult,
  WinGetInfo,
} from '../types'

function errMsg(err: unknown): string {
  // §29: never a bare "something went wrong". The string branch is the
  // common one — the CLI already names the action in what it says — and the
  // other two have to say what happened and where it happened.
  if (typeof err === 'string') return err
  if (err instanceof Error) return `${err.name}: ${err.message}`
  return 'Cause: the command returned a result this version cannot read. Affected component: this page.'
}

/**
 * A stable colour for a tool, derived from its name.
 *
 * The catalogue ships no artwork: it does not bundle third-party logos, and a
 * scraped icon is somebody else's asset served from somewhere nobody checked.
 * A monogram takes the same slot, is generated from data already on disk, and
 * never goes stale when a tool renames itself.
 */
function hueOf(name: string): number {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360
  return h
}

function monogram(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '?'
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase()
  return (words[0][0] + words[1][0]).toUpperCase()
}

/** One line of the card's facts. Absent data is not printed as a placeholder. */
function Fact({ k, v, href }: { k: string; v?: string | null; href?: string | null }) {
  if (!v) return null
  return (
    <div className="flex items-start gap-2 min-w-0 text-[11px]">
      <span className="text-[var(--color-text-muted)] flex-shrink-0 w-20">{k}</span>
      {href ? (
        <button
          className="text-[var(--color-primary)] hover:underline truncate min-w-0 text-left"
          onClick={() => void openLink(href)}
          title={href}
        >
          {v}
        </button>
      ) : (
        <span className="break-words min-w-0">{v}</span>
      )}
    </div>
  )
}

async function openLink(url: string) {
  try {
    await invoke('open_external', { url })
  } catch {
    /* the button below the card repeats the offer; nothing to report here */
  }
}

interface Notice {
  id: string
  ok: boolean
  text: string
}

export default function Appearance() {
  const [catalogue, setCatalogue] = useState<AppCatalogue | null>(null)
  const [status, setStatus] = useState<AppStatusResult | null>(null)
  const [winget, setWinget] = useState<WinGetInfo | null>(null)

  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  const [query, setQuery] = useState('')
  const [group, setGroup] = useState<string>('all')
  const [installedOnly, setInstalledOnly] = useState(false)

  const load = useCallback(async () => {
    const [cat, info] = await Promise.all([
      invokeJson<AppCatalogue>('apps_catalog'),
      invokeJson<WinGetInfo>('apps_probe'),
    ])
    setCatalogue(cat)
    setWinget(info)
    setStatus(await invokeJson<AppStatusResult>('apps_status'))
  }, [])

  useEffect(() => {
    void load().catch(err => setError(errMsg(err)))
  }, [load])

  /**
   * Everything on this page is one catalogue category. The rest of the app
   * reads it too — the Install Apps tab shows it as a category — so this page
   * is a different lens on the same data rather than a second copy of it.
   */
  const tools = useMemo(
    () => (catalogue ? catalogue.apps.filter(a => a.category === 'customization') : []),
    [catalogue],
  )

  const state = useMemo(() => {
    const map = new Map<string, AppStatus>()
    for (const s of status?.apps ?? []) map.set(s.id, s)
    return map
  }, [status])

  const groups = useMemo(() => {
    const seen: string[] = []
    for (const t of tools) {
      const key = t.subcategory ?? 'Other'
      if (!seen.includes(key)) seen.push(key)
    }
    return seen
  }, [tools])

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return tools.filter(t => {
      if (group !== 'all' && (t.subcategory ?? 'Other') !== group) return false
      const here = state.get(t.id)
      if (installedOnly && !here?.installed) return false
      if (!q) return true
      return (
        t.name.toLowerCase().includes(q) ||
        (t.publisher ?? '').toLowerCase().includes(q) ||
        (t.description ?? '').toLowerCase().includes(q) ||
        t.tags.some(tag => tag.toLowerCase().includes(q))
      )
    })
  }, [tools, state, group, installedOnly, query])

  const installedCount = useMemo(
    () => tools.filter(t => state.get(t.id)?.installed).length,
    [tools, state],
  )

  const wingetReady = winget?.available === true

  const run = useCallback(async (key: string, fn: () => Promise<void>) => {
    setBusy(key)
    setError(null)
    setNotice(null)
    try {
      await fn()
    } catch (err) {
      setError(errMsg(err))
    } finally {
      setBusy(null)
    }
  }, [])

  const refreshStatus = useCallback(async () => {
    setStatus(await invokeJson<AppStatusResult>('apps_status'))
  }, [])

  const launch = useCallback(
    (tool: AppEntry) =>
      run(`launch:${tool.id}`, async () => {
        const res = await invokeJson<LaunchResult>('apps_launch', { appId: tool.id })
        setNotice({ id: tool.id, ok: res.success, text: res.message })
      }),
    [run],
  )

  const change = useCallback(
    (tool: AppEntry, action: 'install' | 'uninstall' | 'upgrade') =>
      run(`${action}:${tool.id}`, async () => {
        const res = await invokeJson<AppChange>('apps_change', {
          action,
          appId: tool.id,
        })
        setNotice({ id: tool.id, ok: res.success || res.unchanged, text: res.message })
        if (res.needsElevation && !res.success) {
          setError('That one needs administrator rights. Use Install Apps to retry through UAC.')
        }
        await refreshStatus()
      }),
    [run, refreshStatus],
  )

  const openPage = (url: string) => {
    setNotice(null)
    void invoke('open_external', { url }).catch(err => setError(errMsg(err)))
  }

  if (!catalogue) {
    return (
      <div className="space-y-5 animate-fade-in">
        <h1 className="text-2xl font-bold flex items-center gap-2">
          <Palette size={24} className="text-[var(--color-primary)]" /> Appearance
        </h1>
        <div className="card text-sm text-[var(--color-text-muted)] flex items-center gap-2">
          <Loader2 size={14} className="animate-spin" /> Reading the catalogue…
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <Palette size={24} className="text-[var(--color-primary)]" />
            Appearance
          </h1>
          <p className="text-sm text-[var(--color-text-muted)] mt-1">
            {tools.length} tools for how Windows looks and behaves. Installed and launched from here;
            never bundled with Novimize. Tools without a winget package link to their official site,
            because that is the only source there is.
          </p>
        </div>
        <button className="btn btn-secondary" onClick={() => void run('load', load)} disabled={busy !== null}>
          {busy === 'load' ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
          Refresh
        </button>
      </div>

      {winget && !wingetReady && (
        <div className="card border border-[var(--color-warning)]/40">
          <div className="flex items-start gap-3 text-sm">
            <AlertTriangle size={18} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
            <div>
              <div className="font-medium">winget is not available here, so nothing can be installed.</div>
              <div className="text-[var(--color-text-muted)] mt-1">{winget.error}</div>
              <div className="text-[var(--color-text-muted)] mt-1">
                The official-site links below still work — those tools can be fetched by hand.
              </div>
            </div>
          </div>
        </div>
      )}

      {error && (
        <div className="card border border-[var(--color-danger)]/40 bg-[rgba(239,68,68,0.06)] flex items-start justify-between gap-3">
          <div className="text-sm text-[var(--color-danger)] break-words">{error}</div>
          <button className="btn btn-ghost btn-sm" onClick={() => setError(null)}>
            <X size={14} />
          </button>
        </div>
      )}

      {notice && (
        <div
          className={`card border flex items-start justify-between gap-3 ${
            notice.ok
              ? 'border-[var(--color-success)]/40 bg-[rgba(16,185,129,0.06)]'
              : 'border-[var(--color-danger)]/40 bg-[rgba(239,68,68,0.06)]'
          }`}
        >
          <div className="text-sm min-w-0">
            <span className={notice.ok ? 'text-[var(--color-success)]' : 'text-[var(--color-danger)]'}>
              {notice.ok ? 'Done. ' : 'Not done. '}
            </span>
            <span className="text-[var(--color-text-muted)] break-words">{notice.text}</span>
          </div>
          <button className="btn btn-ghost btn-sm" onClick={() => setNotice(null)}>
            <X size={14} />
          </button>
        </div>
      )}

      {/* Filter */}
      <div className="card">
        <div className="flex items-center gap-2 flex-wrap">
          <div className="relative flex-1 min-w-52">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]" />
            <input
              className="w-full bg-[rgba(255,255,255,0.04)] border border-[var(--color-glass-border)] rounded-lg pl-8 pr-3 py-2 text-sm"
              placeholder="Filter by name, publisher or tag"
              value={query}
              onChange={e => setQuery(e.target.value)}
            />
          </div>
          <button
            className={`btn btn-sm ${installedOnly ? 'btn-primary' : 'btn-secondary'}`}
            onClick={() => setInstalledOnly(v => !v)}
          >
            Installed only ({installedCount}/{tools.length})
          </button>
        </div>

        <div className="flex items-center gap-1.5 flex-wrap mt-3 text-[11px]">
          <LayoutGrid size={12} className="text-[var(--color-text-muted)]" />
          {[{ id: 'all', label: 'All' }, ...groups.map(g => ({ id: g, label: g }))].map(chip => (
            <button
              key={chip.id}
              className={`px-2.5 py-1 rounded-full border transition-colors ${
                group === chip.id
                  ? 'border-[var(--color-primary)] text-[var(--color-primary)] bg-[rgba(99,102,241,0.12)]'
                  : 'border-[var(--color-glass-border)] text-[var(--color-text-muted)] hover:text-[var(--color-text)]'
              }`}
              onClick={() => setGroup(chip.id)}
            >
              {chip.label}
            </button>
          ))}
        </div>
      </div>

      {visible.length === 0 && (
        <div className="card text-sm text-[var(--color-text-muted)]">
          Nothing matches{query ? ` “${query}”` : ''}. Clear the filter to see all {tools.length} tools.
        </div>
      )}

      {groups
        .filter(g => (group === 'all' || group === g) && visible.some(t => (t.subcategory ?? 'Other') === g))
        .map(g => (
          <div key={g} className="space-y-3">
            <h2 className="text-sm font-semibold text-[var(--color-text-muted)] uppercase tracking-wide">
              {g}
              <span className="ml-2 font-normal normal-case tracking-normal">
                {visible.filter(t => (t.subcategory ?? 'Other') === g).length}
              </span>
            </h2>

            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {visible
                .filter(t => (t.subcategory ?? 'Other') === g)
                .map(tool => {
                  const here = state.get(tool.id)
                  const hue = hueOf(tool.name)
                  const keyBusy = busy?.endsWith(`:${tool.id}`)
                  const source = tool.winget ? `winget · ${tool.id}` : 'official site only'

                  return (
                    <div key={tool.id} className="card flex flex-col gap-3">
                      <div className="flex items-start gap-3">
                        <div
                          className="w-10 h-10 rounded-lg flex items-center justify-center text-[13px] font-bold text-white flex-shrink-0"
                          style={{
                            background: `linear-gradient(135deg, hsl(${hue} 62% 46%), hsl(${(hue + 42) % 360} 62% 32%))`,
                          }}
                          aria-hidden
                        >
                          {monogram(tool.name)}
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2 flex-wrap">
                            <span className="font-medium text-sm truncate">{tool.name}</span>
                            {here?.installed && <span className="badge badge-safe">installed</span>}
                            {here?.updateAvailable && (
                              <span className="badge badge-recommended">
                                {here.installedVersion} → {here.availableVersion}
                              </span>
                            )}
                          </div>
                          <div className="text-[11px] text-[var(--color-text-muted)] truncate">
                            {tool.publisher ?? 'Publisher not stated'}
                          </div>
                        </div>
                      </div>

                      {tool.description && (
                        <p className="text-[12px] text-[var(--color-text-muted)] leading-relaxed">
                          {tool.description}
                        </p>
                      )}

                      <div className="space-y-1">
                        <Fact k="Source" v={source} />
                        <Fact k="Website" v={tool.homepage} href={tool.homepage} />
                        <Fact k="Source code" v={tool.github} href={tool.github} />
                        <Fact k="Licence" v={tool.license} />
                        <Fact k="Cost" v={tool.cost} />
                        <Fact k="Windows" v={tool.windows} />
                      </div>

                      {!tool.winget && (
                        <div className="text-[11px] text-[var(--color-text-muted)] border-t border-[var(--color-glass-border)] pt-2">
                          No winget package exists for this tool. Novimize will not fetch an installer
                          from anywhere else — open the official page and take it from there.
                        </div>
                      )}

                      <div className="flex items-center gap-2 flex-wrap mt-auto pt-1">
                        <button
                          className="btn btn-secondary btn-sm"
                          onClick={() => launch(tool)}
                          disabled={keyBusy || busy !== null}
                          title="Looks the tool up in the Start menu and opens what it finds."
                        >
                          {keyBusy ? <Loader2 size={13} className="animate-spin" /> : <Play size={13} />}
                          Launch
                        </button>

                        {tool.winget && !here?.installed && wingetReady && (
                          <button
                            className="btn btn-primary btn-sm"
                            onClick={() => change(tool, 'install')}
                            disabled={busy !== null}
                          >
                            {keyBusy ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
                            Install
                          </button>
                        )}

                        {tool.winget && here?.updateAvailable && (
                          <button
                            className="btn btn-primary btn-sm"
                            onClick={() => change(tool, 'upgrade')}
                            disabled={busy !== null}
                          >
                            {keyBusy ? <Loader2 size={13} className="animate-spin" /> : <Upload size={13} />}
                            Update
                          </button>
                        )}

                        {tool.winget && here?.installed && (
                          <button
                            className="btn btn-ghost btn-sm"
                            onClick={() => change(tool, 'uninstall')}
                            disabled={busy !== null}
                          >
                            {keyBusy ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                            Uninstall
                          </button>
                        )}

                        {tool.homepage && (
                          <button
                            className={`btn btn-sm ${tool.winget ? 'btn-ghost' : 'btn-secondary'}`}
                            onClick={() => openPage(tool.homepage!)}
                            title={tool.homepage}
                          >
                            <Globe size={13} /> {tool.winget ? 'Site' : 'Get it from the official site'}
                          </button>
                        )}

                        {tool.github && tool.github !== tool.homepage && (
                          <button className="btn btn-ghost btn-sm" onClick={() => openPage(tool.github!)} title={tool.github}>
                            <Code size={13} /> Code
                          </button>
                        )}
                      </div>
                    </div>
                  )
                })}
            </div>
          </div>
        ))}

      <div className="text-[11px] text-[var(--color-text-muted)]">
        {status?.deep === false
          ? 'Installed state comes from a quick winget pass. ' : ''}
        Launch resolves the name against the Start menu and reports the entry it opened, so a tool
        filed under a different name is not silently reported as started.
      </div>
    </div>
  )
}
