import { useState, useEffect } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  Camera,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Clock,
  FileText,
  Loader2,
  RotateCcw,
} from 'lucide-react'
import type { SnapshotDetail, SnapshotEntry } from '../types'

interface Snapshot {
  id: string
  timestamp: string
  description: string
  entryCount: number
  tweaksApplied: number
  fileSize: number
}

export default function Snapshots() {
  const [snapshots, setSnapshots] = useState<Snapshot[]>([])
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [detail, setDetail] = useState<SnapshotDetail | null>(null)
  const [detailError, setDetailError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [rollbackLoading, setRollbackLoading] = useState<string | null>(null)
  const [result, setResult] = useState<string | null>(null)

  useEffect(() => { loadSnapshots() }, [])

  async function loadSnapshots() {
    try {
      const raw = await invokeJson<Snapshot[]>('get_snapshots')
      setSnapshots(raw || [])
    } catch (err) {
      console.error('Failed to load snapshots:', err)
    } finally {
      setLoading(false)
    }
  }

  /**
   * Opening a snapshot fetches its entries. The diff is not guessed from
   * the tweak list: it is the before-and-after pair the rollback reads, so
   * what you see is what undoing will restore.
   */
  async function toggle(id: string) {
    if (expandedId === id) {
      setExpandedId(null)
      setDetail(null)
      return
    }
    setExpandedId(id)
    setDetail(null)
    setDetailError(null)
    try {
      setDetail(await invokeJson<SnapshotDetail>('snapshot_show', { snapshotId: id }))
    } catch (err) {
      setDetailError(typeof err === 'string' ? err : (err as Error).message)
    }
  }

  /** Undo = the same rollback, offered where the change is being read. */
  async function rollback(snapshotId: string) {
    setRollbackLoading(snapshotId)
    setResult(null)
    try {
      const res = await invokeJson<{ success?: boolean; message?: string }>('rollback_all', {
        snapshotId,
      })
      setResult(res?.message ?? 'Rolled back.')
      setExpandedId(null)
      setDetail(null)
      await loadSnapshots()
    } catch (err) {
      setResult(typeof err === 'string' ? err : (err as Error).message)
    } finally {
      setRollbackLoading(null)
    }
  }

  function formatDate(ts: string) {
    try {
      const d = new Date(ts)
      return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) +
        ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
    } catch { return ts }
  }

  function formatSize(bytes: number) {
    if (bytes < 1024) return `${bytes} B`
    return `${(bytes / 1024).toFixed(1)} KB`
  }

  if (loading) {
    return (
      <div className="space-y-4">
        <div className="skeleton h-8 w-48" />
        {[1, 2, 3].map(i => <div key={i} className="skeleton h-20" />)}
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="animate-slide-up">
        <h1 className="text-2xl font-bold tracking-tight">Snapshots</h1>
        <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
          What each batch changed, before and after — keep it, or undo it
        </p>
      </div>

      {result && (
        <div
          className="card flex items-start gap-3 animate-slide-up stagger-1"
          style={{ borderColor: 'rgba(34,197,94,0.35)' }}
        >
          <CheckCircle2 size={16} className="text-[var(--color-success)] mt-0.5 flex-shrink-0" />
          <div className="text-[13px] break-words flex-1">{result}</div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setResult(null)}>
            Dismiss
          </button>
        </div>
      )}

      {snapshots.length === 0 ? (
        <div className="card flex flex-col items-center justify-center py-16 animate-fade-in">
          <Camera size={40} className="mx-auto text-[var(--color-text-muted)] mb-3 opacity-40" />
          <p className="text-[14px] font-semibold text-[var(--color-text-muted)]">No snapshots yet</p>
          <p className="text-[12px] text-[var(--color-text-muted)] mt-1 opacity-70">
            Snapshots are created automatically when you apply tweaks
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {snapshots.map((snapshot, i) => {
            const isExpanded = expandedId === snapshot.id
            return (
              <div
                key={snapshot.id}
                className="card p-0 overflow-hidden"
                style={{ animation: `slideUp 300ms cubic-bezier(0.16,1,0.3,1) ${i * 50}ms both` }}
              >
                <div className="flex items-center justify-between px-4 py-3">
                  <button
                    onClick={() => void toggle(snapshot.id)}
                    className="flex items-center gap-3 text-left flex-1"
                  >
                    <div
                      className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                      style={{ background: 'rgba(99,102,241,0.1)' }}
                    >
                      <Camera size={16} className="text-[var(--color-primary)]" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="text-[13px] font-semibold truncate">
                        {snapshot.description || 'Snapshot'}
                      </div>
                      <div className="flex items-center gap-2 text-[11px] text-[var(--color-text-muted)] mt-0.5">
                        <Clock size={10} />
                        <span>{formatDate(snapshot.timestamp)}</span>
                        <span className="opacity-30">·</span>
                        <FileText size={10} />
                        <span>{snapshot.entryCount} entries</span>
                        <span className="opacity-30">·</span>
                        <span>{formatSize(snapshot.fileSize)}</span>
                      </div>
                    </div>
                    {isExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                  </button>

                  <button
                    onClick={() => void rollback(snapshot.id)}
                    disabled={rollbackLoading === snapshot.id}
                    className="btn btn-sm btn-secondary ml-3"
                    title="Put every setting in this snapshot back"
                  >
                    {rollbackLoading === snapshot.id ? (
                      <Loader2 size={14} className="animate-spin" />
                    ) : (
                      <RotateCcw size={14} />
                    )}
                    Undo
                  </button>
                </div>

                {isExpanded && (
                  <div className="border-t border-[var(--color-border)] px-4 py-3 space-y-3">
                    {detailError && (
                      <div className="flex items-start gap-2 text-[12px] text-[var(--color-danger)]">
                        <AlertTriangle size={14} className="mt-0.5 flex-shrink-0" />
                        {detailError}
                      </div>
                    )}

                    {!detail && !detailError && (
                      <div className="flex items-center gap-2 text-[12px] text-[var(--color-text-muted)]">
                        <Loader2 size={13} className="animate-spin" /> Reading what changed…
                      </div>
                    )}

                    {detail && detail.entries.length === 0 && (
                      <div className="text-[12px] text-[var(--color-text-muted)]">
                        This snapshot recorded no individual settings — it was taken before anything
                        was written.
                      </div>
                    )}

                    {detail && detail.entries.length > 0 && (
                      <>
                        <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]">
                          Before → after
                        </div>
                        <div className="space-y-1.5 max-h-72 overflow-y-auto pr-1">
                          {detail.entries.map((entry, index) => (
                            <DiffRow key={`${entry.target}-${index}`} entry={entry} />
                          ))}
                        </div>
                      </>
                    )}

                    <div className="flex justify-end gap-2 pt-1">
                      <button
                        className="btn btn-secondary btn-sm"
                        onClick={() => {
                          setExpandedId(null)
                          setDetail(null)
                          setResult('Kept. Nothing was changed.')
                        }}
                      >
                        <CheckCircle2 size={13} />
                        Keep changes
                      </button>
                      <button
                        className="btn btn-danger btn-sm"
                        onClick={() => void rollback(snapshot.id)}
                        disabled={rollbackLoading === snapshot.id}
                      >
                        {rollbackLoading === snapshot.id ? (
                          <Loader2 size={13} className="animate-spin" />
                        ) : (
                          <RotateCcw size={13} />
                        )}
                        Undo this batch
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

/**
 * One setting, before and after. The pair comes from the snapshot itself —
 * the same values the rollback restores — so the two views cannot disagree
 * about what the change was.
 */
function DiffRow({ entry }: { entry: SnapshotEntry }) {
  const changed = entry.oldValue !== entry.newValue
  return (
    <div className="rounded-lg border border-[var(--color-border)] px-3 py-2">
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-[12px] font-medium truncate">{entry.tweakId}</span>
        <span className="badge badge-optional">{entry.method}</span>
        {!entry.verified && <span className="badge badge-experimental">not verified</span>}
      </div>
      <div className="text-[11px] font-mono text-[var(--color-text-muted)] mt-0.5 break-all">
        {entry.target}
      </div>
      {changed && (
        <div className="flex flex-wrap items-baseline gap-2 mt-1 text-[12px] font-mono">
          <span className="text-[11px] uppercase tracking-wider text-[var(--color-text-muted)]">
            before
          </span>
          <span className="line-through opacity-60 break-all">{entry.oldValue ?? '(not set)'}</span>
          <span className="text-[var(--color-text-muted)]">→</span>
          <span className="text-[11px] uppercase tracking-wider text-[var(--color-text-muted)]">
            after
          </span>
          <span className="text-[var(--color-success)] break-all">
            {entry.newValue ?? '(not set)'}
          </span>
        </div>
      )}
      {!changed && (
        <div className="text-[11px] text-[var(--color-text-muted)] mt-1">
          no recorded change — {entry.oldValue ?? '(not set)'}
        </div>
      )}
    </div>
  )
}
