import { useCallback, useEffect, useMemo, useState } from 'react'
import { invoke } from '@tauri-apps/api/core'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  Ban,
  CheckCircle2,
  Download,
  Eye,
  Globe,
  Loader2,
  Lock,
  Network,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Shield,
  ShieldAlert,
  Trash2,
  Upload,
  X,
} from 'lucide-react'
import type {
  BlockCategory,
  BlockChange,
  BlockFetchResult,
  BlockRule,
  BlockSeverity,
  BlockSource,
  BlockerStatus,
} from '../types'

const CATEGORIES: BlockCategory[] = [
  'Ads',
  'Trackers',
  'Telemetry',
  'Malware',
  'Analytics',
  'Software',
  'Custom',
]

const SEVERITIES: BlockSeverity[] = ['Low', 'Medium', 'High']

/**
 * The sentence the brief insists on for software-specific rules. It is shown
 * in the picker itself rather than only in the confirmation dialog, because a
 * category named "Software" sitting next to "Malware" reads as a licence
 * bypass unless something says otherwise first.
 */
const SOFTWARE_NOTE =
  'Software blocks a product’s own telemetry and network endpoints. It is not an activation or ' +
  'licence bypass of any kind, and the product may stop updating, stop signing in, or lose ' +
  'features that depend on reaching its servers.'

const LICENCE_WARNING =
  'Do not add a licensing server or anything whose purpose is to bypass activation. That is not ' +
  'what this field is for.'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

function when(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime())
    ? '—'
    : d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

function bytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} kB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

function categoryBadge(c: BlockCategory): string {
  switch (c) {
    case 'Malware': return 'badge badge-dangerous'
    case 'Software': return 'badge badge-risky'
    case 'Telemetry':
    case 'Analytics': return 'badge badge-experimental'
    case 'Ads':
    case 'Trackers': return 'badge badge-recommended'
    default: return 'badge badge-optional'
  }
}

function severityBadge(s: BlockSeverity): string {
  switch (s) {
    case 'High': return 'badge badge-risky'
    case 'Medium': return 'badge badge-experimental'
    default: return 'badge badge-safe'
  }
}

/** True when a preview is worth showing, and worth applying from. */
function usable(p: BlockFetchResult | undefined): p is BlockFetchResult {
  return !!p && !!p.url && p.bytes > 0
}

/** One confirmation dialog, used for every write that can cost something. */
type Pending =
  | { kind: 'apply'; source: BlockSource; preview: BlockFetchResult }
  | { kind: 'remove-source'; source: string; name: string; domains: number }
  | { kind: 'add-domain' }
  | { kind: 'add-program' }
  | { kind: 'unmerge' }
  | { kind: 'restore' }
  | { kind: 'clear-firewall' }
  | { kind: 'import' }

