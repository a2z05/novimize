import { useState, useEffect } from 'react'
import { invokeJson } from '../hooks/useTauri'
import { Camera, RotateCcw, ChevronDown, ChevronUp, Loader2, Clock, FileText } from 'lucide-react'

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
  const [loading, setLoading] = useState(true)
  const [rollbackLoading, setRollbackLoading] = useState<string | null>(null)

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

  async function rollback(snapshotId: string) {
    setRollbackLoading(snapshotId)
    try {
      await invokeJson('rollback_all', { snapshotId })
      await loadSnapshots()
    } catch (err) {
      console.error('Rollback failed:', err)
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
        {[1,2,3].map(i => <div key={i} className="skeleton h-20" />)}
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="animate-slide-up">
        <h1 className="text-2xl font-bold tracking-tight">Snapshots</h1>
        <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
          Roll back changes from any snapshot
        </p>
      </div>

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
                    onClick={() => setExpandedId(isExpanded ? null : snapshot.id)}
                    className="flex items-center gap-3 text-left flex-1"
                  >
                    <div className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                         style={{ background: 'rgba(99,102,241,0.1)' }}>
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
                    onClick={() => rollback(snapshot.id)}
                    disabled={rollbackLoading === snapshot.id}
                    className="btn btn-sm btn-secondary ml-3"
                  >
                    {rollbackLoading === snapshot.id ? (
                      <Loader2 size={14} className="animate-spin" />
                    ) : (
                      <RotateCcw size={14} />
                    )}
                    Rollback
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
