import { useState, useEffect, useRef } from 'react'
import { ScanSearch, CheckCircle2, AlertTriangle, AlertCircle, RefreshCw } from 'lucide-react'
import { invokeJson } from '../../hooks/useTauri'

interface ScanStepProps {
  onNext: () => void
}

interface ScanResult {
  tweakId: string
  state: string
  currentValue: string | null
  message: string | null
  detectionSucceeded: boolean
}

export default function ScanStep({ onNext }: ScanStepProps) {
  const [scanning, setScanning] = useState(false)
  const [done, setDone] = useState(false)
  const [results, setResults] = useState<ScanResult[]>([])
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  useEffect(() => {
    startScan()
    return () => { if (intervalRef.current) clearInterval(intervalRef.current) }
  }, [])

  async function startScan() {
    setScanning(true)
    setDone(false)
    setResults([])
    setError(null)
    setProgress(0)
    intervalRef.current = setInterval(() => {
      setProgress(p => Math.min(p + Math.random() * 12, 85))
    }, 250)
    try {
      const data = await invokeJson<ScanResult[]>('scan_tweaks')
      if (intervalRef.current) clearInterval(intervalRef.current)
      setProgress(100)
      setResults(data)
      setDone(true)
    } catch (err: any) {
      if (intervalRef.current) clearInterval(intervalRef.current)
      setError(err?.toString() || 'Scan failed')
    } finally {
      setScanning(false)
    }
  }

  const safeCount = results.filter(r => r.state === 'Applied').length
  const needsAction = results.filter(r => r.state !== 'Applied' && r.detectionSucceeded).length

  return (
    <div className="flex flex-col items-center max-w-lg mx-auto w-full">
      <h2
        className="text-2xl font-bold tracking-tight mb-1 text-center"
        style={{ animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        {scanning ? 'Scanning System...' : done ? 'Scan Complete' : error ? 'Scan Issue' : 'Quick Scan'}
      </h2>
      <p
        className="text-[13px] text-[var(--color-text-muted)] mb-6 text-center"
        style={{ animation: 'slideUp 400ms 50ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        {scanning
          ? 'Checking tweak states across your system'
          : done
            ? `${results.length} tweaks analyzed · ${needsAction} need attention`
            : error
              ? 'We ran into a problem during the scan'
              : ''
        }
      </p>

      {/* Progress bar */}
      {(scanning || done) && (
        <div className="w-full mb-6" style={{ animation: 'slideUp 400ms 100ms cubic-bezier(0.16,1,0.3,1) both' }}>
          <div className="progress-bar h-2">
            <div className="progress-bar-fill h-full" style={{ width: `${progress}%`, transition: 'width 0.3s ease' }} />
          </div>
          <div className="text-[11px] text-[var(--color-text-muted)] mt-1.5 text-center">
            {scanning ? `${Math.round(progress)}%` : done ? '100%' : ''}
          </div>
        </div>
      )}

      {/* Scanning animation */}
      {scanning && (
        <div className="flex flex-col items-center py-8" style={{ animation: 'fadeIn 300ms ease-out' }}>
          <div className="relative">
            <ScanSearch size={40} className="text-[var(--color-primary)]" />
            <div
              className="absolute inset-0 rounded-full"
              style={{ animation: 'pulse-ring 2s ease-in-out infinite', border: '2px solid rgba(99,102,241,0.3)' }}
            />
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mt-4">Analyzing system tweaks...</p>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="text-center py-4" style={{ animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both' }}>
          <div className="rounded-2xl px-6 py-5 mb-4" style={{ background: 'rgba(239,68,68,0.08)', border: '1px solid rgba(239,68,68,0.2)' }}>
            <AlertCircle size={28} className="text-[var(--color-danger)] mx-auto mb-2" />
            <p className="text-[13px] text-[var(--color-text-muted)] mb-1">Scan failed</p>
            <p className="text-[12px] text-[var(--color-danger)] opacity-80">{error}</p>
          </div>
          <div className="flex gap-3 justify-center">
            <button onClick={onNext} className="btn btn-secondary px-6 py-2.5 text-[13px]">Skip</button>
            <button onClick={startScan} className="btn btn-primary px-6 py-2.5 text-[13px] gap-2">
              <RefreshCw size={14} />
              Retry
            </button>
          </div>
        </div>
      )}

      {/* Results */}
      {done && !error && (
        <div className="w-full space-y-2 max-h-64 overflow-y-auto pr-1" style={{ animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both' }}>
          {/* Summary cards */}
          <div className="grid grid-cols-2 gap-3 mb-4">
            <div
              className="rounded-xl px-4 py-3 text-center"
              style={{
                background: 'rgba(34,197,94,0.08)',
                border: '1px solid rgba(34,197,94,0.2)',
                animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both',
              }}
            >
              <CheckCircle2 size={18} className="text-[var(--color-success)] mx-auto mb-1" />
              <div className="text-lg font-bold text-[var(--color-success)]">{safeCount}</div>
              <div className="text-[10px] text-[var(--color-text-muted)]">Optimized</div>
            </div>
            <div
              className="rounded-xl px-4 py-3 text-center"
              style={{
                background: needsAction > 0 ? 'rgba(245,158,11,0.08)' : 'rgba(34,197,94,0.08)',
                border: `1px solid ${needsAction > 0 ? 'rgba(245,158,11,0.2)' : 'rgba(34,197,94,0.2)'}`,
                animation: 'slideUp 400ms 100ms cubic-bezier(0.16,1,0.3,1) both',
              }}
            >
              {needsAction > 0 ? (
                <AlertTriangle size={18} className="text-[var(--color-warning)] mx-auto mb-1" />
              ) : (
                <CheckCircle2 size={18} className="text-[var(--color-success)] mx-auto mb-1" />
              )}
              <div className={`text-lg font-bold ${needsAction > 0 ? 'text-[var(--color-warning)]' : 'text-[var(--color-success)]'}`}>
                {needsAction}
              </div>
              <div className="text-[10px] text-[var(--color-text-muted)]">Need Action</div>
            </div>
          </div>

          {/* Tweak list */}
          {results.slice(0, 8).map((r, i) => (
            <div
              key={r.tweakId}
              className="flex items-center gap-2.5 rounded-lg px-3 py-2"
              style={{
                background: 'var(--color-bg-card)',
                border: '1px solid var(--color-border-subtle)',
                animation: `slideUp 300ms ${i * 40}ms cubic-bezier(0.16,1,0.3,1) both`,
              }}
            >
              <div className={`w-2 h-2 rounded-full flex-shrink-0 ${
                r.state === 'Applied'
                  ? 'bg-[var(--color-success)]'
                  : 'bg-[var(--color-warning)]'
              }`} />
              <span className="text-[12px] font-medium truncate flex-1">{r.tweakId}</span>
              <span className="text-[10px] text-[var(--color-text-muted)]">{r.state}</span>
            </div>
          ))}
        </div>
      )}

      {done && !error && (
        <button
          onClick={onNext}
          className="btn btn-primary px-8 py-3 text-[14px] mt-6"
          style={{ animation: 'slideUp 400ms 200ms cubic-bezier(0.16,1,0.3,1) both' }}
        >
          Continue
        </button>
      )}
    </div>
  )
}
