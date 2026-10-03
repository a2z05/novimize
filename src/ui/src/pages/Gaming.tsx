import { useCallback, useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  FolderPlus,
  FolderX,
  Gamepad2,
  Info,
  Loader2,
  Play,
  RefreshCw,
  Save,
  Square,
  Trash2,
  XCircle,
} from 'lucide-react'
import type {
  GameDetection,
  GameEntry,
  GameModeControl,
  GameModePreset,
  GameModeResult,
  GameModeStatus,
} from '../types'

/** The three schemes every Windows install has. Anything else is typed in the CLI. */
const PLANS = [
  { value: 'high-performance', label: 'High performance' },
  { value: 'balanced', label: 'Balanced' },
  { value: 'power-saver', label: 'Power saver' },
]

type Tab = 'games' | 'presets'

function errMsg(err: unknown): string {
  // §29: never a bare "something went wrong". The string branch is the
  // common one — the CLI already names the action in what it says — and the
  // other two have to say what happened and where it happened.
  if (typeof err === 'string') return err
  if (err instanceof Error) return `${err.name}: ${err.message}`
  return 'Cause: the command returned a result this version cannot read. Affected component: this page.'
}

function stamp(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}

/**
 * One line for one thing a session did, tried to do, or deliberately did not.
 * The three are not interchangeable: "took", "attempted and did not take", and
 * "never attempted" need different words, because only the first two say
 * anything about the machine.
 */
function ControlRow({ control }: { control: GameModeControl }) {
  const state = control.applied ? 'ok' : control.restorable ? 'fail' : 'skip'
  return (
    <div className="flex items-start gap-2 py-1">
      <span className="mt-0.5 flex-shrink-0">
        {state === 'ok' && <CheckCircle2 size={14} className="text-[var(--color-success)]" />}
        {state === 'fail' && <XCircle size={14} className="text-[var(--color-danger)]" />}
        {state === 'skip' && <AlertTriangle size={14} className="text-[var(--color-warning)]" />}
      </span>
      <div className="min-w-0 flex-1">
        <div className="text-[13px] leading-snug">{control.description}</div>
        {control.error ? (
          <div className="text-[11px] text-[var(--color-danger)] break-words">{control.error}</div>
        ) : (
          state === 'fail' && (
            <div className="text-[11px] text-[var(--color-danger)]">Did not take.</div>
          )
        )}
      </div>
    </div>
  )
}

