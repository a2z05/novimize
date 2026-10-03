import { useCallback, useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  Download,
  Gamepad2,
  Loader2,
  Lock,
  Plus,
  RefreshCw,
  ShieldOff,
  Trash2,
  X,
} from 'lucide-react'
import type { DefenderExclusionState, ExclusionChange, GameDetection } from '../types'

/**
 * The standing warning, wherever it is shown. An exclusion is not a preference
 * — it is the scanner being told to look away from a folder, and the sentence
 * that says so belongs next to the path every single time.
 */
const NOT_SCANNED = 'Defender will not scan excluded content.'

function errMsg(err: unknown): string {
  // §29: never a bare "something went wrong". The string branch is the
  // common one — the CLI already names the action in what it says — and the
  // other two have to say what happened and where it happened.
  if (typeof err === 'string') return err
  if (err instanceof Error) return `${err.name}: ${err.message}`
  return 'Cause: the command returned a result this version cannot read. Affected component: this page.'
}

export default function Exclusions() {
  const [state, setState] = useState<DefenderExclusionState | null>(null)
  const [games, setGames] = useState<GameDetection | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<ExclusionChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pathInput, setPathInput] = useState('')
  const [pendingPath, setPendingPath] = useState<string | null>(null)

  const load = useCallback(async () => {
    const [exclusions, detection] = await Promise.all([
      invokeJson<DefenderExclusionState>('defender_list'),
      // Games are offered as one-click candidates, so detection failing must
      // not take the exclusion list down with it.
      invokeJson<GameDetection>('gaming_detect').catch(() => null),
    ])
    setState(exclusions)
    setGames(detection)
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

  const readable = !!state?.readable
  const excluded = new Set((state?.paths ?? []).map(p => p.toLowerCase()))

  async function add(path: string, confirm: boolean) {
    setChange(null)
    await run('add', async () => {
      const res = await invokeJson<ExclusionChange>('defender_change', {
        action: 'add',
        path,
        confirm,
        output: null,
      })
      setChange(res)
      if (res.success && res.after) setState(s => (s ? { ...s, paths: res.after! } : s))
      if (res.success) setPathInput('')
      await load()
    })
  }

  async function remove(path: string) {
    setChange(null)
    await run('remove', async () => {
      const res = await invokeJson<ExclusionChange>('defender_change', {
        action: 'remove',
        path,
        confirm: null,
        output: null,
      })
      setChange(res)
      await load()
    })
  }

  async function exportList() {
    setChange(null)
    await run('export', async () => {
      const res = await invokeJson<{ file: string }>('defender_change', {
        action: 'export',
        path: null,
        confirm: null,
        output: null,
      })
      setChange({
        action: 'export',
        path: res.file,
        success: true,
        unchanged: false,
        message: `Wrote the list to ${res.file}. Add-MpPreference -ExclusionPath (Get-Content <file>) puts it back.`,
        before: state?.paths ?? [],
      })
    })
  }

  const candidates = (games?.games ?? [])
    .filter(g => g.installPath && !excluded.has(g.installPath.toLowerCase()))
    .slice(0, 40)

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <ShieldOff size={22} className="text-[var(--color-warning)]" />
            Defender Exclusions
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Folders and files Defender is told not to scan
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

      {change && (
        <div
          className="card flex items-start gap-3 animate-slide-up stagger-1"
          style={{ borderColor: change.success ? 'rgba(34,197,94,0.35)' : 'rgba(239,68,68,0.4)' }}
        >
          {change.success
            ? <CheckCircle2 size={16} className="text-[var(--color-success)] mt-0.5 flex-shrink-0" />
            : <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />}
          <div className="min-w-0 flex-1">
            <div className="text-[13px] break-words">{change.message}</div>
            {change.action !== 'export' && (
              <div className="text-[11px] text-[var(--color-text-muted)] font-mono mt-0.5 break-all">
                {change.path}
              </div>
            )}
          </div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setChange(null)}>
            Dismiss
          </button>
        </div>
      )}

      {/* ── Elevation ───────────────────────────────────────────── */}
      {state && !readable && (
        <div className="card flex items-start gap-3 border-[var(--color-warning)] animate-slide-up stagger-1">
          <div className="w-9 h-9 rounded-xl bg-amber-500/15 flex items-center justify-center flex-shrink-0">
            <Lock size={17} className="text-[var(--color-warning)]" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="text-[13px] font-semibold">Reading exclusions needs administrator rights</div>
            <div className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
              {state.message ?? 'Defender did not report a list it is willing to show.'}
              {' '}Until then nothing here is a list — it is a refusal, and showing an empty one would
              be a wrong answer rather than a smaller true one.
            </div>
            <div className="text-[12px] text-[var(--color-warning)] mt-1.5">{NOT_SCANNED}</div>
          </div>
          <button
            className="btn btn-secondary btn-sm flex-shrink-0"
            onClick={() => void run('load', load)}
            disabled={busy !== null}
          >
            {busy === 'load'
              ? <Loader2 size={14} className="animate-spin" />
              : <RefreshCw size={14} />}
            Retry
          </button>
        </div>
      )}

      {/* ── Add ─────────────────────────────────────────────────── */}
      {state && readable && (
        <div className="card animate-slide-up stagger-1">
          <div className="flex items-center gap-2 mb-3">
            <Plus size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Exclude a path</h3>
          </div>
          <div className="flex gap-2">
            <input
              className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
              placeholder="D:\Games\SomeGame"
              value={pathInput}
              onChange={e => setPathInput(e.target.value)}
              onKeyDown={e => {
                const p = pathInput.trim()
                if (e.key === 'Enter' && p) setPendingPath(p)
              }}
            />
            <button
              className="btn btn-primary"
              onClick={() => { const p = pathInput.trim(); if (p) setPendingPath(p) }}
              disabled={busy !== null || !pathInput.trim()}
            >
              {busy === 'add'
                ? <Loader2 size={14} className="animate-spin" />
                : <Plus size={14} />}
              Exclude
            </button>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mt-2">
            System locations, your user profile, entire drives and network paths are refused by the
            engine before Defender is ever asked.
          </p>
        </div>
      )}

      {/* ── Current exclusions ──────────────────────────────────── */}
      {state && readable && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-3">
            <ShieldOff size={14} className="text-[var(--color-warning)]" />
            <h3 className="text-[13px] font-semibold">Excluded paths</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{state.paths.length}</span>
            <button
              className="ml-auto btn btn-secondary btn-sm"
              onClick={() => void exportList()}
              disabled={busy !== null || state.paths.length === 0}
              title="Write the list to a file that Add-MpPreference can read back"
            >
              {busy === 'export'
                ? <Loader2 size={13} className="animate-spin" />
                : <Download size={13} />}
              Export
            </button>
          </div>

          <div className="text-[12px] text-[var(--color-warning)] mb-3">{NOT_SCANNED}</div>

          {state.paths.length === 0 ? (
            <div className="text-[13px] text-[var(--color-text-muted)] py-4 text-center">
              Nothing is excluded. Defender scans everything on this machine.
            </div>
          ) : (
            <div className="space-y-1">
              {state.paths.map(path => (
                <div key={path} className="flex items-center gap-2 py-1.5">
                  <span className="text-[12px] font-mono truncate flex-1" title={path}>{path}</span>
                  <button
                    className="btn btn-ghost btn-sm flex-shrink-0"
                    onClick={() => void remove(path)}
                    disabled={busy !== null}
                    title="Remove this exclusion — Defender scans it again"
                  >
                    <Trash2 size={13} />
                    Remove
                  </button>
                </div>
              ))}
            </div>
          )}

          {state.processes.length > 0 && (
            <div className="mt-4 pt-3 border-t border-[var(--color-border)]">
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Excluded processes ({state.processes.length})
              </div>
              {state.processes.map(proc => (
                <div key={proc} className="text-[12px] font-mono text-[var(--color-text-muted)] py-0.5 truncate" title={proc}>
                  {proc}
                </div>
              ))}
              <div className="text-[11px] text-[var(--color-text-muted)] mt-1">
                Added by something other than Novimize; shown so the list is complete.
              </div>
            </div>
          )}
        </div>
      )}

      {/* ── Game candidates ─────────────────────────────────────── */}
      {games && (
        <div className="card animate-slide-up stagger-3">
          <div className="flex items-center gap-2 mb-1">
            <Gamepad2 size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Your games</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {candidates.length} not yet excluded
            </span>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
            Scanning a game folder costs frames for nothing — Defender has no business reading it.
            Each one still asks for confirmation with the exact path.
          </p>

          {candidates.length === 0 ? (
            <div className="text-[13px] text-[var(--color-text-muted)] py-3 text-center">
              Every detected game is already excluded, or none were detected.
            </div>
          ) : (
            <div className="max-h-72 overflow-y-auto pr-1 space-y-1">
              {candidates.map(g => {
                const path = g.installPath!
                return (
                  <div key={path} className="flex items-center gap-2 py-1.5">
                    <span className="text-[13px] truncate flex-1 min-w-0" title={path}>
                      {g.name}
                      <span className="text-[11px] text-[var(--color-text-muted)] ml-2">{g.launcher}</span>
                    </span>
                    {readable ? (
                      <button
                        className="btn btn-secondary btn-sm flex-shrink-0"
                        onClick={() => setPendingPath(path)}
                        disabled={busy !== null}
                      >
                        <Plus size={13} />
                        Exclude
                      </button>
                    ) : (
                      <span className="flex items-center gap-1 text-[11px] text-[var(--color-text-muted)] flex-shrink-0">
                        <Lock size={12} />
                        Requires administrator
                      </span>
                    )}
                  </div>
                )
              })}
            </div>
          )}
        </div>
      )}

      {/* ── Confirmation ────────────────────────────────────────── */}
      {pendingPath !== null && (
        <div className="modal-backdrop" onClick={() => setPendingPath(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div className="w-10 h-10 rounded-xl bg-red-500/15 flex items-center justify-center flex-shrink-0">
                <ShieldOff size={19} className="text-[var(--color-danger)]" />
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">Exclude this path?</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  This turns the scanner off for the folder below.
                </p>
              </div>
              <button
                className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1"
                onClick={() => setPendingPath(null)}
              >
                <X size={16} />
              </button>
            </div>

            <div className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2.5 mb-3">
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Path
              </div>
              <div className="text-[13px] font-mono break-all">{pendingPath}</div>
            </div>

            <div className="flex items-start gap-2 rounded-lg bg-red-500/10 border border-red-500/25 px-3 py-2.5 mb-4">
              <AlertTriangle size={15} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
              <div className="text-[12px] text-[var(--color-danger)]">{NOT_SCANNED}</div>
            </div>

            <div className="flex justify-end gap-2">
              <button className="btn btn-secondary" onClick={() => setPendingPath(null)}>
                Cancel
              </button>
              <button
                className="btn btn-danger"
                onClick={() => {
                  const p = pendingPath
                  setPendingPath(null)
                  void add(p, true)
                }}
                disabled={busy !== null}
              >
                {busy === 'add'
                  ? <Loader2 size={14} className="animate-spin" />
                  : <ShieldOff size={14} />}
                Exclude it
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
