import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { invoke } from '@tauri-apps/api/core'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Download,
  ExternalLink,
  Info,
  Loader2,
  PackagePlus,
  RefreshCw,
  Search,
  ShieldCheck,
  Trash2,
  Upload,
  X,
  XCircle,
} from 'lucide-react'
import type {
  AppCatalogue,
  AppChange,
  AppSearchResponse,
  AppStatus,
  AppStatusResult,
  InstalledCheck,
  PackageDetail,
  WinGetInfo,
} from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

/** Install scope. Each one has a different cost, so the choice is explicit. */
const SCOPES = [
  {
    value: 'any' as const,
    label: 'Let winget decide',
    admin: false,
    note: 'Whatever the package manifest defaults to.',
  },
  {
    value: 'user' as const,
    label: 'Just me',
    admin: false,
    note: 'Current user only. No administrator rights.',
  },
  {
    value: 'machine' as const,
    label: 'Everyone',
    admin: true,
    note: 'All users. Administrator rights.',
  },
]

type Scope = (typeof SCOPES)[number]['value']
type Filter = 'all' | 'not-installed' | 'updates'
type QueueStatus = 'pending' | 'running' | 'done' | 'failed' | 'cancelled'
type QueueAction = 'install' | 'uninstall' | 'upgrade' | 'upgrade-all'

interface QueueItem {
  key: string
  id: string
  name: string
  action: QueueAction
  scope: Scope
  elevated: boolean
  status: QueueStatus
  result: AppChange | null
}

const QUEUE_LABEL: Record<QueueStatus, string> = {
  pending: 'Waiting',
  running: 'Installing…',
  done: 'Done',
  failed: 'Failed',
  cancelled: 'Cancelled',
}

function queueRunningLabel(item: QueueItem): string {
  if (item.status !== 'running') return QUEUE_LABEL[item.status]
  if (item.action === 'uninstall') return 'Uninstalling…'
  if (item.action.startsWith('upgrade')) return 'Upgrading…'
  return 'Installing…'
}

