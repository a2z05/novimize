import { useState, useEffect } from 'react'
import { Cpu, MemoryStick, HardDrive, Gamepad2, Monitor, Loader2, CheckCircle2, AlertCircle, RefreshCw } from 'lucide-react'
import { invokeJson } from '../../hooks/useTauri'

interface SystemInfo {
  osCaption: string
  osVersion: string
  cpuName: string
  cpuCores: number
  cpuTier: number
  ramTotalGb: number
  ramTier: number
  primaryStorageType: number
  primaryStorageModel: string
  storageTier: number
  gpuName: string
  gpuVendor: string
  gpuRamMb: number
  gpuTier: number
  overallTier: number
  formFactor: number
  isLaptop: boolean
  activePowerPlan: string
}

interface DetectStepProps {
  onNext: () => void
  systemInfo: SystemInfo | null
  setSystemInfo: (info: SystemInfo) => void
}

const DETECT_ITEMS = [
  { key: 'cpu', icon: Cpu, label: 'Processor', color: '#6366F1', delay: 0 },
  { key: 'ram', icon: MemoryStick, label: 'Memory', color: '#3B82F6', delay: 100 },
  { key: 'gpu', icon: Gamepad2, label: 'Graphics', color: '#A855F7', delay: 200 },
  { key: 'storage', icon: HardDrive, label: 'Storage', color: '#22C55E', delay: 300 },
  { key: 'os', icon: Monitor, label: 'Operating System', color: '#F59E0B', delay: 400 },
]

function tierName(tier: number): string {
  switch (tier) {
    case 1: return 'Ultra'
    case 2: return 'High'
    case 3: return 'Mid'
    case 4: return 'Low'
    case 5: return 'Very Low'
    default: return 'Unknown'
  }
}

function formatValue(info: SystemInfo | null, key: string): { main: string; sub: string } {
  if (!info) return { main: '—', sub: '' }
  switch (key) {
    case 'cpu': return { main: info.cpuName, sub: `${info.cpuCores} cores · ${tierName(info.cpuTier)}` }
    case 'ram': return { main: `${info.ramTotalGb} GB`, sub: tierName(info.ramTier) }
    case 'gpu': return { main: info.gpuName || 'Integrated', sub: info.gpuRamMb ? `${(info.gpuRamMb / 1024).toFixed(0)} GB VRAM` : '' }
    case 'storage': return { main: info.primaryStorageModel, sub: tierName(info.storageTier) }
    case 'os': return { main: info.osCaption, sub: `${info.osVersion} · ${info.isLaptop ? 'Laptop' : 'Desktop'}` }
    default: return { main: '—', sub: '' }
  }
}

export default function DetectStep({ onNext, systemInfo, setSystemInfo }: DetectStepProps) {
  const [loading, setLoading] = useState(!systemInfo)
  const [detectedKeys, setDetectedKeys] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (systemInfo) {
      DETECT_ITEMS.forEach((item, i) => {
        setTimeout(() => setDetectedKeys(prev => [...prev, item.key]), i * 150)
      })
      return
    }
    let cancelled = false
    detectHardware()
    return () => { cancelled = true }

    async function detectHardware() {
      try {
        const info = await invokeJson<SystemInfo>('get_system_info')
        if (cancelled) return
        setSystemInfo(info)
        setLoading(false)
        DETECT_ITEMS.forEach((item, i) => {
          setTimeout(() => {
            if (!cancelled) setDetectedKeys(prev => [...prev, item.key])
          }, i * 200)
        })
      } catch (err: any) {
        if (!cancelled) setError(err?.toString() || 'Failed to detect hardware')
      }
    }
  }, [])

  const allDetected = detectedKeys.length === DETECT_ITEMS.length

  return (
    <div className="flex flex-col items-center max-w-lg mx-auto w-full">
      <h2
        className="text-2xl font-bold tracking-tight mb-1 text-center"
        style={{ animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        {loading ? 'Detecting Hardware...' : error ? 'Detection Issue' : 'System Detected'}
      </h2>
      <p
        className="text-[13px] text-[var(--color-text-muted)] mb-6 text-center"
        style={{ animation: 'slideUp 400ms 50ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        {loading
          ? 'Scanning your PC components'
          : error
            ? 'We had trouble reading your hardware info'
            : `Tier: ${tierName(systemInfo?.overallTier ?? 0)}`
        }
      </p>

      {error ? (
        <div className="text-center" style={{ animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both' }}>
          <div className="rounded-2xl px-6 py-5 mb-6" style={{ background: 'rgba(239,68,68,0.08)', border: '1px solid rgba(239,68,68,0.2)' }}>
            <AlertCircle size={28} className="text-[var(--color-danger)] mx-auto mb-2" />
            <p className="text-[13px] text-[var(--color-text-muted)] mb-1">Something went wrong</p>
            <p className="text-[12px] text-[var(--color-danger)] opacity-80">{error}</p>
          </div>
          <div className="flex gap-3 justify-center">
            <button
              onClick={onNext}
              className="btn btn-secondary px-6 py-2.5 text-[13px] gap-2"
            >
              Skip for now
            </button>
            <button
              onClick={() => { setError(null); setLoading(true); window.location.reload() }}
              className="btn btn-primary px-6 py-2.5 text-[13px] gap-2"
            >
              <RefreshCw size={14} />
              Try again
            </button>
          </div>
        </div>
      ) : (
        <div className="w-full space-y-2">
          {DETECT_ITEMS.map((item) => {
            const detected = detectedKeys.includes(item.key)
            const value = formatValue(systemInfo, item.key)
            return (
              <div
                key={item.key}
                className="flex items-center gap-3 rounded-xl px-4 py-3 transition-all duration-300"
                style={{
                  background: detected ? 'var(--color-bg-card)' : 'transparent',
                  border: `1px solid ${detected ? 'var(--color-border)' : 'var(--color-border-subtle)'}`,
                  opacity: detected ? 1 : 0.4,
                  transform: detected ? 'translateX(0) scale(1)' : 'translateX(-8px) scale(0.98)',
                  boxShadow: detected ? '0 2px 12px rgba(99,102,241,0.06)' : 'none',
                }}
              >
                <div
                  className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 transition-all duration-300"
                  style={{
                    background: `${item.color}15`,
                    boxShadow: detected ? `0 0 12px ${item.color}15` : 'none',
                  }}
                >
                  {loading && !detected ? (
                    <Loader2 size={16} style={{ color: item.color }} className="animate-spin" />
                  ) : (
                    <item.icon size={16} style={{ color: item.color }} className={detected ? 'drop-shadow-[0_0_4px_currentColor]' : ''} />
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="text-[11px] text-[var(--color-text-muted)]">{item.label}</div>
                  <div className="text-[13px] font-semibold truncate">{value.main}</div>
                  {value.sub && <div className="text-[10px] text-[var(--color-text-muted)] mt-0.5">{value.sub}</div>}
                </div>
                {detected && (
                  <div className="flex-shrink-0" style={{ animation: 'checkbox-pop 400ms cubic-bezier(0.34, 1.56, 0.64, 1) both' }}>
                    <CheckCircle2 size={16} className="text-[var(--color-success)]" style={{ filter: 'drop-shadow(0 0 4px rgba(34,197,94,0.4))' }} />
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {!loading && !error && allDetected && (
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
