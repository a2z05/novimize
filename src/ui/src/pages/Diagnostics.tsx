import { useState, useEffect } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  Activity,
  CheckCircle2,
  AlertTriangle,
  XCircle,
  Loader2,
  Wifi,
  HardDrive,
  Gauge,
  Shield,
  Cpu,
  Monitor,
  MemoryStick,
  Server,
  Clock3,
  Lock,
} from 'lucide-react'

// ── Types ──────────────────────────────────────────────────────────
interface HealthCheck {
  name: string
  details: string
  status: number // 0=Ok, 1=Warning, 2=Critical
}
interface SystemInfoData {
  osCaption?: string
  osEdition?: string
  buildNumber?: number
  cpuName?: string
  cpuCores?: number
  cpuTier?: number
  ramTotalGb?: number
  ramTier?: number
  primaryStorageModel?: string
  primaryStorageType?: number
  primaryStorageSizeGb?: number
  storageTier?: number
  gpuName?: string
  gpuTier?: number
  overallTier?: number
}
interface HealthReport {
  systemInfo?: SystemInfoData | null
  overallStatus?: number
  checks: HealthCheck[]
}

// ── Helpers ────────────────────────────────────────────────────────
const TIER_LABEL: Record<number, string> = { 0: 'Ultra', 1: 'High', 2: 'Mid', 3: 'Low', 4: 'Very Low' }
const STORAGE_LABEL: Record<number, string> = { 0: 'NVMe', 1: 'SATA SSD', 2: 'HDD', 3: 'Unknown' }
const TIER_COLOR: Record<number, string> = {
  0: 'bg-violet-500 text-white',
  1: 'bg-emerald-500 text-white',
  2: 'bg-sky-500 text-white',
  3: 'bg-amber-500 text-white',
  4: 'bg-red-500 text-white',
}
function tierLabel(v?: number) { return v == null ? '—' : (TIER_LABEL[v] ?? String(v)) }
function storageLabel(v?: number) { return v == null ? '—' : (STORAGE_LABEL[v] ?? String(v)) }

function parseDiskDetails(details: string): { freeGb?: number; freePct?: number; usedPct?: number } {
  // e.g. "33.2GB free (14.0%)"
  const m = details.match(/([\d.]+)GB free \(([\d.]+)%\)/)
  if (!m) return {}
  const freePct = parseFloat(m[2])
  return { freeGb: parseFloat(m[1]), freePct, usedPct: Math.max(0, Math.min(100, 100 - freePct)) }
}

function statusIcon(status: number) {
  switch (status) {
    case 0: return <CheckCircle2 size={16} className="text-[var(--color-success)]" />
    case 1: return <AlertTriangle size={16} className="text-[var(--color-warning)]" />
    case 2: return <XCircle size={16} className="text-[var(--color-danger)]" />
    default: return <AlertTriangle size={16} className="text-[var(--color-text-muted)]" />
  }
}
function statusDot(status: number) {
  if (status === 0) return 'bg-[var(--color-success)]'
  if (status === 1) return 'bg-[var(--color-warning)]'
  return 'bg-[var(--color-danger)]'
}
function barColor(status: number) {
  if (status === 0) return 'bg-[var(--color-success)]'
  if (status === 1) return 'bg-amber-500'
  return 'bg-[var(--color-danger)]'
}

type Tab = 'health' | 'network' | 'startup' | 'benchmark'