export default function Install() {
  const [catalogue, setCatalogue] = useState<AppCatalogue | null>(null)
  const [winget, setWinget] = useState<WinGetInfo | null>(null)
  const [status, setStatus] = useState<AppStatusResult | null>(null)
  const [deep, setDeep] = useState(false)

  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<AppChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const [category, setCategory] = useState<string>('all')
  const [filter, setFilter] = useState<Filter>('all')
  // Opened from the command palette: the app it found becomes the search.
  const [searchParams] = useSearchParams()
  const [query, setQuery] = useState(() => searchParams.get('q') ?? '')
  const [wingetSearch, setWingetSearch] = useState<AppSearchResponse | null>(null)
  const [searching, setSearching] = useState(false)

  const [detail, setDetail] = useState<AppStatus | null>(null)
  const [detailData, setDetailData] = useState<PackageDetail | null>(null)
  const [check, setCheck] = useState<InstalledCheck | null>(null)
  const [checking, setChecking] = useState(false)
  const [scope, setScope] = useState<Scope>('any')
  const [showLog, setShowLog] = useState<string | null>(null)

  const [queue, setQueue] = useState<QueueItem[]>([])

  // The queue is read inside a loop that spans many awaits. Keeping it in a ref
  // as the source of truth means the loop always sees the current list instead
  // of the one captured when it started.
  const queueRef = useRef<QueueItem[]>([])
  const cancelRef = useRef(false)
  const processingRef = useRef(false)
  const detailToken = useRef(0)

  const commit = useCallback((next: QueueItem[]) => {
    queueRef.current = next
    setQueue(next)
  }, [])

  const loadStatus = useCallback(async () => {
    const result = await invokeJson<AppStatusResult>('apps_status')
    setStatus(result)
    if (result.error) setError(result.error)
  }, [])

  /** The authoritative pass: every package Windows reports, not just winget's. */
  const loadDeep = useCallback(async () => {
    setDeep(true)
    setStatus(await invokeJson<AppStatusResult>('apps_status', { deep: true }))
  }, [])

  const load = useCallback(async () => {
    const [cat, info] = await Promise.all([
      invokeJson<AppCatalogue>('apps_catalog'),
      invokeJson<WinGetInfo>('apps_probe'),
    ])
    setCatalogue(cat)
    setWinget(info)
    if (info.available) await loadStatus()
  }, [loadStatus])

  const run = useCallback(async (key: string, fn: () => Promise<void>) => {
    setBusy(key)
    setError(null)
    try {
      await fn()
    } catch (err) {
      setError(errMsg(err))
    } finally {
      setBusy(null)
    }
  }, [])

  useEffect(() => {
    void run('load', load)
  }, [run, load])

  /**
   * Drain the queue one item at a time.
   *
   * winget has no streaming transport here, so "cancel" cannot mean "abort what
   * is running" — that would kill an installer half-way and leave the machine in
   * a state nobody can describe. It means "finish this one, then stop", and the
   * button says so.
   */
  const processQueue = useCallback(async () => {
    if (processingRef.current) return
    processingRef.current = true
    setBusy('queue')
    setError(null)
    try {
      for (;;) {
        const next = queueRef.current.find(i => i.status === 'pending')
        if (!next) break

        if (cancelRef.current) {
          commit(queueRef.current.map(i => (i.status === 'pending' ? { ...i, status: 'cancelled' } : i)))
          break
        }

        commit(queueRef.current.map(i => (i.key === next.key ? { ...i, status: 'running' } : i)))

        let changeResult: AppChange
        try {
          changeResult = await invokeJson<AppChange>('apps_change', {
            action: next.action,
            appId: next.id,
            scope: next.scope,
            elevated: next.elevated,
          })
        } catch (err) {
          changeResult = {
            action: next.action,
            id: next.id,
            success: false,
            unchanged: false,
            message: errMsg(err),
            exitCode: -1,
            log: '',
            scope: next.scope,
            needsElevation: false,
          }
        }

        const failed = !changeResult.success && !changeResult.unchanged
        commit(
          queueRef.current.map(i =>
            i.key === next.key ? { ...i, status: failed ? 'failed' : 'done', result: changeResult } : i,
          ),
        )
        setChange(changeResult)

        // Re-read after every item so the card the user just acted on stops
        // offering the button they have already pressed.
        try {
          await loadStatus()
        } catch {
          /* a stale list is not worth failing the queue over */
        }
      }
    } finally {
      processingRef.current = false
      setBusy(null)
    }
  }, [commit, loadStatus])

  const enqueue = useCallback(
    (app: { id: string; name: string }, action: QueueItem['action'], chosenScope: Scope) => {
      const key = `${action}:${app.id}`
      const existing = queueRef.current.find(i => i.key === key)
      if (existing && (existing.status === 'pending' || existing.status === 'running')) return
      cancelRef.current = false
      commit([
        ...queueRef.current.filter(i => i.key !== key),
        { key, id: app.id, name: app.name, action, scope: chosenScope, elevated: false, status: 'pending', result: null },
      ])
      void processQueue()
    },
    [commit, processQueue],
  )

  const cancelQueue = useCallback(() => {
    cancelRef.current = true
  }, [])

  const retryQueue = useCallback(() => {
    cancelRef.current = false
    commit(
      queueRef.current.map(i =>
        i.status === 'failed' || i.status === 'cancelled' ? { ...i, status: 'pending', result: null } : i,
      ),
    )
    void processQueue()
  }, [commit, processQueue])

  /** One failed item, retried through UAC after winget said it needed admin. */
  const retryElevated = useCallback(
    (item: QueueItem) => {
      commit(
        queueRef.current.map(i =>
          i.key === item.key ? { ...i, status: 'pending', elevated: true, result: null } : i,
        ),
      )
      void processQueue()
    },
    [commit, processQueue],
  )

  const clearFinished = useCallback(() => {
    commit(queueRef.current.filter(i => i.status === 'pending' || i.status === 'running'))
  }, [commit])

  const openDetail = useCallback(
    async (app: AppStatus) => {
      const token = ++detailToken.current
      setDetail(app)
      setDetailData(null)
      setCheck(null)
      setScope('any')
      setShowLog(null)
      setChecking(true)
      setError(null)
      try {
        const [data, installed] = await Promise.all([
          invokeJson<PackageDetail>('apps_show', { appId: app.id }),
          invokeJson<InstalledCheck>('apps_installed', { appId: app.id }),
        ])
        if (detailToken.current !== token) return
        setDetailData(data)
        setCheck(installed)
        if (data.error) setError(data.error)
      } catch (err) {
        if (detailToken.current === token) setError(errMsg(err))
      } finally {
        if (detailToken.current === token) setChecking(false)
      }
    },
    [],
  )

  const closeDetail = useCallback(() => {
    detailToken.current++
    setDetail(null)
    setDetailData(null)
    setCheck(null)
    setChecking(false)
  }, [])

  const searchWinget = useCallback(async () => {
    const term = query.trim()
    if (!term) return
    setSearching(true)
    setError(null)
    try {
      setWingetSearch(await invokeJson<AppSearchResponse>('apps_search', { query: term }))
    } catch (err) {
      setError(errMsg(err))
    } finally {
      setSearching(false)
    }
  }, [query])

  const openLink = useCallback(async (url: string) => {
    try {
      await invoke('open_external', { url })
    } catch (err) {
      setError(errMsg(err))
    }
  }, [])

  const upgradeAll = useCallback(() => {
    cancelRef.current = false
    const key = 'upgrade-all:*'
    commit([
      ...queueRef.current.filter(i => i.key !== key),
      {
        key,
        id: '*',
        name: 'Everything with an update',
        action: 'upgrade-all',
        scope: 'any',
        elevated: false,
        status: 'pending',
        result: null,
      },
    ])
    void processQueue()
  }, [commit, processQueue])

  const byId = useMemo(() => {
    const map = new Map<string, AppStatus>()
    for (const app of status?.apps ?? []) map.set(app.id, app)
    return map
  }, [status])

  const categories = catalogue?.categories ?? []
  const updatesAvailable = (status?.apps ?? []).filter(a => a.updateAvailable)
  const queueActive = queue.some(i => i.status === 'pending' || i.status === 'running')

  const visible = useMemo(() => {
    const term = query.trim().toLowerCase()
    return (status?.apps ?? [])
      .filter(app => (category === 'all' ? true : app.category === category))
      .filter(app => (filter === 'all' ? true : filter === 'updates' ? app.updateAvailable : !app.installed))
      .filter(app =>
        term.length === 0
          ? true
          : app.name.toLowerCase().includes(term) ||
            app.id.toLowerCase().includes(term) ||
            (app.publisher ?? '').toLowerCase().includes(term) ||
            app.tags.some(t => t.includes(term)),
      )
      .sort((a, b) => a.name.localeCompare(b.name))
  }, [status, category, filter, query])

  const wingetMissing = winget !== null && !winget.available

  return (
    <div className="space-y-5 animate-fade-in">
      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <PackagePlus size={24} className="text-[var(--color-primary)]" />
            Install Apps
          </h1>
          <p className="text-sm text-[var(--color-text-muted)] mt-1">
            Every install below is a winget command against the ID and source shown first. Nothing is
            downloaded from anywhere else.
          </p>
        </div>
        <button className="btn btn-secondary" onClick={() => void run('load', load)} disabled={busy !== null}>
          {busy === 'load' ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
          Refresh
        </button>
      </div>

      {/* winget availability */}
      {wingetMissing && (
        <div className="card border border-[var(--color-warning)]/40">
          <div className="flex items-start gap-3">
            <AlertTriangle size={18} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
            <div className="text-sm">
              <div className="font-medium">winget is not available on this machine.</div>
              <div className="text-[var(--color-text-muted)] mt-1">{winget?.error}</div>
            </div>
          </div>
        </div>
      )}

      {winget?.available && (
        <div className="flex flex-wrap items-center gap-2 text-[11px] text-[var(--color-text-muted)]">
          <span className="badge badge-safe">{winget.version}</span>
          {winget.sources.map(s => (
            <span key={s.name} className="badge badge-optional">
              {s.name}
              {s.explicit ? ' · explicit' : ''}
            </span>
          ))}
          <span className="ml-auto flex items-center gap-1">
            {deep ? 'Full scan (slow)' : 'Quick scan'}
            <button className="btn btn-ghost btn-sm" onClick={() => void run('deep', loadDeep)}>
              {busy === 'deep' ? <Loader2 size={12} className="animate-spin" /> : <RefreshCw size={12} />}
              Full check
            </button>
          </span>
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

      {change && !change.success && !change.unchanged && (
        <div className="card border border-[var(--color-danger)]/40 bg-[rgba(239,68,68,0.06)]">
          <div className="flex items-start justify-between gap-3">
            <div className="text-sm">
              <div className="text-[var(--color-danger)] font-medium">
                {change.action} {change.id} failed.
              </div>
              <div className="text-[var(--color-text-muted)] mt-1">{change.message}</div>
              {change.needsElevation && (
                <div className="text-[11px] text-[var(--color-warning)] mt-1">
                  Needs administrator rights — the retry button below will ask.
                </div>
              )}
            </div>
            <button className="btn btn-ghost btn-sm" onClick={() => setChange(null)}>
              <X size={14} />
            </button>
          </div>
        </div>
      )}

      {/* Queue */}
      {queue.length > 0 && (
        <div className="card">
          <div className="flex items-center justify-between gap-3 flex-wrap">
            <div className="text-sm font-medium flex items-center gap-2">
              Queue
              <span className="badge badge-optional">
                {queue.filter(i => i.status === 'done').length}/{queue.length} done
              </span>
            </div>
            <div className="flex items-center gap-2">
              <button
                className="btn btn-secondary btn-sm"
                onClick={cancelQueue}
                disabled={!queueActive}
                title="Stops after the item that is installing right now. An installer is never killed half-way."
              >
                <X size={13} /> Cancel after current
              </button>
              <button className="btn btn-secondary btn-sm" onClick={retryQueue} disabled={queueActive}>
                <RefreshCw size={13} /> Retry failed
              </button>
              <button className="btn btn-ghost btn-sm" onClick={clearFinished} disabled={queueActive}>
                <Trash2 size={13} /> Clear
              </button>
            </div>
          </div>

          <div className="mt-3 space-y-2">
            {queue.map(item => (
              <div key={item.key} className="flex items-start gap-2 py-1.5 border-b border-[var(--color-glass-border)] last:border-0">
                <span className="mt-0.5 flex-shrink-0">
                  {item.status === 'done' && <CheckCircle2 size={14} className="text-[var(--color-success)]" />}
                  {item.status === 'failed' && <XCircle size={14} className="text-[var(--color-danger)]" />}
                  {item.status === 'running' && <Loader2 size={14} className="animate-spin text-[var(--color-primary)]" />}
                  {(item.status === 'pending' || item.status === 'cancelled') && (
                    <AlertTriangle size={14} className="text-[var(--color-text-muted)]" />
                  )}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="text-[13px] flex items-center gap-2 flex-wrap">
                    <span className="truncate">{item.name}</span>
                    <span className="badge badge-optional">{item.action}</span>
                    {item.elevated && <span className="badge badge-experimental">admin</span>}
                    <span className="text-[11px] text-[var(--color-text-muted)]">{queueRunningLabel(item)}</span>
                  </div>
                  {item.result && (
                    <div className="text-[11px] text-[var(--color-text-muted)] break-words">
                      {item.result.unchanged ? 'Nothing to do — already in that state.' : item.result.message}
                    </div>
                  )}
                  {item.result?.needsElevation && item.status === 'failed' && (
                    <button className="btn btn-secondary btn-sm mt-1" onClick={() => retryElevated(item)}>
                      <ShieldCheck size={13} /> Retry as administrator
                    </button>
                  )}
                  {item.result && item.result.log && (
                    <div className="mt-1">
                      <button
                        className="text-[11px] text-[var(--color-primary)] flex items-center gap-1"
                        onClick={() => setShowLog(showLog === item.key ? null : item.key)}
                      >
                        {showLog === item.key ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                        Log
                      </button>
                      {showLog === item.key && (
                        <pre className="mt-1 text-[10px] whitespace-pre-wrap break-words bg-black/30 rounded p-2 max-h-48 overflow-y-auto">
                          {item.result.log || '(winget produced no output)'}
                        </pre>
                      )}
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Toolbar */}
      <div className="card">
        <div className="flex items-center gap-2 flex-wrap">
          <div className="relative flex-1 min-w-52">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]" />
            <input
              className="w-full bg-[rgba(255,255,255,0.04)] border border-[var(--color-glass-border)] rounded-lg pl-8 pr-3 py-2 text-sm"
              placeholder="Filter the catalogue, or search winget"
              value={query}
              onChange={e => setQuery(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && void searchWinget()}
            />
          </div>
          <button className="btn btn-secondary" onClick={() => void searchWinget()} disabled={searching || !query.trim()}>
            {searching ? <Loader2 size={14} className="animate-spin" /> : <Search size={14} />}
            Search winget
          </button>
          <button className="btn btn-primary" onClick={upgradeAll} disabled={busy !== null || updatesAvailable.length === 0}>
            <Download size={14} /> Update all ({updatesAvailable.length})
          </button>
        </div>

        <div className="flex items-center gap-2 mt-3 flex-wrap">
          {[{ id: 'all', label: 'All categories' }, ...categories].map(c => (
            <button
              key={c.id}
              className={`chip ${category === c.id ? 'border-[var(--color-primary)] text-[var(--color-primary)]' : ''}`}
              onClick={() => setCategory(c.id)}
            >
              {c.label}
            </button>
          ))}
        </div>
        <div className="flex items-center gap-2 mt-2 flex-wrap">
          {(
            [
              { id: 'all', label: 'Everything' },
              { id: 'not-installed', label: 'Not installed' },
              { id: 'updates', label: 'Updates available' },
            ] as { id: Filter; label: string }[]
          ).map(f => (
            <button
              key={f.id}
              className={`chip ${filter === f.id ? 'border-[var(--color-primary)] text-[var(--color-primary)]' : ''}`}
              onClick={() => setFilter(f.id)}
            >
              {f.label}
            </button>
          ))}
          {categories.length > 0 && category !== 'all' && (
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {categories.find(c => c.id === category)?.description}
            </span>
          )}
        </div>
      </div>

      {/* Winget search results */}
      {wingetSearch && (
        <div className="card">
          <div className="flex items-center justify-between gap-3">
            <div className="text-sm font-medium">
              Winget results for “{wingetSearch.query}”
            </div>
            <button className="btn btn-ghost btn-sm" onClick={() => setWingetSearch(null)}>
              <X size={14} />
            </button>
          </div>
          <div className="mt-3 space-y-2">
            {wingetSearch.results.length === 0 && (
              <div className="text-sm text-[var(--color-text-muted)]">Nothing matched.</div>
            )}
            {wingetSearch.results.map(r => {
              const known = byId.get(r.id)
              return (
                <div key={r.id} className="flex items-center justify-between gap-3 py-1.5 border-b border-[var(--color-glass-border)] last:border-0">
                  <div className="min-w-0">
                    <div className="text-[13px] truncate">
                      {r.name} <span className="text-[var(--color-text-muted)]">· {r.id}</span>
                    </div>
                    <div className="text-[11px] text-[var(--color-text-muted)]">
                      {r.version}
                      {r.installed ? ` · installed ${r.installedVersion ?? ''}` : ''}
                      {r.inCatalogue ? ' · in the catalogue' : ' · not in the catalogue'}
                      {!r.installable ? ' · not installable (no winget package ID)' : ''}
                    </div>
                  </div>
                  <div className="flex items-center gap-2 flex-shrink-0">
                    {known && (
                      <button className="btn btn-ghost btn-sm" onClick={() => void openDetail(known)}>
                        Details
                      </button>
                    )}
                    <button
                      className="btn btn-secondary btn-sm"
                      disabled={!r.installable || r.installed || busy !== null}
                      onClick={() => enqueue({ id: r.id, name: r.name }, 'install', 'any')}
                    >
                      <Download size={13} /> Install
                    </button>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Catalogue */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
        {!status &&
          Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="card h-32 skeleton animate-shimmer-slide" />
          ))}

        {status && visible.length === 0 && (
          <div className="card col-span-full text-sm text-[var(--color-text-muted)]">
            Nothing in the catalogue matches.
          </div>
        )}

        {visible.map(app => (
          <div key={app.id} className="card card-interactive flex flex-col gap-2">
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <div className="font-medium text-sm truncate">{app.name}</div>
                <div className="text-[11px] text-[var(--color-text-muted)] truncate">
                  {app.publisher ?? 'Publisher not stated'}
                </div>
              </div>
              {app.installed ? (
                app.updateAvailable ? (
                  <span className="badge badge-recommended flex-shrink-0">update</span>
                ) : (
                  <span className="badge badge-safe flex-shrink-0">installed</span>
                )
              ) : (
                <span className="badge badge-optional flex-shrink-0">not installed</span>
              )}
            </div>

            <div className="text-[11px] text-[var(--color-text-muted)] line-clamp-2 min-h-8">
              {app.description ?? 'No description.'}
            </div>

            <div className="text-[11px] text-[var(--color-text-muted)] font-mono break-all">
              {app.id}
              {app.installed && app.installedVersion ? ` · ${app.installedVersion}` : ''}
              {app.updateAvailable && app.availableVersion ? ` → ${app.availableVersion}` : ''}
            </div>

            <div className="flex items-center gap-2 mt-auto pt-1">
              <button className="btn btn-ghost btn-sm" onClick={() => void openDetail(app)}>
                <Info size={13} /> Details
              </button>
              {!app.installed && (
                <button
                  className="btn btn-primary btn-sm ml-auto"
                  disabled={busy !== null || wingetMissing}
                  onClick={() => enqueue(app, 'install', 'any')}
                >
                  <Download size={13} /> Install
                </button>
              )}
              {app.installed && app.updateAvailable && (
                <button
                  className="btn btn-primary btn-sm ml-auto"
                  disabled={busy !== null || wingetMissing}
                  onClick={() => enqueue(app, 'upgrade', 'any')}
                >
                  <Upload size={13} /> Upgrade
                </button>
              )}
              {app.installed && !app.updateAvailable && (
                <button
                  className="btn btn-danger btn-sm ml-auto"
                  disabled={busy !== null || wingetMissing}
                  onClick={() => enqueue(app, 'uninstall', 'any')}
                  title="Removes the package with winget. This does not take a snapshot — the app's own uninstaller decides what is removed."
                >
                  <Trash2 size={13} /> Uninstall
                </button>
              )}
            </div>
          </div>
        ))}
      </div>

      {/* Deliberately absent */}
      {catalogue && catalogue.absent.length > 0 && (
        <div className="card">
          <div className="text-sm font-medium flex items-center gap-2">
            <Info size={14} /> Not offered on purpose
          </div>
          <div className="mt-2 space-y-1">
            {catalogue.absent.map(a => (
              <div key={a.name} className="text-[12px] text-[var(--color-text-muted)]">
                <span className="text-[var(--color-text)]">{a.name}</span> — {a.reason}
              </div>
            ))}
          </div>
        </div>
      )}

      {status && (
        <div className="text-[11px] text-[var(--color-text-muted)]">
          {status.deep
            ? 'Full check: every package Windows reports, including copies installed outside winget.'
            : 'Quick check: winget’s own index. Open an app’s details to confirm its installed state — that check also scans Add/Remove Programs and sees copies winget’s index does not.'}
        </div>
      )}

      {/* Detail modal */}
      {detail && (
        <div className="modal-backdrop" onClick={closeDetail}>
          <div className="modal-content animate-scale-in w-full max-w-2xl" onClick={e => e.stopPropagation()}>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="text-lg font-bold">{detailData?.name || detail.name}</div>
                <div className="text-[12px] font-mono break-all text-[var(--color-text-muted)]">{detail.id}</div>
              </div>
              <button className="btn btn-ghost btn-sm" onClick={closeDetail}>
                <X size={16} />
              </button>
            </div>

            {checking && (
              <div className="flex items-center gap-2 text-sm text-[var(--color-text-muted)] mt-4">
                <Loader2 size={14} className="animate-spin" /> Asking winget what is actually installed…
              </div>
            )}

            {detailData?.error && (
              <div className="text-sm text-[var(--color-danger)] mt-3">{detailData.error}</div>
            )}

            {!checking && (
              <>
                {/* The source, before anything else. */}
                <div className="mt-4 grid grid-cols-2 gap-2 text-[12px]">
                  <Row k="Source" v={detailData?.source ?? 'winget'} />
                  <Row k="Version" v={detailData?.version ?? check?.installedVersion ?? 'unknown'} />
                  <Row k="Publisher" v={detailData?.publisher ?? 'not stated'} />
                  <Row k="Licence" v={detailData?.license ?? 'not stated'} />
                  <Row k="Installed" v={
                    !check
                      ? 'unknown'
                      : check.installed
                        ? `${check.installedVersion ?? 'yes'}${check.availableVersion ? ` → ${check.availableVersion}` : ''}`
                        : 'not installed'
                  } />
                  <Row k="Catalogue" v={detailData?.inCatalogue ? (detailData.category ?? 'yes') : 'not in it'} />
                </div>

                {(detailData?.installerType || detailData?.installerUrl) && (
                  <div className="mt-3 text-[12px] space-y-1">
                    <div className="text-[var(--color-text-muted)]">What would be downloaded</div>
                    {detailData?.installerType && <Row k="Installer type" v={detailData.installerType} />}
                    {detailData?.installerUrl && <Row k="Installer URL" v={detailData.installerUrl} />}
                    {detailData?.installerSha256 && <Row k="SHA256" v={detailData.installerSha256} />}
                  </div>
                )}

                {detailData?.description && (
                  <p className="mt-3 text-[13px] text-[var(--color-text-muted)]">{detailData.description}</p>
                )}

                <div className="mt-4">
                  <div className="text-[12px] font-medium mb-2">Install scope</div>
                  <div className="space-y-1.5">
                    {SCOPES.map(s => (
                      <label key={s.value} className="flex items-start gap-2 text-[12px] cursor-pointer">
                        <input
                          type="radio"
                          name="scope"
                          className="mt-0.5"
                          checked={scope === s.value}
                          onChange={() => setScope(s.value)}
                        />
                        <span>
                          <span className={s.admin ? 'text-[var(--color-warning)]' : ''}>{s.label}</span>
                          {s.admin && <span className="badge badge-experimental ml-2">administrator</span>}
                          <span className="block text-[var(--color-text-muted)]">{s.note}</span>
                        </span>
                      </label>
                    ))}
                  </div>
                </div>

                <div className="flex items-center gap-2 mt-5 flex-wrap">
                  {check?.installed ? (
                    <>
                      {check.availableVersion && (
                        <button
                          className="btn btn-primary"
                          disabled={busy !== null}
                          onClick={() => {
                            enqueue(detail, 'upgrade', scope)
                            closeDetail()
                          }}
                        >
                          <Upload size={14} /> Upgrade to {check.availableVersion}
                        </button>
                      )}
                      <button
                        className="btn btn-danger"
                        disabled={busy !== null}
                        onClick={() => {
                          enqueue(detail, 'uninstall', scope)
                          closeDetail()
                        }}
                      >
                        <Trash2 size={14} /> Uninstall
                      </button>
                    </>
                  ) : (
                    <button
                      className="btn btn-primary"
                      disabled={busy !== null || !check}
                      onClick={() => {
                        enqueue(detail, 'install', scope)
                        closeDetail()
                      }}
                    >
                      <Download size={14} /> Install
                    </button>
                  )}

                  {(detail.homepage || detailData?.homepage || detailData?.publisherUrl) && (
                    <button
                      className="btn btn-secondary"
                      onClick={() =>
                        void openLink(detailData?.homepage ?? detailData?.publisherUrl ?? detail.homepage ?? '')
                      }
                    >
                      <ExternalLink size={14} /> Official page
                    </button>
                  )}
                </div>

                <div className="mt-3 text-[11px] text-[var(--color-text-muted)]">
                  Runs: <span className="font-mono break-all">winget install --id {detail.id} --exact --source winget</span>
                  {scope !== 'any' && <span className="font-mono"> --scope {scope}</span>}
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function Row({ k, v }: { k: string; v: string }) {
  return (
    <div className="flex items-start gap-2 min-w-0">
      <span className="text-[var(--color-text-muted)] flex-shrink-0 w-24">{k}</span>
      <span className="break-all min-w-0">{v}</span>
    </div>
  )
}