export default function Blocker() {
  const [status, setStatus] = useState<BlockerStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<BlockChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [previews, setPreviews] = useState<Record<string, BlockFetchResult>>({})
  const [pending, setPending] = useState<Pending | null>(null)

  const [domain, setDomain] = useState('')
  const [domainCategory, setDomainCategory] = useState<BlockCategory>('Ads')
  const [domainPurpose, setDomainPurpose] = useState('')
  const [domainSeverity, setDomainSeverity] = useState<BlockSeverity>('Low')
  const [programId, setProgramId] = useState('')
  const [programPath, setProgramPath] = useState('')
  const [programCategory, setProgramCategory] = useState<BlockCategory>('Telemetry')
  const [importPath, setImportPath] = useState('')
  const [filter, setFilter] = useState('')

  const load = useCallback(
    async () => setStatus(await invokeJson<BlockerStatus>('blocker_status')),
    [],
  )

  /**
   * One busy flag, one error banner. Returning the command's result rather
   * than swallowing it is what lets "fetch, then decide" stay a single call
   * without the caller having to repeat the try/catch.
   */
  const run = useCallback(async <T,>(key: string, fn: () => Promise<T>): Promise<T | null> => {
    setBusy(key)
    setError(null)
    try {
      return await fn()
    } catch (err) {
      setError(errMsg(err))
      return null
    } finally {
      setBusy(null)
    }
  }, [])

  useEffect(() => {
    void run('load', load)
  }, [run, load])

  const hostsRules = useMemo(
    () => (status?.rules ?? []).filter(r => r.kind === 'Hosts'),
    [status],
  )
  const firewallRules = useMemo(
    () => (status?.rules ?? []).filter(r => r.kind === 'Firewall'),
    [status],
  )

  /**
   * Rules belonging to an applied list are counted on that list's card, not
   * listed here: one list is two thousand lines, and a table nobody can scroll
   * is worse than no table.
   */
  const ownRules = useMemo(() => {
    const pool: BlockRule[] = [
      ...hostsRules.filter(r => r.source === 'custom' || !r.source),
      ...firewallRules,
    ]
    const q = filter.trim().toLowerCase()
    if (!q) return pool
    return pool.filter(
      r =>
        r.id.toLowerCase().includes(q) ||
        (r.application ?? '').toLowerCase().includes(q) ||
        (r.purpose ?? '').toLowerCase().includes(q),
    )
  }, [hostsRules, firewallRules, filter])

  const listedCount = hostsRules.length - hostsRules.filter(r => r.source === 'custom' || !r.source).length

  async function preview(src: BlockSource) {
    const res = await run(`fetch:${src.id}`, () =>
      invokeJson<BlockFetchResult>('blocker_fetch', { source: src.id }),
    )
    if (!res) return
    setPreviews(p => ({ ...p, [src.id]: res }))
    if (!res.url) setError(`'${src.id}' is not a list Novimize knows about.`)
    else if (res.bytes === 0)
      setError(`The download from ${src.url} failed or came up empty. Nothing was written.`)
  }

  /**
   * Fetch if there is nothing worth showing yet, then open the dialog with
   * real numbers in it. Applying a list whose size nobody has seen is the
   * thing this two-step exists to prevent, so the fetch is not optional.
   */
  async function prepareApply(src: BlockSource) {
    let shown = previews[src.id]
    if (!usable(shown)) {
      const res = await run(`fetch:${src.id}`, () =>
        invokeJson<BlockFetchResult>('blocker_fetch', { source: src.id }),
      )
      if (!res) return
      setPreviews(p => ({ ...p, [src.id]: res }))
      if (!res.url) {
        setError(`'${src.id}' is not a list Novimize knows about.`)
        return
      }
      if (res.bytes === 0) {
        setError(`The download from ${src.url} failed or came up empty. Nothing was written.`)
        return
      }
      shown = res
    }
    setPending({ kind: 'apply', source: src, preview: shown })
  }

  /** Re-fetch an applied list and go straight to its confirmation. */
  async function updateApplied(sourceId: string) {
    const src = status?.sources.find(s => s.id.toLowerCase() === sourceId.toLowerCase())
    if (!src) {
      setError(`'${sourceId}' is no longer in the catalogue, so it cannot be re-fetched. Take it off instead.`)
      return
    }
    setPreviews(p => {
      const next = { ...p }
      delete next[src.id]
      return next
    })
    await prepareApply(src)
  }

  async function apply(src: BlockSource) {
    const res = await run(`apply:${src.id}`, () =>
      invokeJson<BlockChange>('blocker_apply', { source: src.id, confirm: true }),
    )
    if (res) {
      setChange(res)
      await load()
    }
  }

  async function removeSource(sourceId: string) {
    const res = await run(`rm:${sourceId}`, () =>
      invokeJson<BlockChange>('blocker_remove_source', { source: sourceId, confirm: true }),
    )
    if (res) {
      setChange(res)
      await load()
    }
  }

  async function ruleAction(action: 'enable' | 'disable' | 'remove', rule: BlockRule) {
    const res = await run(`${action}:${rule.id}`, () =>
      invokeJson<BlockChange>('blocker_rule', {
        action,
        id: rule.id,
        kind: rule.kind === 'Hosts' ? 'hosts' : 'firewall',
      }),
    )
    if (res) {
      setChange(res)
      await load()
    }
  }

  async function addDomain(confirm: boolean) {
    const res = await run('add', () =>
      invokeJson<BlockChange>('blocker_add', {
        value: domain.trim(),
        category: domainCategory,
        purpose: domainPurpose.trim() || null,
        severity: domainSeverity,
        confirm,
      }),
    )
    if (res) {
      setChange(res)
      if (res.success) {
        setDomain('')
        setDomainPurpose('')
      }
      await load()
    }
  }

  async function addProgram(confirm: boolean) {
    const res = await run('program', () =>
      invokeJson<BlockChange>('blocker_program', {
        id: programId.trim(),
        program: programPath.trim(),
        category: programCategory,
        purpose: null,
        severity: 'Low',
        confirm,
      }),
    )
    if (res) {
      setChange(res)
      if (res.success) {
        setProgramId('')
        setProgramPath('')
      }
      await load()
    }
  }

  async function reset(action: 'unmerge' | 'restore' | 'clear-firewall') {
    const res = await run(action, () =>
      invokeJson<BlockChange>('blocker_reset', { action, confirm: true }),
    )
    if (res) {
      setChange(res)
      await load()
    }
  }

  async function exportRules() {
    const res = await run('export', () =>
      invokeJson<BlockChange>('blocker_export', { output: null }),
    )
    if (res) setChange(res)
  }

  async function importRules() {
    const res = await run('import', () =>
      invokeJson<BlockChange>('blocker_import', { input: importPath.trim(), confirm: true }),
    )
    if (res) {
      setChange(res)
      if (res.success) setImportPath('')
      await load()
    }
  }

  /**
   * A Software-category rule is confirmed twice over: the dialog says what it
   * is and is not, and the CLI refuses the write without --confirm even if
   * this call were to drop it.
   */
  function confirmAddDomain() {
    if (domainCategory === 'Software') setPending({ kind: 'add-domain' })
    else void addDomain(false)
  }

  const applied = status?.applied ?? []
  const appliedIds = new Set(applied.map(a => a.source.toLowerCase()))
  const writable = status?.writable ?? false

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Ban size={22} className="text-[var(--color-danger)]" />
            Blocker
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Hosts rules, firewall rules and blocklists — each one with a way back
          </p>
        </div>
        <button
          className="btn btn-secondary btn-sm"
          onClick={() => void run('load', load)}
          disabled={busy !== null}
        >
          {busy === 'load' ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
          Refresh
        </button>
      </div>

      {error && (
        <div className="card flex items-start gap-3 border-[var(--color-danger)] animate-slide-up stagger-1">
          <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          <div className="text-[13px] text-[var(--color-danger)] break-words flex-1">{error}</div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setError(null)}>
            Dismiss
          </button>
        </div>
      )}

      {change && (
        <div
          className="card flex items-start gap-3 animate-slide-up stagger-1"
          style={{ borderColor: change.success ? 'rgba(34,197,94,0.35)' : 'rgba(239,68,68,0.4)' }}
        >
          {change.success ? (
            <CheckCircle2 size={16} className="text-[var(--color-success)] mt-0.5 flex-shrink-0" />
          ) : (
            <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          )}
          <div className="min-w-0 flex-1">
            <div className="text-[13px] break-words">{change.message}</div>
            {change.backup && (
              <div className="text-[11px] text-[var(--color-text-muted)] font-mono mt-0.5 break-all">
                Backup: {change.backup}
              </div>
            )}
            {change.needsElevation && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1 flex items-center gap-1.5">
                <Lock size={12} /> Administrator rights are required, so nothing was changed.
              </div>
            )}
          </div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setChange(null)}>
            Dismiss
          </button>
        </div>
      )}

      {/* ── Where things stand ───────────────────────────────────── */}
      {status && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 animate-slide-up stagger-1">
          <div className="card">
            <div className="flex items-center gap-2 mb-3">
              <Network size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Hosts file</h3>
              <span
                className="ml-auto text-[11px] text-[var(--color-text-muted)] font-mono truncate max-w-[55%]"
                title={status.hostsPath}
              >
                {status.hostsPath}
              </span>
            </div>
            <div className="grid grid-cols-3 gap-2 text-center">
              <Stat value={status.managed} label="managed" />
              <Stat value={status.enabled} label="enabled" />
              <Stat value={status.unmanaged} label="yours" />
            </div>
            <div className="mt-3 space-y-1.5 text-[12px]">
              <Line on={writable} yes="Writable" no="Needs administrator rights" />
              <Line
                on={status.backupExists}
                yes="Backup kept"
                no="No backup yet — one is taken on the first write"
              />
              {status.hostsMalformed && (
                <div className="flex items-start gap-1.5 text-[var(--color-danger)]">
                  <AlertTriangle size={13} className="mt-0.5 flex-shrink-0" />
                  The Novimize markers are unbalanced. Nothing can be written until they are fixed by
                  hand.
                </div>
              )}
            </div>
            <p className="text-[11px] text-[var(--color-text-muted)] mt-2">
              “Yours” are lines somebody else wrote. They are counted so the number is honest, and
              never edited.
            </p>
          </div>

          <div className="card">
            <div className="flex items-center gap-2 mb-3">
              <Shield size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Windows Firewall</h3>
            </div>
            <div className="grid grid-cols-3 gap-2 text-center">
              <Stat value={status.firewallRules} label="rules" />
              <Stat value={firewallRules.filter(r => r.enabled).length} label="enabled" />
              <Stat value={applied.length} label="lists" />
            </div>
            <div className="mt-3 space-y-1.5 text-[12px]">
              <Line
                on={status.firewallReadable}
                yes="Readable"
                no="Could not be read — the list below may be incomplete"
              />
              <Line
                on={applied.length > 0}
                yes={`${applied.length} list(s) applied`}
                no="No blocklist applied yet"
              />
            </div>
            <p className="text-[11px] text-[var(--color-text-muted)] mt-2">
              Every rule here carries the <span className="font-mono">Novimize.Block.</span> prefix,
              so the firewall code only ever touches rules it made.
            </p>
          </div>
        </div>
      )}

      {/* ── Applied lists ────────────────────────────────────────── */}
      {status && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-1">
            <Globe size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Applied lists</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{applied.length}</span>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
            Taking one list off leaves every other list and every line you wrote exactly as it is.
          </p>

          {applied.length === 0 ? (
            <div className="text-[13px] text-[var(--color-text-muted)] py-4 text-center">
              Nothing is applied. Pick a list below to see what it contains before writing anything.
            </div>
          ) : (
            <div className="space-y-2">
              {applied.map(a => (
                <div
                  key={a.source}
                  className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2.5"
                >
                  <div className="min-w-0 flex-1">
                    <div className="text-[13px] font-medium truncate">
                      {a.name ?? a.source}
                      <span className="text-[11px] text-[var(--color-text-muted)] font-mono ml-2">
                        {a.source}
                      </span>
                    </div>
                    <div className="text-[11px] text-[var(--color-text-muted)]">
                      {a.domains.toLocaleString()} rule{a.domains === 1 ? '' : 's'} · added{' '}
                      {when(a.appliedAt)} · updated {when(a.updatedAt)}
                    </div>
                  </div>
                  <button
                    className="btn btn-secondary btn-sm"
                    onClick={() => void updateApplied(a.source)}
                    disabled={busy !== null}
                    title="Re-fetch this list and show what would change"
                  >
                    {busy === `fetch:${a.source}` || busy === `apply:${a.source}` ? (
                      <Loader2 size={13} className="animate-spin" />
                    ) : (
                      <RefreshCw size={13} />
                    )}
                    Update
                  </button>
                  <button
                    className="btn btn-ghost btn-sm"
                    onClick={() =>
                      setPending({
                        kind: 'remove-source',
                        source: a.source,
                        name: a.name ?? a.source,
                        domains: a.domains,
                      })
                    }
                    disabled={busy !== null}
                    title="Remove this list's rules and nothing else"
                  >
                    <Trash2 size={13} />
                    Take off
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── Available blocklists ─────────────────────────────────── */}
      {status && status.sources.length > 0 && (
        <div className="card animate-slide-up stagger-3">
          <div className="flex items-center gap-2 mb-1">
            <Globe size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Blocklists Novimize can fetch</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{status.sources.length}</span>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
            Nothing here is shipped inside Novimize — only the address it comes from. Preview shows
            the size and the diff; nothing is written until you apply.
          </p>

          <div className="space-y-2">
            {status.sources.map(src => {
              const shown = previews[src.id]
              return (
                <div key={src.id} className="rounded-lg border border-[var(--color-border)] p-3">
                  <div className="flex flex-wrap items-start gap-2">
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="text-[13px] font-semibold">{src.name}</span>
                        <span className={categoryBadge(src.category)}>{src.category}</span>
                        <span className={severityBadge(src.severity)}>{src.severity}</span>
                        <span className="badge badge-optional">{src.format}</span>
                        {appliedIds.has(src.id.toLowerCase()) && (
                          <span className="badge badge-safe">applied</span>
                        )}
                      </div>
                      {src.purpose && (
                        <div className="text-[12px] text-[var(--color-text-muted)] mt-1">
                          {src.purpose}
                        </div>
                      )}
                      {src.breakage && (
                        <div className="text-[12px] text-[var(--color-warning)] mt-1 flex items-start gap-1.5">
                          <AlertTriangle size={13} className="mt-0.5 flex-shrink-0" />
                          May break: {src.breakage}
                        </div>
                      )}
                      <div className="text-[11px] text-[var(--color-text-muted)] font-mono mt-1 break-all">
                        {src.url}
                        {src.license && <span className="font-sans"> · {src.license}</span>}
                      </div>
                    </div>
                    <div className="flex gap-1.5 flex-shrink-0">
                      {src.homepage && (
                        <button
                          className="btn btn-ghost btn-sm"
                          title="Open the project's own page"
                          onClick={() =>
                            void invoke('open_external', { url: src.homepage }).catch(err =>
                              setError(errMsg(err)),
                            )
                          }
                        >
                          <Globe size={13} />
                        </button>
                      )}
                      <button
                        className="btn btn-secondary btn-sm"
                        onClick={() => void preview(src)}
                        disabled={busy !== null}
                      >
                        {busy === `fetch:${src.id}` ? (
                          <Loader2 size={13} className="animate-spin" />
                        ) : (
                          <Eye size={13} />
                        )}
                        Preview
                      </button>
                      <button
                        className="btn btn-primary btn-sm"
                        onClick={() => void prepareApply(src)}
                        disabled={busy !== null}
                        title="Fetch if needed, then show the diff before writing"
                      >
                        Apply…
                      </button>
                    </div>
                  </div>

                  {usable(shown) && (
                    <div className="mt-2 rounded-md bg-[var(--color-bg-elevated)] border border-[var(--color-border)] px-3 py-2 text-[12px]">
                      <span className="font-medium">{shown.domains.toLocaleString()}</span> entries ·{' '}
                      {bytes(shown.bytes)} · fetched {when(shown.fetchedAt)}
                      {shown.firstTime ? (
                        <span className="text-[var(--color-text-muted)]">
                          {' '}· nothing from this list is applied yet, so there is no diff
                        </span>
                      ) : (
                        <span>
                          {' '}· against what this machine enforces now:{' '}
                          <span className="text-[var(--color-success)]">+{shown.added}</span>{' '}
                          <span className="text-[var(--color-danger)]">−{shown.removed}</span>
                        </span>
                      )}
                      <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">
                        Nothing has been written.
                      </div>
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* ── Your own rules ───────────────────────────────────────── */}
      <div className="card animate-slide-up stagger-4">
        <div className="flex items-center gap-2 mb-3 flex-wrap">
          <Plus size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">Your own rules</h3>
          <span className="text-[11px] text-[var(--color-text-muted)]">{ownRules.length}</span>
          {listedCount > 0 && (
            <span className="text-[11px] text-[var(--color-text-muted)]">
              · {listedCount.toLocaleString()} from applied lists are counted above, not listed here
            </span>
          )}
          <div className="ml-auto relative">
            <Search
              size={13}
              className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]"
            />
            <input
              className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md pl-8 pr-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors w-44"
              placeholder="Filter"
              value={filter}
              onChange={e => setFilter(e.target.value)}
            />
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4">
          <div className="rounded-lg border border-[var(--color-border)] p-3">
            <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-2">
              Block a domain
            </div>
            <input
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors font-mono"
              placeholder="ads.example.com"
              value={domain}
              onChange={e => setDomain(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter' && domain.trim()) confirmAddDomain()
              }}
            />
            <div className="flex gap-2 mt-2">
              <select
                className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-2 py-2 text-[12px] outline-none focus:border-[var(--color-primary)]"
                value={domainCategory}
                onChange={e => setDomainCategory(e.target.value as BlockCategory)}
              >
                {CATEGORIES.map(c => (
                  <option key={c} value={c}>{c}</option>
                ))}
              </select>
              <select
                className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-2 py-2 text-[12px] outline-none focus:border-[var(--color-primary)]"
                value={domainSeverity}
                onChange={e => setDomainSeverity(e.target.value as BlockSeverity)}
              >
                {SEVERITIES.map(s => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>
            <input
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors mt-2"
              placeholder="One sentence: what this blocks and why"
              value={domainPurpose}
              onChange={e => setDomainPurpose(e.target.value)}
            />
            {domainCategory === 'Software' && (
              <div className="text-[11px] text-[var(--color-warning)] mt-2 leading-relaxed">
                {SOFTWARE_NOTE}
              </div>
            )}
            <button
              className="btn btn-primary btn-sm mt-2 w-full"
              onClick={confirmAddDomain}
              disabled={busy !== null || !domain.trim()}
            >
              {busy === 'add' ? <Loader2 size={13} className="animate-spin" /> : <Plus size={13} />}
              Block it
            </button>
          </div>

          <div className="rounded-lg border border-[var(--color-border)] p-3">
            <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-2">
              Block a program (firewall)
            </div>
            <input
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
              placeholder="Rule id, e.g. some-app"
              value={programId}
              onChange={e => setProgramId(e.target.value)}
            />
            <input
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors mt-2 font-mono"
              placeholder="C:\Path\To\app.exe"
              value={programPath}
              onChange={e => setProgramPath(e.target.value)}
            />
            <select
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-2 py-2 text-[12px] outline-none focus:border-[var(--color-primary)] mt-2"
              value={programCategory}
              onChange={e => setProgramCategory(e.target.value as BlockCategory)}
            >
              {CATEGORIES.map(c => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
            {programCategory === 'Software' && (
              <div className="text-[11px] text-[var(--color-warning)] mt-2 leading-relaxed">
                {SOFTWARE_NOTE}
              </div>
            )}
            <button
              className="btn btn-primary btn-sm mt-2 w-full"
              onClick={() =>
                programCategory === 'Software'
                  ? setPending({ kind: 'add-program' })
                  : void addProgram(false)
              }
              disabled={busy !== null || !programId.trim() || !programPath.trim()}
            >
              {busy === 'program' ? (
                <Loader2 size={13} className="animate-spin" />
              ) : (
                <Plus size={13} />
              )}
              Block it
            </button>
          </div>
        </div>

        {ownRules.length === 0 ? (
          <div className="text-[13px] text-[var(--color-text-muted)] py-4 text-center">
            {filter ? 'Nothing matches that filter.' : 'No rules of your own yet.'}
          </div>
        ) : (
          <div className="space-y-1 max-h-96 overflow-y-auto pr-1">
            {ownRules.map(r => (
              <div key={`${r.kind}:${r.id}`} className="flex items-center gap-2 py-1.5">
                <button
                  className="btn btn-ghost btn-sm px-1.5 flex-shrink-0"
                  onClick={() => void ruleAction(r.enabled ? 'disable' : 'enable', r)}
                  disabled={busy !== null}
                  title={r.enabled ? 'Stop enforcing this rule' : 'Enforce this rule again'}
                >
                  <span
                    className="w-2 h-2 rounded-full block"
                    style={{
                      background: r.enabled ? 'var(--color-success)' : 'var(--color-text-muted)',
                      opacity: r.enabled ? 1 : 0.4,
                    }}
                  />
                </button>
                <span
                  className="text-[12px] font-mono truncate flex-1 min-w-0"
                  style={{ opacity: r.enabled ? 1 : 0.5 }}
                  title={`${r.id}${r.application ? ` → ${r.application}` : ''}`}
                >
                  {r.id}
                </span>
                <span className={`${categoryBadge(r.category)} flex-shrink-0`}>{r.category}</span>
                <span className="text-[11px] text-[var(--color-text-muted)] flex-shrink-0">
                  {r.kind === 'Firewall' ? 'firewall' : 'hosts'}
                </span>
                <button
                  className="btn btn-ghost btn-sm flex-shrink-0"
                  onClick={() => void ruleAction('remove', r)}
                  disabled={busy !== null}
                  title="Remove this rule"
                >
                  <Trash2 size={13} />
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* ── Move it around, and back ─────────────────────────────── */}
      <div className="card animate-slide-up stagger-5">
        <div className="flex items-center gap-2 mb-1">
          <RotateCcw size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">Export, import and rollback</h3>
        </div>
        <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
          Unmerge removes Novimize’s section and leaves your own hosts lines alone. Restore puts the
          file back to the copy taken before the first write, which discards your rules too.
        </p>

        <div className="flex flex-wrap gap-2 mb-4">
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => void exportRules()}
            disabled={busy !== null}
            title="Write everything Novimize enforces to a file"
          >
            {busy === 'export' ? (
              <Loader2 size={13} className="animate-spin" />
            ) : (
              <Download size={13} />
            )}
            Export
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setPending({ kind: 'unmerge' })}
            disabled={busy !== null || !status?.managed}
          >
            {busy === 'unmerge' ? (
              <Loader2 size={13} className="animate-spin" />
            ) : (
              <Trash2 size={13} />
            )}
            Unmerge
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setPending({ kind: 'restore' })}
            disabled={busy !== null || !status?.backupExists}
            title={status?.backupExists ? '' : 'No backup has been taken yet'}
          >
            {busy === 'restore' ? (
              <Loader2 size={13} className="animate-spin" />
            ) : (
              <RotateCcw size={13} />
            )}
            Restore backup
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setPending({ kind: 'clear-firewall' })}
            disabled={busy !== null || !status?.firewallRules}
          >
            {busy === 'clear-firewall' ? (
              <Loader2 size={13} className="animate-spin" />
            ) : (
              <ShieldAlert size={13} />
            )}
            Clear firewall rules
          </button>
        </div>

        <div className="flex gap-2">
          <input
            className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors font-mono"
            placeholder="Path to a previous export"
            value={importPath}
            onChange={e => setImportPath(e.target.value)}
          />
          <button
            className="btn btn-secondary"
            onClick={() => setPending({ kind: 'import' })}
            disabled={busy !== null || !importPath.trim()}
          >
            {busy === 'import' ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
            Import
          </button>
        </div>
      </div>

      {/* ── Confirmation ─────────────────────────────────────────── */}
      {pending && (
        <ConfirmDialog
          pending={pending}
          status={status}
          busy={busy}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const p = pending
            setPending(null)
            switch (p.kind) {
              case 'apply': void apply(p.source); break
              case 'remove-source': void removeSource(p.source); break
              case 'add-domain': void addDomain(true); break
              case 'add-program': void addProgram(true); break
              case 'unmerge': void reset('unmerge'); break
              case 'restore': void reset('restore'); break
              case 'clear-firewall': void reset('clear-firewall'); break
              case 'import': void importRules(); break
            }
          }}
        />
      )}
    </div>
  )
}

function Stat({ value, label }: { value: number; label: string }) {
  return (
    <div className="rounded-lg bg-[var(--color-bg-elevated)] border border-[var(--color-border)] py-2">
      <div className="text-[17px] font-semibold leading-none">{value.toLocaleString()}</div>
      <div className="text-[10px] text-[var(--color-text-muted)] mt-1 uppercase tracking-wider">
        {label}
      </div>
    </div>
  )
}

function Line({ on, yes, no }: { on: boolean; yes: string; no: string }) {
  return (
    <div className={on ? 'text-[var(--color-text)]' : 'text-[var(--color-warning)]'}>
      <span
        className="inline-block w-1.5 h-1.5 rounded-full mr-2 align-middle"
        style={{ background: on ? 'var(--color-success)' : 'var(--color-warning)' }}
      />
      {on ? yes : no}
    </div>
  )
}

function ConfirmDialog({
  pending,
  status,
  busy,
  onCancel,
  onConfirm,
}: {
  pending: Pending
  status: BlockerStatus | null
  busy: string | null
  onCancel: () => void
  onConfirm: () => void
}) {
  const detail = describe(pending, status)
  return (
    <div className="modal-backdrop" onClick={onCancel}>
      <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
        <div className="flex items-start gap-3 mb-4">
          <div
            className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
            style={{ background: detail.danger ? 'rgba(239,68,68,0.15)' : 'rgba(59,130,246,0.15)' }}
          >
            {detail.danger ? (
              <ShieldAlert size={19} className="text-[var(--color-danger)]" />
            ) : (
              <Shield size={19} className="text-[var(--color-primary)]" />
            )}
          </div>
          <div className="min-w-0 flex-1">
            <h3 className="text-[15px] font-semibold">{detail.title}</h3>
            <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">{detail.body}</p>
          </div>
          <button
            className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1"
            onClick={onCancel}
          >
            <X size={16} />
          </button>
        </div>

        <div className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2.5 mb-3 space-y-1">
          {detail.lines.map(line => (
            <div key={line} className="text-[12px] break-words">
              {line}
            </div>
          ))}
        </div>

        {detail.warnings.length > 0 && (
          <div className="flex flex-col gap-2 mb-4">
            {detail.warnings.map(w => (
              <div
                key={w}
                className="flex items-start gap-2 rounded-lg bg-red-500/10 border border-red-500/25 px-3 py-2.5"
              >
                <AlertTriangle size={15} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
                <div className="text-[12px] text-[var(--color-danger)]">{w}</div>
              </div>
            ))}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <button className="btn btn-secondary" onClick={onCancel}>
            Cancel
          </button>
          <button
            className={detail.danger ? 'btn btn-danger' : 'btn btn-primary'}
            onClick={onConfirm}
            disabled={busy !== null}
          >
            {busy !== null && <Loader2 size={14} className="animate-spin" />}
            {detail.confirm}
          </button>
        </div>
      </div>
    </div>
  )
}

function describe(pending: Pending, status: BlockerStatus | null) {
  switch (pending.kind) {
    case 'apply':
      return {
        title: `Apply ${pending.source.name}?`,
        body: 'This writes into the managed section of the hosts file.',
        danger: false,
        confirm: 'Apply',
        lines: [
          `${pending.preview.domains.toLocaleString()} entries from ${pending.source.url}`,
          pending.preview.firstTime
            ? 'Nothing from this list is applied yet.'
            : `Against what this machine enforces now: ${pending.preview.added} added, ${pending.preview.removed} removed.`,
          `${status?.unmanaged ?? 0} of your own hosts lines sit outside the section and will not be touched.`,
        ],
        warnings: pending.source.breakage ? [`May break: ${pending.source.breakage}`] : [],
      }

    case 'remove-source':
      return {
        title: `Take ${pending.name} off?`,
        body: 'Only this list’s own rules are removed.',
        danger: false,
        confirm: 'Take it off',
        lines: [
          `${pending.domains.toLocaleString()} rule(s) will be removed.`,
          'Every other applied list and every line you wrote stays exactly as it is.',
        ],
        warnings: [],
      }

    case 'add-domain':
      return {
        title: 'Block a software-specific endpoint?',
        body: 'This is an optional network endpoint rule, nothing more.',
        danger: true,
        confirm: 'Block it',
        lines: [SOFTWARE_NOTE],
        warnings: [LICENCE_WARNING],
      }

    case 'add-program':
      return {
        title: 'Block this program from the network?',
        body: 'A firewall rule will drop its traffic in both directions.',
        danger: true,
        confirm: 'Block it',
        lines: [SOFTWARE_NOTE],
        warnings: [LICENCE_WARNING],
      }

    case 'unmerge':
      return {
        title: 'Remove everything Novimize added?',
        body: 'The managed section comes out; the rest of the hosts file stays.',
        danger: true,
        confirm: 'Unmerge',
        lines: [
          `${status?.managed ?? 0} managed rule(s) will be removed.`,
          `${status?.unmanaged ?? 0} of your own lines sit outside the section and will not be touched.`,
          'The backup taken before the first write is kept, so Restore is still available.',
        ],
        warnings: [],
      }

    case 'restore':
      return {
        title: 'Restore the hosts file backup?',
        body: 'The file goes back to how it looked before Novimize’s first write.',
        danger: true,
        confirm: 'Restore',
        lines: [
          `Backup: ${status?.backupPath ?? 'unknown'}`,
          'Everything added since — including rules you added in this app — is discarded.',
        ],
        warnings: ['This cannot be undone from here.'],
      }

    case 'clear-firewall':
      return {
        title: 'Clear every Novimize firewall rule?',
        body: 'Only rules carrying the Novimize prefix are removed.',
        danger: true,
        confirm: 'Clear them',
        lines: [`${status?.firewallRules ?? 0} rule(s) will be removed.`],
        warnings: ['Rules you or another program created are not touched.'],
      }

    case 'import':
      return {
        title: 'Import an export file?',
        body: 'The hosts section in the file replaces the current managed section.',
        danger: true,
        confirm: 'Import',
        lines: ['Firewall rules from the file are recreated under the Novimize prefix.'],
        warnings: ['Whatever is in the managed section now is replaced.'],
      }
  }
}