export default function Diagnostics() {
  const [tab, setTab] = useState<Tab>('health')
  const [healthReport, setHealthReport] = useState<HealthReport | null>(null)
  const [networkReport, setNetworkReport] = useState<any>(null)
  const [startupReport, setStartupReport] = useState<any>(null)
  const [benchmarkReport, setBenchmarkReport] = useState<any>(null)
  const [loading, setLoading] = useState(false)

  async function runDiag(mode: Tab) {
    setTab(mode)
    setLoading(true)
    try {
      const modeMap: Record<Tab, string | undefined> = {
        health: undefined,
        network: 'network',
        startup: 'startup',
        benchmark: 'bench',
      }
      const raw = await invokeJson<any>('run_diagnostics', { mode: modeMap[mode] })
      switch (mode) {
        case 'health': setHealthReport(raw); break
        case 'network': setNetworkReport(raw); break
        case 'startup': setStartupReport(raw); break
        case 'benchmark': setBenchmarkReport(raw); break
      }
    } catch (err) {
      console.error('Diagnostics failed:', err)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { runDiag('health') }, [])

  const tabs: { id: Tab; label: string; icon: any }[] = [
    { id: 'health', label: 'Health', icon: Activity },
    { id: 'network', label: 'Network', icon: Wifi },
    { id: 'startup', label: 'Startup', icon: HardDrive },
    { id: 'benchmark', label: 'Benchmark', icon: Gauge },
  ]

  // Derived for health tab
  const si = healthReport?.systemInfo
  const passed = healthReport ? healthReport.checks.filter(c => c.status === 0).length : 0
  const total = healthReport?.checks.length ?? 0
  const overall = healthReport?.overallStatus ?? 0
  const overallLabel = overall === 0 ? 'Healthy' : overall === 1 ? 'Attention needed' : 'Critical'
  const diskCheck = healthReport?.checks.find(c => c.name.includes('Disk'))
  const memCheck  = healthReport?.checks.find(c => c.name === 'Memory')
  const svcChecks = healthReport?.checks.filter(c =>
    ['Windows Defender','Windows Firewall','Base Filtering','Windows Update','RPC','DCOM','Cryptographic','Event Log']
      .some(k => c.name.includes(k))
  ) ?? []
  const otherChecks = healthReport?.checks.filter(c => c.name === 'Security' || c.name === 'Uptime') ?? []
  const diskParsed = diskCheck ? parseDiskDetails(diskCheck.details) : {}

  return (
    <div className="space-y-6">
      <div className="animate-slide-up">
        <h1 className="text-2xl font-bold tracking-tight">Diagnostics</h1>
        <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">System health and performance checks</p>
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 bg-[var(--color-bg-card)] p-1 rounded-xl border border-[var(--color-border)] animate-slide-up stagger-1">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => runDiag(id)}
            className={`relative flex items-center gap-2 px-4 py-2.5 rounded-lg text-[13px] font-medium transition-all duration-250 ${
              tab === id
                ? 'text-white shadow-lg'
                : 'text-[var(--color-text-muted)] hover:text-[var(--color-text)] hover:bg-[rgba(255,255,255,0.03)]'
            }`}
            style={tab === id ? {
              background: 'linear-gradient(135deg, #6366F1, #7C3AED)',
              boxShadow: '0 4px 16px rgba(99,102,241,0.3), 0 0 0 1px rgba(99,102,241,0.2)',
            } : {}}
          >
            <Icon size={16} />
            {label}
          </button>
        ))}
      </div>

      {loading && (
        <div className="card flex items-center justify-center py-12">
          <Loader2 size={24} className="animate-spin text-[var(--color-primary)]" />
          <span className="ml-3 text-[var(--color-text-muted)]">Running diagnostics...</span>
        </div>
      )}

      {/* Health Tab */}
      {!loading && tab === 'health' && healthReport && (
        <div className="space-y-4">
          {/* Overall banner */}
          <div className="card flex flex-wrap items-center gap-3 py-4">
            <div className={`w-9 h-9 rounded-xl flex items-center justify-center ${overall === 0 ? 'bg-emerald-500/15 text-emerald-400' : overall === 1 ? 'bg-amber-500/15 text-amber-400' : 'bg-red-500/15 text-red-400'}`}>
              {overall === 0 ? <CheckCircle2 size={18} /> : overall === 1 ? <AlertTriangle size={18} /> : <XCircle size={18} />}
            </div>
            <div className="flex-1 min-w-[160px]">
              <div className="text-[13px] font-semibold flex items-center gap-2">
                {overallLabel}
                <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-bold tracking-wide ${overall === 0 ? 'bg-emerald-500 text-white' : overall === 1 ? 'bg-amber-500 text-white' : 'bg-red-500 text-white'}`}>
                  {overall === 0 ? 'OK' : overall === 1 ? 'WARNING' : 'CRITICAL'}
                </span>
              </div>
              <div className="text-[12px] text-[var(--color-text-muted)]">{passed}/{total} checks passed · {si?.osCaption ?? ''}{si?.buildNumber ? ` · Build ${si.buildNumber}` : ''}</div>
            </div>
            {si?.overallTier != null && (
              <span className={`px-2.5 py-1 rounded-full text-xs font-bold ${TIER_COLOR[si.overallTier] ?? 'bg-white/10'}`}>
                Tier {tierLabel(si.overallTier)}
              </span>
            )}
          </div>

          {/* System snapshot */}
          {si && (
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
              <div className="card py-3">
                <div className="flex items-center gap-2 text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]"><Cpu size={14} />CPU</div>
                <div className="text-[13px] font-medium leading-tight mt-1 truncate" title={si.cpuName}>{si.cpuName || '—'}</div>
                <div className="text-xs text-[var(--color-text-muted)] mt-0.5">{si.cpuCores ? `${si.cpuCores} cores` : ''} {si.cpuTier != null && <span className={`ml-1 inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold ${TIER_COLOR[si.cpuTier] ?? ''}`}>{tierLabel(si.cpuTier)}</span>}</div>
              </div>
              <div className="card py-3">
                <div className="flex items-center gap-2 text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]"><MemoryStick size={14} />Memory</div>
                <div className="text-[13px] font-medium mt-1">{si.ramTotalGb != null ? `${si.ramTotalGb.toFixed(2)} GB` : '—'}</div>
                <div className="text-xs text-[var(--color-text-muted)] mt-0.5">RAM {si.ramTier != null && <span className={`ml-1 inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold ${TIER_COLOR[si.ramTier] ?? ''}`}>{tierLabel(si.ramTier)}</span>}</div>
              </div>
              <div className="card py-3">
                <div className="flex items-center gap-2 text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]"><Monitor size={14} />GPU</div>
                <div className="text-[13px] font-medium leading-tight mt-1 truncate" title={si.gpuName}>{si.gpuName || '—'}</div>
                <div className="text-xs text-[var(--color-text-muted)] mt-0.5">{si.gpuTier != null && <span className={`inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold ${TIER_COLOR[si.gpuTier] ?? ''}`}>{tierLabel(si.gpuTier)}</span>}</div>
              </div>
              <div className="card py-3">
                <div className="flex items-center gap-2 text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]"><Server size={14} />Storage</div>
                <div className="text-[13px] font-medium leading-tight mt-1 truncate" title={si.primaryStorageModel}>{si.primaryStorageModel || '—'}</div>
                <div className="text-xs text-[var(--color-text-muted)] mt-0.5">
                  {storageLabel(si.primaryStorageType)}{si.primaryStorageSizeGb ? ` · ${si.primaryStorageSizeGb.toFixed(0)} GB` : ''}
                  {si.storageTier != null && <span className={`ml-1 inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold ${TIER_COLOR[si.storageTier] ?? ''}`}>{tierLabel(si.storageTier)}</span>}
                </div>
              </div>
            </div>
          )}

          {/* Resource bars */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
            {/* Disk */}
            {diskCheck && (
              <div className="card">
                <div className="flex items-center gap-2 mb-2">
                  <HardDrive size={14} className="text-[var(--color-text-muted)]" />
                  <span className="text-[13px] font-semibold">{diskCheck.name}</span>
                  <span className={`ml-auto w-2 h-2 rounded-full ${statusDot(diskCheck.status)}`} />
                  <span className="text-xs text-[var(--color-text-muted)]">{diskCheck.details}</span>
                </div>
                {diskParsed.usedPct != null && (
                  <div className="h-2 rounded-full bg-white/10 overflow-hidden">
                    <div className={`h-full rounded-full transition-all ${barColor(diskCheck.status)}`} style={{ width: `${diskParsed.usedPct.toFixed(1)}%` }} />
                  </div>
                )}
                {diskParsed.usedPct != null && (
                  <div className="flex justify-between text-[11px] text-[var(--color-text-muted)] mt-1">
                    <span>Used {diskParsed.usedPct.toFixed(1)}%</span><span>Free {diskParsed.freePct!.toFixed(1)}%</span>
                  </div>
                )}
              </div>
            )}
            {/* Memory */}
            {memCheck && (
              <div className="card">
                <div className="flex items-center gap-2 mb-2">
                  <MemoryStick size={14} className="text-[var(--color-text-muted)]" />
                  <span className="text-[13px] font-semibold">{memCheck.name}</span>
                  <span className={`ml-auto w-2 h-2 rounded-full ${statusDot(memCheck.status)}`} />
                  <span className="text-xs text-[var(--color-text-muted)]">{memCheck.details}</span>
                </div>
                {/* tier bar */}
                <div className="flex gap-1 mt-1">
                  {[0,1,2,3,4].map(i => {
                    const active = si?.ramTier != null ? i >= (4 - si.ramTier) : false
                    // dimmer for inactive
                    return <div key={i} className={`h-2 flex-1 rounded-full ${active ? (memCheck.status===0?'bg-emerald-500': memCheck.status===1?'bg-amber-500':'bg-red-500') : 'bg-white/10'}`} />
                  })}
                </div>
                <div className="text-[11px] text-[var(--color-text-muted)] mt-1">Tier {si?.ramTier != null ? tierLabel(si.ramTier) : '—'} — {memCheck.status===0 ? 'OK' : memCheck.status===1 ? 'Consider upgrade' : 'Low memory'}</div>
              </div>
            )}
          </div>

          {/* Services */}
          {svcChecks.length > 0 && (
            <div className="card">
              <div className="flex items-center gap-2 mb-3">
                <Shield size={14} className="text-[var(--color-primary)]" />
                <h3 className="text-[13px] font-semibold">Critical Services</h3>
                <span className="text-xs text-[var(--color-text-muted)]">· {svcChecks.filter(c=>c.status===0).length}/{svcChecks.length} running</span>
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                {svcChecks.map((check, i) => (
                  <div key={i} className="flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[rgba(255,255,255,0.02)] px-3 py-2">
                    {statusIcon(check.status)}
                    <span className="text-[13px] font-medium flex-1 truncate">{check.name}</span>
                    <span className={`text-xs ${check.status===0?'text-[var(--color-success)]': check.status===1?'text-amber-400':'text-red-400'}`}>{check.details}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Other */}
          {otherChecks.length > 0 && (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {otherChecks.map((check, i) => (
                <div key={i} className="card flex items-center gap-3 py-3">
                  {check.name === 'Security' ? <Lock size={16} className="text-[var(--color-text-muted)]" /> : <Clock3 size={16} className="text-[var(--color-text-muted)]" />}
                  <div className="flex-1">
                    <div className="text-[13px] font-medium">{check.name}</div>
                    <div className="text-xs text-[var(--color-text-muted)]">{check.details}</div>
                  </div>
                  {statusIcon(check.status)}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Network Tab */}
      {!loading && tab === 'network' && networkReport && (
        <div className="space-y-3">
          <div className="card">
            <h3 className="font-semibold text-sm mb-3">Ping Results</h3>
            <div className="grid grid-cols-3 gap-4 text-sm">
              <div>
                <div className="text-[var(--color-text-muted)]">Average Latency</div>
                <div className="text-lg font-bold">{networkReport.pingResults?.averageLatencyMs?.toFixed(1)}ms</div>
              </div>
              <div>
                <div className="text-[var(--color-text-muted)]">Packet Loss</div>
                <div className="text-lg font-bold">{networkReport.pingResults?.packetLossPercent?.toFixed(1)}%</div>
              </div>
              <div>
                <div className="text-[var(--color-text-muted)]">DNS Resolution</div>
                <div className="text-lg font-bold">{networkReport.dnsResolution?.resolutionMs}ms</div>
              </div>
            </div>
          </div>

          {networkReport.adapterInfo?.length > 0 && (
            <div className="card">
              <h3 className="font-semibold text-sm mb-3">Network Adapters</h3>
              {networkReport.adapterInfo.map((adapter: any, i: number) => (
                <div key={i} className="flex items-center justify-between py-2 border-b border-[var(--color-border)] last:border-0 text-sm">
                  <div>
                    <span className="font-medium">{adapter.name}</span>
                    <span className="text-[var(--color-text-muted)] ml-2">{adapter.type}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-[var(--color-text-muted)]">
                      {(adapter.speed / 1000000).toFixed(0)} Mbps
                    </span>
                    <span className={`w-2 h-2 rounded-full ${
                      adapter.status === 'Up' ? 'bg-[var(--color-success)]' : 'bg-[var(--color-danger)]'
                    }`} />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Startup Tab */}
      {!loading && tab === 'startup' && startupReport && (
        <div className="card">
          <h3 className="font-semibold text-sm mb-3">
            Startup Items ({startupReport.items?.length || 0})
          </h3>
          {startupReport.items?.length === 0 && (
            <div className="text-sm text-[var(--color-text-muted)] py-6 text-center">No startup items found.</div>
          )}
          {startupReport.items?.map((item: any, i: number) => (
            <div key={i} className="flex items-center justify-between py-2 border-b border-[var(--color-border)] last:border-0 text-sm">
              <div>
                <div className="font-medium">{item.name}</div>
                <div className="text-xs text-[var(--color-text-muted)]">{item.location}</div>
              </div>
              <div className="text-xs text-[var(--color-text-muted)] truncate max-w-[300px]">{item.command}</div>
            </div>
          ))}
          {startupReport.recommendation && (
            <div className="text-xs text-[var(--color-text-muted)] mt-3 pt-3 border-t border-[var(--color-border)]">{startupReport.recommendation}</div>
          )}
        </div>
      )}

      {/* Benchmark Tab */}
      {!loading && tab === 'benchmark' && benchmarkReport && (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-4">
            <div className="card">
              <h3 className="font-semibold text-sm mb-3">Network</h3>
              <div className="space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">Avg Latency</span>
                  <span>{benchmarkReport.network?.avgLatencyMs?.toFixed(1)}ms</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">Min / Max</span>
                  <span>{benchmarkReport.network?.minLatencyMs?.toFixed(0)} / {benchmarkReport.network?.maxLatencyMs?.toFixed(0)}ms</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">Packet Loss</span>
                  <span>{benchmarkReport.network?.packetLossPercent?.toFixed(1)}%</span>
                </div>
              </div>
            </div>

            <div className="card">
              <h3 className="font-semibold text-sm mb-3">System</h3>
              <div className="space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">CPU Cores</span>
                  <span>{benchmarkReport.system?.cpuCores}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">RAM</span>
                  <span>{benchmarkReport.system?.ramGb?.toFixed(1)} GB</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">Storage</span>
                  <span>{benchmarkReport.system?.storageType}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-text-muted)]">Tier</span>
                  <span>{benchmarkReport.system?.overallTier}</span>
                </div>
              </div>
            </div>
          </div>

          <div className="card">
            <h3 className="font-semibold text-sm mb-3">Memory</h3>
            <div className="grid grid-cols-3 gap-4 text-sm">
              <div>
                <div className="text-[var(--color-text-muted)]">Total</div>
                <div className="font-bold">{benchmarkReport.memory?.totalGb?.toFixed(1)} GB</div>
              </div>
              <div>
                <div className="text-[var(--color-text-muted)]">Used</div>
                <div className="font-bold">{benchmarkReport.memory?.usedGb?.toFixed(1)} GB</div>
              </div>
              <div>
                <div className="text-[var(--color-text-muted)]">Free</div>
                <div className="font-bold">{benchmarkReport.memory?.freeGb?.toFixed(1)} GB</div>
              </div>
            </div>
            {benchmarkReport.memory?.totalGb && (
              <div className="h-2 rounded-full bg-white/10 overflow-hidden mt-3">
                <div className="h-full bg-sky-500 rounded-full" style={{ width: `${Math.min(100, (benchmarkReport.memory.usedGb / benchmarkReport.memory.totalGb) * 100).toFixed(1)}%` }} />
              </div>
            )}
            <div className="text-[11px] text-[var(--color-text-muted)] mt-1">Benchmark took {benchmarkReport.duration ? String(benchmarkReport.duration).slice(3,7) + 's' : '—'}</div>
          </div>
        </div>
      )}
    </div>
  )
}
