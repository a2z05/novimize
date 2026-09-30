import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { invokeJson } from '../hooks/useTauri'
import ScoreRing from '../components/ScoreRing'
import TierBadge from '../components/TierBadge'
import {
  ScanSearch,
  RotateCcw,
  Monitor,
  Cpu,
  MemoryStick,
  HardDrive,
  Gamepad2,
  Shield,
} from 'lucide-react'

interface SystemInfo {
  osCaption: string
  osVersion: string
  buildNumber: number
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

export default function Dashboard() {
  const navigate = useNavigate()
  const [systemInfo, setSystemInfo] = useState<SystemInfo | null>(null)
  const [score, setScore] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { loadData() }, [])

  async function loadData() {
    try {
      const sysInfo = await invokeJson<SystemInfo>('get_system_info')
      setSystemInfo(sysInfo)
      // Get real optimization score from recommendations
      const recs = await invokeJson<any[]>('get_recommendations', {})
      if (recs?.length) {
        // Score = inverse of how many tweaks need applying (0 = all applied, 100 = none applied)
        const totalRecs = recs.length
        // Use the first item's score as base, or compute from tier
        const avgScore = recs.reduce((sum: number, r: any) => sum + (r.score || 50), 0) / totalRecs
        setScore(Math.round(avgScore))
      } else {
        setScore(80) // No recommendations = system well optimized
      }
    } catch (err: any) {
      console.error('Failed to load system info:', err)
      setError(err?.toString() || 'Failed to load system info')
    } finally {
      setLoading(false)
    }
  }

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="skeleton h-8 w-48" />
        <div className="grid grid-cols-3 gap-4">
          <div className="skeleton h-48 col-span-2" />
          <div className="skeleton h-48" />
        </div>
        <div className="grid grid-cols-3 gap-4">
          {[1, 2, 3].map(i => <div key={i} className="skeleton h-24" />)}
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="space-y-6">
        <div className="animate-slide-up">
          <h1 className="text-2xl font-bold tracking-tight">Dashboard</h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">System overview and quick actions</p>
        </div>
        <div className="card flex flex-col items-center justify-center py-12">
          <p className="text-[var(--color-danger)] font-semibold mb-2">Failed to load system info</p>
          <p className="text-[12px] text-[var(--color-text-muted)]">{error}</p>
          <button onClick={() => { setLoading(true); setError(null); loadData() }} className="btn btn-secondary mt-4">
            Retry
          </button>
        </div>
      </div>
    )
  }

  const hardwareItems = [
    { icon: Cpu, label: 'Processor', name: systemInfo?.cpuName, sub: `${systemInfo?.cpuCores} cores`, tier: systemInfo?.cpuTier, color: '#6366F1' },
    { icon: MemoryStick, label: 'Memory', name: `${systemInfo?.ramTotalGb} GB`, sub: 'RAM', tier: systemInfo?.ramTier, color: '#3B82F6' },
    { icon: HardDrive, label: 'Storage', name: systemInfo?.primaryStorageModel, sub: systemInfo?.primaryStorageType, tier: systemInfo?.storageTier, color: '#22C55E' },
    { icon: Gamepad2, label: 'Graphics', name: systemInfo?.gpuName, sub: systemInfo?.gpuRamMb ? `${(systemInfo.gpuRamMb / 1024).toFixed(0)} GB VRAM` : '', tier: systemInfo?.gpuTier, color: '#A855F7' },
  ]

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="animate-slide-up">
        <h1 className="text-2xl font-bold tracking-tight">Dashboard</h1>
        <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">System overview and quick actions</p>
      </div>

      {/* Main grid: System Info + Score */}
      <div className="grid grid-cols-3 gap-4">
        {/* System Info */}
        <div className="card col-span-2 animate-slide-up stagger-1">
          <div className="flex items-center gap-2 mb-4">
            <div className="w-8 h-8 rounded-lg flex items-center justify-center"
                 style={{ background: 'linear-gradient(135deg, rgba(99,102,241,0.15), rgba(168,85,247,0.15))' }}>
              <Monitor size={16} className="text-[var(--color-primary)]" />
            </div>
            <h2 className="font-semibold text-[14px]">System Information</h2>
            <TierBadge tier={['', 'Ultra', 'High', 'Mid', 'Low', 'VeryLow'][systemInfo?.overallTier ?? 3]} />
          </div>

          <div className="grid grid-cols-2 gap-3">
            {hardwareItems.map((item, i) => (
              <div key={item.label}
                   className="flex items-start gap-2.5 rounded-xl p-3 transition-all duration-200 hover:bg-[rgba(255,255,255,0.03)] hover:shadow-[inset_0_0_20px_rgba(99,102,241,0.04)] group"
                   style={{ animation: `slideUp 300ms ease-out ${(i + 2) * 60}ms both` }}>
                <div className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 mt-0.5 transition-all duration-200 group-hover:scale-110"
                     style={{ background: `${item.color}15`, boxShadow: `0 0 0 1px ${item.color}10` }}>
                  <item.icon size={15} style={{ color: item.color }} />
                </div>
                <div className="min-w-0">
                  <div className="text-[11px] text-[var(--color-text-muted)]">{item.label}</div>
                  <div className="text-[13px] font-semibold truncate">{item.name}</div>
                  <div className="flex items-center gap-1.5 mt-0.5">
                    <span className="text-[10px] text-[var(--color-text-muted)]">{item.sub}</span>
                    {item.tier ? <TierBadge tier={['', 'Ultra', 'High', 'Mid', 'Low', 'VeryLow'][item.tier] || 'Mid'} /> : null}
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* Footer meta */}
          <div className="mt-3 pt-3 border-t border-[var(--color-border-subtle)] flex items-center gap-3 text-[11px] text-[var(--color-text-muted)]">
            <span>{systemInfo?.osCaption} ({systemInfo?.osVersion})</span>
            <span className="opacity-30">·</span>
            <span>{systemInfo?.isLaptop ? 'Laptop' : 'Desktop'}</span>
            <span className="opacity-30">·</span>
            <span>{systemInfo?.activePowerPlan}</span>
          </div>
        </div>

        {/* Score Card */}
        <div className="card flex flex-col items-center justify-center animate-slide-up stagger-2 relative overflow-hidden">
          {/* Background glow */}
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
            <div className="w-40 h-40 rounded-full opacity-20"
                 style={{
                   background: score >= 70
                     ? 'radial-gradient(circle, rgba(34,197,94,0.4) 0%, transparent 70%)'
                     : score >= 40
                     ? 'radial-gradient(circle, rgba(245,158,11,0.4) 0%, transparent 70%)'
                     : 'radial-gradient(circle, rgba(239,68,68,0.4) 0%, transparent 70%)',
                 }} />
          </div>
          <div className="relative z-10">
            <ScoreRing score={score} size={160} />
          </div>
          <p className="mt-3 text-[13px] text-[var(--color-text-muted)] relative z-10">Optimization Score</p>
          <p className="text-[11px] text-[var(--color-text-muted)] mt-0.5 relative z-10 opacity-70">
            {score >= 70 ? 'System is well optimized' : score >= 40 ? 'Room for improvement' : 'Significant gains available'}
          </p>
        </div>
      </div>

      {/* Quick Actions */}
      <div className="grid grid-cols-3 gap-4">
        {[
          {
            icon: ScanSearch, label: 'Scan System', desc: 'Detect tweak states',
            color: '#6366F1', gradient: 'linear-gradient(135deg, rgba(99,102,241,0.12), rgba(168,85,247,0.08))',
            hoverBorder: 'rgba(99,102,241,0.4)', onClick: () => navigate('/scan'), delay: 'stagger-3',
          },
          {
            icon: Shield, label: 'Auto Optimize', desc: 'Apply safe tweaks',
            color: '#22C55E', gradient: 'linear-gradient(135deg, rgba(34,197,94,0.12), rgba(16,185,129,0.08))',
            hoverBorder: 'rgba(34,197,94,0.4)', onClick: () => navigate('/scan'), delay: 'stagger-4',
          },
          {
            icon: RotateCcw, label: 'Rollback', desc: 'Undo changes',
            color: '#F59E0B', gradient: 'linear-gradient(135deg, rgba(245,158,11,0.12), rgba(217,119,6,0.08))',
            hoverBorder: 'rgba(245,158,11,0.4)', onClick: () => navigate('/snapshots'), delay: 'stagger-5',
          },
        ].map(action => (
          <button
            key={action.label}
            onClick={action.onClick}
            className={`card card-interactive flex items-center gap-3 animate-slide-up ${action.delay} group`}
          >
            <div className="w-11 h-11 rounded-xl flex items-center justify-center flex-shrink-0 transition-all duration-200 group-hover:scale-110 group-hover:shadow-lg"
                 style={{ background: action.gradient }}>
              <action.icon size={20} style={{ color: action.color }} />
            </div>
            <div className="text-left">
              <div className="text-[13px] font-semibold">{action.label}</div>
              <div className="text-[11px] text-[var(--color-text-muted)]">{action.desc}</div>
            </div>
          </button>
        ))}
      </div>
    </div>
  )
}