export default function Gaming() {
  const [detection, setDetection] = useState<GameDetection | null>(null)
  const [status, setStatus] = useState<GameModeStatus | null>(null)
  const [presets, setPresets] = useState<GameModePreset[]>([])
  const [result, setResult] = useState<GameModeResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('games')

  // What the next session would do.
  const [plan, setPlan] = useState('high-performance')
  const [suppressNotifications, setSuppressNotifications] = useState(true)
  const [denyBackgroundApps, setDenyBackgroundApps] = useState(true)
  const [services, setServices] = useState('')
  const [forProcess, setForProcess] = useState('')
  const [selected, setSelected] = useState<GameEntry | null>(null)

  const [folderInput, setFolderInput] = useState('')
  const [presetName, setPresetName] = useState('')

  const load = useCallback(async () => {
    const [det, st, ps] = await Promise.all([
      invokeJson<GameDetection>('gaming_detect'),
      invokeJson<GameModeStatus>('gaming_status'),
      invokeJson<GameModePreset[]>('gaming_preset', { action: 'list' }),
    ])
    setDetection(det)
    setStatus(st)
    setPresets(ps)
  }, [])

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

  const active = !!status?.active
  // A selection that no longer exists in the list is not a selection — refresh
  // must not go on passing a path the user can no longer see.
  const selectedPath =
    selected && detection?.games.some(g => g.installPath === selected.installPath)
      ? selected.installPath
      : null

  async function startSession() {
    setResult(null)
    await run('start', async () => {
      const res = await invokeJson<GameModeResult>('gaming_start', {
        plan,
        noNotifications: !suppressNotifications,
        noBackgroundApps: !denyBackgroundApps,
        services: services.trim() || null,
        forProcess: forProcess.trim() || null,
        game: selectedPath,
        controls: null,
      })
      setResult(res)
      if (res.status) setStatus(res.status)
    })
  }

  async function stopSession() {
    setResult(null)
    await run('stop', async () => {
      const res = await invokeJson<GameModeResult>('gaming_stop')
      setResult(res)
      if (res.status) setStatus(res.status)
      else setStatus(await invokeJson<GameModeStatus>('gaming_status'))
    })
  }

  async function addFolder() {
    const path = folderInput.trim()
    if (!path) return
    await run('folder', async () => {
      await invokeJson('gaming_folder', { action: 'add', path })
      setFolderInput('')
      await load()
    })
  }

  async function removeFolder(path: string) {
    await run('folder', async () => {
      await invokeJson('gaming_folder', { action: 'remove', path })
      await load()
    })
  }

  async function savePreset() {
    const name = presetName.trim()
    if (!name) return
    await run('preset-save', async () => {
      await invokeJson('gaming_preset', {
        action: 'save',
        name,
        plan,
        noNotifications: !suppressNotifications,
        noBackgroundApps: !denyBackgroundApps,
        services: services.trim() || null,
        forProcess: forProcess.trim() || null,
        game: selectedPath,
      })
      setPresetName('')
      await load()
    })
  }

  async function applyPreset(name: string) {
    setResult(null)
    await run('preset-apply', async () => {
      const res = await invokeJson<GameModeResult>('gaming_preset', { action: 'apply', name })
      setResult(res)
      if (res.status) setStatus(res.status)
    })
  }

  async function deletePreset(name: string) {
    await run('preset-delete', async () => {
      await invokeJson('gaming_preset', { action: 'delete', name })
      await load()
    })
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: 'games', label: 'Games' },
    { id: 'presets', label: 'Presets' },
  ]

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Gaming Center</h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Temporary sessions that put every value back when they end
          </p>
        </div>
        <button
          className="btn btn-secondary btn-sm"
          onClick={() => void run('load', load)}
          disabled={busy !== null}
        >
          {busy === 'load'
            ? <Loader2 size={14} className="animate-spin" />
            : <RefreshCw size={14} />}
          Refresh
        </button>
      </div>

      {error && (
        <div className="card flex items-start gap-3 border-[var(--color-danger)] animate-slide-up stagger-1">
          <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          <div className="text-[13px] text-[var(--color-danger)] break-words">{error}</div>
        </div>
      )}

      {/* ── Session ─────────────────────────────────────────────── */}
      <div
        className="card animate-slide-up stagger-1"
        style={active ? { borderColor: 'rgba(34,197,94,0.4)' } : undefined}
      >
        <div className="flex flex-wrap items-center gap-3">
          <div
            className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
            style={{
              background: active
                ? 'rgba(34,197,94,0.15)'
                : 'linear-gradient(135deg, rgba(99,102,241,0.15), rgba(168,85,247,0.15))',
            }}
          >
            <Gamepad2 size={19} className={active ? 'text-[var(--color-success)]' : 'text-[var(--color-primary)]'} />
          </div>
          <div className="flex-1 min-w-[180px]">
            <div className="text-[13px] font-semibold flex items-center gap-2">
              {active ? 'Game mode active' : 'Game mode is off'}
              {active && (
                <span className="badge badge-safe">live</span>
              )}
            </div>
            <div className="text-[11px] text-[var(--color-text-muted)]">
              {active ? (
                <>Started {stamp(status?.startedAt)}</>
              ) : (
                'Nothing has been changed. Every control is captured first and put back when you stop.'
              )}
            </div>
          </div>
          {active ? (
            <button
              className="btn btn-danger"
              onClick={() => void stopSession()}
              disabled={busy !== null}
            >
              {busy === 'stop'
                ? <Loader2 size={15} className="animate-spin" />
                : <Square size={15} />}
              Stop game mode
            </button>
          ) : (
            <button
              className="btn btn-primary"
              onClick={() => void startSession()}
              disabled={busy !== null}
            >
              {busy === 'start'
                ? <Loader2 size={15} className="animate-spin" />
                : <Play size={15} />}
              Start game mode
            </button>
          )}
        </div>

        {selectedPath && !active && (
          <div className="mt-3 flex items-center gap-2 text-[11px] text-[var(--color-text-muted)]">
            <span className="badge badge-optional">for this game</span>
            <span className="truncate font-mono">{selectedPath}</span>
            <button
              className="ml-auto btn btn-ghost btn-sm"
              onClick={() => setSelected(null)}
            >
              Clear
            </button>
          </div>
        )}

        {active && status?.session?.gamePath && (
          <div className="mt-3 text-[11px] text-[var(--color-text-muted)] font-mono truncate">
            {status.session.gamePath}
          </div>
        )}

        {active && (status?.controls?.length ?? 0) > 0 && (
          <div className="mt-4 pt-3 border-t border-[var(--color-border)]">
            <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
              In force
            </div>
            {status!.controls.map(c => (
              <ControlRow key={`${c.kind}:${c.target}`} control={c} />
            ))}
          </div>
        )}
      </div>

      {/* What the last start or stop reported, including the ones it refused. */}
      {result && (
        <div
          className="card animate-slide-up"
          style={{
            borderColor: result.success
              ? 'rgba(34,197,94,0.35)'
              : 'rgba(245,158,11,0.35)',
          }}
        >
          <div className="flex items-start gap-3">
            {result.success
              ? <CheckCircle2 size={16} className="text-[var(--color-success)] mt-0.5 flex-shrink-0" />
              : <AlertTriangle size={16} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />}
            <div className="min-w-0 flex-1">
              <div className="text-[13px] font-semibold">{result.message}</div>
              {result.controls.length > 0 && (
                <div className="mt-1">
                  {result.controls.map(c => (
                    <ControlRow key={`${c.kind}:${c.target}`} control={c} />
                  ))}
                </div>
              )}
            </div>
            <button
              className="btn btn-ghost btn-sm flex-shrink-0"
              onClick={() => setResult(null)}
            >
              Dismiss
            </button>
          </div>
        </div>
      )}

      {/* ── Controls for the next session ───────────────────────── */}
      <div className="card animate-slide-up stagger-2">
        <div className="flex items-center gap-2 mb-4">
          <Info size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">What the session will change</h3>
          {active && (
            <span className="ml-auto text-[11px] text-[var(--color-text-muted)]">
              A session is running — stop it to change these.
            </span>
          )}
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <label className="block">
            <span className="block text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1.5">
              Power plan
            </span>
            <select
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
              value={plan}
              onChange={e => setPlan(e.target.value)}
              disabled={active}
            >
              {PLANS.map(p => (
                <option key={p.value} value={p.value}>{p.label}</option>
              ))}
            </select>
            <span className="block text-[11px] text-[var(--color-text-muted)] mt-1">
              Switched back to the plan that was active when you stop.
            </span>
          </label>

          <label className="block">
            <span className="block text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1.5">
              Raise priority for
            </span>
            <input
              className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
              placeholder="Game process name, without .exe"
              value={forProcess}
              onChange={e => setForProcess(e.target.value)}
              disabled={active}
            />
            <span className="block text-[11px] text-[var(--color-text-muted)] mt-1">
              Ends by itself when that process exits — nothing to put back.
            </span>
          </label>
        </div>

        <div className="mt-4 space-y-2">
          <div className="flex items-center justify-between gap-3 rounded-lg border border-[var(--color-border)] bg-[rgba(255,255,255,0.02)] px-3 py-2.5">
            <div className="min-w-0">
              <div className="text-[13px] font-medium">Suppress toast notifications</div>
              <div className="text-[11px] text-[var(--color-text-muted)]">
                Machine policy — needs administrator rights.
              </div>
            </div>
            <div
              className={`toggle ${suppressNotifications ? 'active' : ''}`}
              onClick={() => !active && setSuppressNotifications(v => !v)}
            />
          </div>

          <div className="flex items-center justify-between gap-3 rounded-lg border border-[var(--color-border)] bg-[rgba(255,255,255,0.02)] px-3 py-2.5">
            <div className="min-w-0">
              <div className="text-[13px] font-medium">Deny background apps</div>
              <div className="text-[11px] text-[var(--color-text-muted)]">
                Machine policy — needs administrator rights.
              </div>
            </div>
            <div
              className={`toggle ${denyBackgroundApps ? 'active' : ''}`}
              onClick={() => !active && setDenyBackgroundApps(v => !v)}
            />
          </div>
        </div>

        <label className="block mt-4">
          <span className="block text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1.5">
            Stop these services for the session
          </span>
          <input
            className="w-full bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
            placeholder="SysMain, DiagTrack"
            value={services}
            onChange={e => setServices(e.target.value)}
            disabled={active}
          />
          <span className="block text-[11px] text-[var(--color-text-muted)] mt-1">
            Comma-separated. Only services the security guard already allows; each one is started
            again on stop, and only if it was running when the session captured it.
          </span>
        </label>
      </div>

      {/* ── Tabs ────────────────────────────────────────────────── */}
      <div className="flex gap-1 bg-[var(--color-bg-card)] p-1 rounded-xl border border-[var(--color-border)] animate-slide-up stagger-3">
        {tabs.map(({ id, label }) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`relative flex-1 px-4 py-2.5 rounded-lg text-[13px] font-medium transition-all duration-250 ${
              tab === id
                ? 'text-white shadow-lg'
                : 'text-[var(--color-text-muted)] hover:text-[var(--color-text)] hover:bg-[rgba(255,255,255,0.03)]'
            }`}
            style={tab === id ? {
              background: 'linear-gradient(135deg, #6366F1, #7C3AED)',
              boxShadow: '0 4px 16px rgba(99,102,241,0.3), 0 0 0 1px rgba(99,102,241,0.2)',
            } : {}}
          >
            {label}
          </button>
        ))}
      </div>

      {busy === 'load' && !detection && (
        <div className="card flex items-center justify-center py-12">
          <Loader2 size={24} className="animate-spin text-[var(--color-primary)]" />
          <span className="ml-3 text-[var(--color-text-muted)]">Looking for your games...</span>
        </div>
      )}

      {/* ── Games ───────────────────────────────────────────────── */}
      {tab === 'games' && detection && (
        <div className="space-y-4">
          {detection.warnings.length > 0 && (
            <div className="card animate-slide-up">
              <div className="flex items-center gap-2 mb-2">
                <AlertTriangle size={14} className="text-[var(--color-warning)]" />
                <h3 className="text-[13px] font-semibold">Worth knowing</h3>
              </div>
              {detection.warnings.map((w, i) => (
                <div key={i} className="text-[12px] text-[var(--color-text-muted)] py-0.5">{w}</div>
              ))}
            </div>
          )}

          {/* Launchers */}
          <div className="animate-slide-up stagger-1">
            <div className="flex items-center gap-2 mb-2">
              <h3 className="text-[13px] font-semibold">Launchers</h3>
              <span className="text-[11px] text-[var(--color-text-muted)]">
                {detection.launchers.filter(l => l.detected).length} of {detection.launchers.length} found
              </span>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
              {detection.launchers.map(l => (
                <div key={l.id} className="card py-3">
                  <div className="flex items-center gap-2">
                    {l.detected
                      ? <CheckCircle2 size={14} className="text-[var(--color-success)] flex-shrink-0" />
                      : <span className="w-3.5 h-3.5 rounded-full border border-[var(--color-border)] flex-shrink-0" />}
                    <span className={`text-[13px] font-semibold truncate ${l.detected ? '' : 'text-[var(--color-text-muted)]'}`}>
                      {l.name}
                    </span>
                    {l.detected && <span className="ml-auto badge badge-safe">found</span>}
                  </div>
                  <div
                    className="text-[11px] text-[var(--color-text-muted)] font-mono mt-1.5 truncate"
                    title={l.evidence ?? undefined}
                  >
                    {l.evidence ?? 'not installed'}
                  </div>
                  {l.installPath && (
                    <div className="text-[11px] text-[var(--color-text-muted)] truncate" title={l.installPath}>
                      {l.installPath}
                    </div>
                  )}
                  {l.libraries.length > 0 && (
                    <div className="mt-2 pt-2 border-t border-[var(--color-border)] space-y-0.5">
                      {l.libraries.map(lib => (
                        <div key={lib} className="text-[11px] text-[var(--color-text-muted)] truncate" title={lib}>
                          {lib}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          {/* Games */}
          <div className="card p-0 overflow-hidden animate-slide-up stagger-2">
            <div className="flex items-center gap-2 px-4 py-3 border-b border-[var(--color-border)]">
              <Gamepad2 size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Games</h3>
              <span className="text-[11px] text-[var(--color-text-muted)]">
                {detection.games.length} found
              </span>
              <span className="ml-auto text-[11px] text-[var(--color-text-muted)]">
                Click one to aim the next session at it
              </span>
            </div>

            {detection.games.length === 0 && (
              <div className="px-4 py-8 text-center text-[13px] text-[var(--color-text-muted)]">
                No games found. Add a folder below and the scanner will look inside it.
              </div>
            )}

            {detection.games.map(g => {
              const isSel = !!selectedPath && g.installPath === selectedPath
              return (
                <button
                  key={`${g.launcher}:${g.installPath ?? g.name}`}
                  onClick={() => setSelected(isSel ? null : g)}
                  className={`w-full text-left px-4 py-2.5 border-b border-[var(--color-border)] last:border-0 flex items-center gap-3 transition-colors ${
                    isSel ? 'bg-[rgba(99,102,241,0.10)]' : 'hover:bg-[rgba(255,255,255,0.03)]'
                  }`}
                >
                  <span className={`badge flex-shrink-0 ${g.source === 'manifest' ? 'badge-safe' : 'badge-optional'}`}>
                    {g.source === 'manifest' ? 'manifest' : 'candidate'}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="text-[13px] font-medium truncate">{g.name}</div>
                    {g.installPath && (
                      <div className="text-[11px] text-[var(--color-text-muted)] font-mono truncate" title={g.installPath}>
                        {g.installPath}
                      </div>
                    )}
                  </div>
                  <span className="text-[11px] text-[var(--color-text-muted)] flex-shrink-0">{g.launcher}</span>
                </button>
              )
            })}
          </div>

          <div className="text-[11px] text-[var(--color-text-muted)] px-1">
            <span className="badge badge-safe mr-1">manifest</span>
            the launcher said so — the name comes from its own records
            <span className="badge badge-optional mx-1 ml-3">candidate</span>
            a folder walk found an executable — a guess, and labelled as one
          </div>

          {/* Folders */}
          <div className="card animate-slide-up stagger-3">
            <div className="flex items-center gap-2 mb-3">
              <FolderPlus size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Your game folders</h3>
              <span className="text-[11px] text-[var(--color-text-muted)]">
                scanned for executables, everything found here is a candidate
              </span>
            </div>
            <div className="flex gap-2">
              <input
                className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
                placeholder="D:\Games"
                value={folderInput}
                onChange={e => setFolderInput(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') void addFolder() }}
              />
              <button
                className="btn btn-secondary"
                onClick={() => void addFolder()}
                disabled={busy !== null || !folderInput.trim()}
              >
                {busy === 'folder'
                  ? <Loader2 size={14} className="animate-spin" />
                  : <FolderPlus size={14} />}
                Add
              </button>
            </div>

            {detection.folders.length > 0 && (
              <div className="mt-3 pt-3 border-t border-[var(--color-border)] space-y-1">
                {detection.folders.map(folder => (
                  <div key={folder} className="flex items-center gap-2 py-1">
                    <span className="text-[12px] font-mono truncate flex-1" title={folder}>{folder}</span>
                    <button
                      className="btn btn-ghost btn-sm"
                      onClick={() => void removeFolder(folder)}
                      disabled={busy !== null}
                    >
                      <FolderX size={13} />
                      Remove
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {/* ── Presets ─────────────────────────────────────────────── */}
      {tab === 'presets' && (
        <div className="space-y-4">
          <div className="card animate-slide-up">
            <div className="flex items-center gap-2 mb-3">
              <Save size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Save these controls as a preset</h3>
            </div>
            <div className="flex gap-2">
              <input
                className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
                placeholder="Preset name, e.g. Nightly"
                value={presetName}
                onChange={e => setPresetName(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') void savePreset() }}
              />
              <button
                className="btn btn-primary"
                onClick={() => void savePreset()}
                disabled={busy !== null || !presetName.trim()}
              >
                {busy === 'preset-save'
                  ? <Loader2 size={14} className="animate-spin" />
                  : <Save size={14} />}
                Save
              </button>
            </div>
            <p className="text-[11px] text-[var(--color-text-muted)] mt-2">
              Saves what the controls are — never a previous value. Applying one starts a normal
              session, and stop puts everything back.
            </p>
          </div>

          {presets.length === 0 ? (
            <div className="card flex flex-col items-center justify-center py-10">
              <Save size={22} className="text-[var(--color-text-muted)] mb-2 opacity-60" />
              <p className="text-[13px] font-semibold">No presets yet</p>
              <p className="text-[12px] text-[var(--color-text-muted)]">
                Set the controls above, give them a name, and they can be started with one click.
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              {presets.map((p, i) => (
                <div
                  key={p.name}
                  className={`card animate-slide-up stagger-${Math.min(i + 1, 5)}`}
                >
                  <div className="flex flex-wrap items-center gap-3">
                    <div className="flex-1 min-w-[160px]">
                      <div className="text-[13px] font-semibold">{p.name}</div>
                      <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">
                        {p.plan ?? 'default plan'} · notifications {p.notifications ? 'suppressed' : 'left alone'} ·
                        background apps {p.backgroundApps ? 'denied' : 'left alone'}
                      </div>
                      {p.gamePath && (
                        <div className="text-[11px] text-[var(--color-text-muted)] font-mono truncate" title={p.gamePath}>
                          {p.gamePath}
                        </div>
                      )}
                      {p.services.length > 0 && (
                        <div className="text-[11px] text-[var(--color-text-muted)]">
                          stops: {p.services.join(', ')}
                        </div>
                      )}
                      {p.priorityProcess && (
                        <div className="text-[11px] text-[var(--color-text-muted)]">
                          raises priority of {p.priorityProcess}
                        </div>
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <button
                        className="btn btn-primary btn-sm"
                        onClick={() => void applyPreset(p.name)}
                        disabled={busy !== null || active}
                        title={active ? 'A session is already running.' : undefined}
                      >
                        {busy === 'preset-apply'
                          ? <Loader2 size={13} className="animate-spin" />
                          : <Play size={13} />}
                        Apply
                      </button>
                      <button
                        className="btn btn-ghost btn-sm"
                        onClick={() => void deletePreset(p.name)}
                        disabled={busy !== null}
                      >
                        <Trash2 size={13} />
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
