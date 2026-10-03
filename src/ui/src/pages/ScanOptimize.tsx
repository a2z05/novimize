import { useState, useEffect, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { invokeJson } from '../hooks/useTauri'
import type { TweakDef, DetectionResult, ApplySummary, ApplySessionResult, BatchPlan } from '../types'
import { tallyStatus } from '../types'
import RiskBadge from '../components/RiskBadge'
import ConfirmModal from '../components/ConfirmModal'
import type { ConfirmTweakItem } from '../types'
import {
  ScanSearch,
  Search,
  Zap,
  Filter,
  Loader2,
  ChevronDown,
  CheckCircle2,
  Circle,
  AlertTriangle,
  XCircle,
  RotateCcw,
  AlertOctagon,
  Sparkles,
} from 'lucide-react'

// Categories where a change reaches below the settings layer — power
// management and the GPU driver path. The old set named 'gpu' and 'power',
// which no tweak uses, so this branch never fired.
const specialCategories = new Set(['cpu-power', 'gpu-gaming'])
const specialMethods = new Set(['PowerShell', 'Dism'])

function isSpecialTweak(tweak: TweakDef): boolean {
  if (specialMethods.has(tweak.method)) return true
  if (tweak.risk === 'Experimental' || tweak.risk === 'Risky' || tweak.risk === 'Dangerous') return true
  if (specialCategories.has(tweak.category) && tweak.evidence < 4) return true
  return false
}

const methodLabels: Record<string, string> = {
  Registry: 'Registry',
  Service: 'Service',
  PowerCfg: 'Power',
  NetSh: 'Network',
  PowerShell: 'Script',
  Dism: 'System',
  AppX: 'App',
  TaskScheduler: 'Task',
  Script: 'Script',
}

const categoryIcons: Record<string, string> = {
  'cpu-power': '⚡',
  'gpu-gaming': '🎮',
  'network': '🌐',
  'privacy': '🔒',
  'services': '⚙️',
  'startup': '🚀',
  'storage': '💾',
  'visual-effects': '🎨',
  'cleanup': '🧹',
  'explorer': '📁',
}

export default function ScanOptimize() {
  const [tweaks, setTweaks] = useState<TweakDef[]>([])
  const [scanResults, setScanResults] = useState<DetectionResult[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [scanning, setScanning] = useState(false)
  const [applying, setApplying] = useState(false)
  const [scanned, setScanned] = useState(false)
  const [filterCategory, setFilterCategory] = useState<string>('all')
  // Opened from the command palette: the term it found is the search box.
  const [searchParams] = useSearchParams()
  const [search, setSearch] = useState<string>(() => searchParams.get('q') ?? '')
  const [expandedCategories, setExpandedCategories] = useState<Set<string>>(new Set())
  const [showConfirm, setShowConfirm] = useState(false)
  const [plan, setPlan] = useState<BatchPlan | null | undefined>(undefined)
  const [applyProgress, setApplyProgress] = useState<{ current: number; total: number } | null>(null)
  const [applyResult, setApplyResult] = useState<ApplySummary | null>(null)

  useEffect(() => { loadTweaks() }, [])

  async function loadTweaks() {
    try {
      const raw = await invokeJson<TweakDef[]>('list_tweaks', {})
      setTweaks(raw || [])
      if (raw?.length) {
        const cats = [...new Set(raw.map(t => t.category))]
        if (cats.length) setExpandedCategories(new Set([cats[0]]))
      }
    } catch (err) {
      console.error('Failed to load tweaks:', err)
    }
  }

  async function runScan() {
    setScanning(true)
    setScanned(false)
    setScanResults([])
    setSelected(new Set())
    try {
      const raw = await invokeJson<DetectionResult[]>('scan_tweaks', {})
      setScanResults(raw || [])
      setScanned(true)
      const notApplied = new Set((raw || []).filter(r => r.state === 'NotApplied').map(r => r.tweakId))
      setSelected(notApplied)
      const cats = [...new Set(tweaks.map(t => t.category))]
      setExpandedCategories(new Set(cats))
    } catch (err) {
      console.error('Scan failed:', err)
    } finally {
      setScanning(false)
    }
  }

  function toggleSelect(id: string) {
    const next = new Set(selected)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    setSelected(next)
  }

  function selectAll() {
    const notApplied = scanResults.filter(r => r.state === 'NotApplied').map(r => r.tweakId)
    setSelected(new Set(notApplied))
  }

  function deselectAll() {
    setSelected(new Set())
  }

  function toggleCategory(cat: string) {
    const next = new Set(expandedCategories)
    if (next.has(cat)) next.delete(cat)
    else next.add(cat)
    setExpandedCategories(next)
  }

  function toggleCategorySelect(cat: string) {
    const catTweaks = filteredTweaks.filter(t => t.category === cat)
    const catIds = catTweaks
      .filter(t => getStateForTweak(t.id)?.state !== 'Applied')
      .map(t => t.id)
    const allSelected = catIds.every(id => selected.has(id))
    const next = new Set(selected)
    if (allSelected) {
      catIds.forEach(id => next.delete(id))
    } else {
      catIds.forEach(id => next.add(id))
    }
    setSelected(next)
  }

  const filteredTweaks = useMemo(() => {
    return tweaks.filter(t => {
      if (filterCategory !== 'all' && t.category !== filterCategory) return false
      if (search.trim()) {
        const q = search.trim().toLowerCase()
        const hay = `${t.id} ${t.name} ${t.category} ${t.description ?? ''}`.toLowerCase()
        if (!hay.includes(q)) return false
      }
      return true
    })
  }, [tweaks, filterCategory, search])

  const categories = useMemo(() => {
    return [...new Set(filteredTweaks.map(t => t.category))].sort()
  }, [filteredTweaks])

  function getStateForTweak(id: string): DetectionResult | undefined {
    return scanResults.find(r => r.tweakId === id)
  }

  const riskBreakdown = useMemo(() => {
    const counts: Record<string, number> = {}
    selected.forEach(id => {
      const t = tweaks.find(x => x.id === id)
      if (t) counts[t.risk] = (counts[t.risk] || 0) + 1
    })
    return counts
  }, [selected, tweaks])

  // Plan the run whenever the confirm modal is open and the selection could
  // have moved — including after a guided fix adds or drops a tweak. `undefined`
  // means "checking", which is why it is not simply null.
  useEffect(() => {
    if (!showConfirm) return
    if (selected.size === 0) {
      setPlan(null)
      return
    }
    let cancelled = false
    setPlan(undefined)
    const ids = Array.from(selected).join(',')
    invokeJson<BatchPlan>('plan_tweak', { tweakId: ids })
      .then(p => { if (!cancelled) setPlan(p ?? null) })
      .catch(err => {
        console.error('Plan failed:', err)
        if (!cancelled) setPlan(null)
      })
    return () => { cancelled = true }
  }, [showConfirm, selected])

  /** Guided fix for a missing dependency: pull it into the run and re-plan. */
  function includeTweak(id: string) {
    setSelected(prev => {
      const next = new Set(prev)
      next.add(id)
      return next
    })
  }

  /**
   * Guided fix for a conflict: keep one side. Dropping both would be worse
   * than either — a conflict means pick one, not do neither.
   */
  function keepConflict(keepId: string, dropId: string) {
    setSelected(prev => {
      const next = new Set(prev)
      next.delete(dropId)
      next.add(keepId)
      // Nothing left to apply, so an open modal would show an empty run.
      if (next.size === 0) setShowConfirm(false)
      return next
    })
  }

  function buildConfirmItems(): ConfirmTweakItem[] {
    return Array.from(selected).map(id => {
      const t = tweaks.find(x => x.id === id)!
      return {
        id: t.id,
        name: t.name,
        description: t.description,
        risk: t.risk,
        method: t.method,
        targetValue: t.targetValue,
        defaultValue: t.defaultValue,
        currentValue: getStateForTweak(id)?.currentValue,
        registryKey: t.apply?.registryKey,
        registryValue: t.apply?.registryValue,
        serviceName: t.apply?.serviceName,
        isSpecial: isSpecialTweak(t),
      }
    })
  }

  async function handleConfirmApply() {
    setApplying(true)
    setApplyProgress({ current: 0, total: selected.size })
    setShowConfirm(false)
    setApplyResult(null)
    // Started outside the try so a throw below cannot leak a live interval
    // that keeps calling setApplyProgress forever.
    let progress = 0
    const interval = setInterval(() => {
      progress++
      setApplyProgress({ current: Math.min(progress, selected.size), total: selected.size })
    }, 300)
    try {
      const ids = Array.from(selected).join(',')
      const res = await invokeJson<ApplySessionResult>('apply_tweak', { tweakId: ids })
      setApplyProgress({ current: selected.size, total: selected.size })

      // Parse result. Elevation refusals and measurement skips get their own
      // buckets — always show the summary card so they are visible instead of
      // being silently swallowed when nothing truly failed.
      const summary: ApplySummary = { ok: 0, fail: 0, skipped: 0, needAdmin: 0, blocked: 0, errors: [] }
      if (res?.results?.length) {
        for (const r of res.results) {
          const bucket = tallyStatus(r.status)
          summary[bucket]++
          if (bucket === 'fail') summary.errors.push(`${r.tweakId}: ${r.message || r.status}`)
        }
      } else {
        summary.ok = res?.tweaksSucceeded ?? 0
        summary.fail = res?.tweaksFailed ?? 0
        summary.skipped = res?.tweaksSkipped ?? 0
        summary.needAdmin = res?.tweaksNeedElevation ?? 0
        summary.blocked = res?.tweaksBlocked ?? 0
      }
      setApplyResult(summary)
      setSelected(new Set())
      await runScan()
    } catch (err: any) {
      console.error('Apply failed:', err)
      setApplyResult({ ok: 0, fail: selected.size, skipped: 0, needAdmin: 0, blocked: 0, errors: [String(err?.message || err)] })
    } finally {
      clearInterval(interval)
      setApplying(false)
      setApplyProgress(null)
    }
  }

  async function handleApplySingle(id: string) {
    try {
      const res = await invokeJson<ApplySessionResult>('apply_tweak', { tweakId: id })
      const r = res?.results?.[0]
      if (r) {
        const bucket = tallyStatus(r.status)
        if (bucket === 'ok') {
          await runScan()
          return
        }
        const summary: ApplySummary = { ok: 0, fail: 0, skipped: 0, needAdmin: 0, blocked: 0, errors: [] }
        summary[bucket] = 1
        if (bucket === 'fail') summary.errors = [`${r.tweakId}: ${r.message || r.status}`]
        setApplyResult(summary)
        setTimeout(() => setApplyResult(null), 8000)
      }
      await runScan()
    } catch (err: any) {
      setApplyResult({ ok: 0, fail: 1, skipped: 0, needAdmin: 0, blocked: 0, errors: [String(err?.message || err)] })
      setTimeout(() => setApplyResult(null), 8000)
    }
  }

  async function handleRollbackSingle(id: string) {
    try {
      await invokeJson<any>('rollback_tweak', { tweakId: id })
      await runScan()
    } catch (err) {
      console.error('Rollback failed:', err)
    }
  }

  const totalApplied = scanResults.filter(r => r.state === 'Applied').length
  const totalNeedsAction = scanResults.filter(r => r.state !== 'Applied' && r.detectionSucceeded).length

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Scan & Optimize</h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            {scanned
              ? `${totalApplied} applied · ${totalNeedsAction} need action · ${selected.size} selected`
              : 'Detect and apply system optimizations'
            }
          </p>
        </div>
        <button
          onClick={runScan}
          disabled={scanning}
          className="btn btn-primary gap-2"
        >
          {scanning ? (
            <>
              <Loader2 size={16} className="animate-spin" />
              Scanning...
            </>
          ) : (
            <>
              <ScanSearch size={16} />
              {scanned ? 'Rescan' : 'Scan System'}
            </>
          )}
        </button>
      </div>

      {/* Scan progress indicator */}
      {scanning && (
        <div className="card animate-scale-in">
          <div className="flex items-center gap-4">
            <div className="w-10 h-10 rounded-xl flex items-center justify-center"
                 style={{ background: 'rgba(99,102,241,0.1)' }}>
              <Loader2 size={20} className="text-[var(--color-primary)] animate-spin" />
            </div>
            <div className="flex-1">
              <div className="text-[13px] font-semibold">Scanning system tweaks...</div>
              <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">Checking each tweak's current state</div>
            </div>
          </div>
          <div className="mt-3 h-1 rounded-full bg-[var(--color-bg-elevated)] overflow-hidden">
            <div className="h-full rounded-full animate-shimmer-slide"
                 style={{ width: '40%', background: 'linear-gradient(90deg, #6366F1, #A855F7, #6366F1)', backgroundSize: '200% 100%' }} />
          </div>
        </div>
      )}

      {/* Stats row after scan */}
      {scanned && !scanning && (
        <div className="grid grid-cols-3 gap-3 animate-slide-up stagger-1">
          <div className="card flex items-center gap-3 py-3">
            <div className="w-9 h-9 rounded-lg flex items-center justify-center" style={{ background: 'rgba(34,197,94,0.1)' }}>
              <CheckCircle2 size={16} className="text-[var(--color-success)]" />
            </div>
            <div>
              <div className="text-[18px] font-bold">{totalApplied}</div>
              <div className="text-[11px] text-[var(--color-text-muted)]">Applied</div>
            </div>
          </div>
          <div className="card flex items-center gap-3 py-3">
            <div className="w-9 h-9 rounded-lg flex items-center justify-center" style={{ background: 'rgba(245,158,11,0.1)' }}>
              <AlertTriangle size={16} className="text-[var(--color-warning)]" />
            </div>
            <div>
              <div className="text-[18px] font-bold">{totalNeedsAction}</div>
              <div className="text-[11px] text-[var(--color-text-muted)]">Need Action</div>
            </div>
          </div>
          <div className="card flex items-center gap-3 py-3">
            <div className="w-9 h-9 rounded-lg flex items-center justify-center" style={{ background: 'rgba(99,102,241,0.1)' }}>
              <Sparkles size={16} className="text-[var(--color-primary)]" />
            </div>
            <div>
              <div className="text-[18px] font-bold">{selected.size}</div>
              <div className="text-[11px] text-[var(--color-text-muted)]">Selected</div>
            </div>
          </div>
        </div>
      )}

      {/* Filter bar */}
      {(scanned || search.trim().length > 0) && (
        <div className="flex items-center gap-3 flex-wrap animate-slide-up stagger-2">
          <div className="relative">
            <Search
              size={13}
              className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]"
            />
            <input
              className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md pl-8 pr-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors w-56"
              placeholder="Search tweaks"
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          <div className="flex items-center gap-1.5 flex-wrap">
            <Filter size={13} className="text-[var(--color-text-muted)]" />
            <button
              onClick={() => setFilterCategory('all')}
              className={`chip ${filterCategory === 'all' ? 'active' : ''}`}
            >
              All ({tweaks.length})
            </button>
            {[...new Set(tweaks.map(t => t.category))].map(cat => (
              <button
                key={cat}
                onClick={() => setFilterCategory(filterCategory === cat ? 'all' : cat)}
                className={`chip ${filterCategory === cat ? 'active' : ''}`}
              >
                {categoryIcons[cat] || '📋'} {cat} ({tweaks.filter(t => t.category === cat).length})
              </button>
            ))}
          </div>
          <div className="flex-1" />
          <div className="flex items-center gap-2 text-[12px]">
            <button onClick={selectAll} className="text-[var(--color-primary)] hover:underline font-medium">Select all</button>
            <span className="opacity-30">·</span>
            <button onClick={deselectAll} className="text-[var(--color-primary)] hover:underline font-medium">Clear</button>
          </div>
        </div>
      )}

      {/* Category cards */}
      {scanned && (
        <div className="space-y-3 animate-slide-up stagger-3">
          {categories.map((category) => {
            const catTweaks = filteredTweaks.filter(t => t.category === category)
            const isExpanded = expandedCategories.has(category) || filterCategory !== 'all'
            const appliedCount = catTweaks.filter(t => getStateForTweak(t.id)?.state === 'Applied').length
            const needsAction = catTweaks.filter(t => {
              const s = getStateForTweak(t.id)
              return s && s.state !== 'Applied' && s.detectionSucceeded
            }).length
            const catIds = catTweaks.filter(t => getStateForTweak(t.id)?.state !== 'Applied').map(t => t.id)
            const allCatSelected = catIds.length > 0 && catIds.every(id => selected.has(id))
            const progress = catTweaks.length > 0 ? (appliedCount / catTweaks.length) * 100 : 0

            return (
              <div key={category} className="card p-0 overflow-hidden">
                {/* Category header */}
                <button
                  onClick={() => toggleCategory(category)}
                  className="w-full flex items-center gap-3 px-4 py-3.5 hover:bg-[rgba(255,255,255,0.02)] transition-colors text-left"
                >
                  <span className="text-lg">{categoryIcons[category] || '📋'}</span>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-[14px] font-semibold capitalize">{category}</span>
                      <span className="text-[11px] text-[var(--color-text-muted)]">{catTweaks.length} tweaks</span>
                    </div>
                    <div className="flex items-center gap-3 mt-1">
                      <div className="h-1 flex-1 max-w-[120px] rounded-full bg-[var(--color-bg-elevated)] overflow-hidden">
                        <div className="h-full rounded-full transition-all duration-500" style={{ width: `${progress}%`, background: 'linear-gradient(90deg, #22C55E, #10B981)' }} />
                      </div>
                      <span className="text-[10px] text-[var(--color-success)]">{appliedCount}/{catTweaks.length}</span>
                      {needsAction > 0 && <span className="text-[10px] text-[var(--color-warning)]">{needsAction} pending</span>}
                    </div>
                  </div>
                  {/* Select category checkbox */}
                  <label className="checkbox-wrapper" onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      checked={allCatSelected}
                      onChange={() => toggleCategorySelect(category)}
                    />
                    <span className="checkbox-visual" />
                  </label>
                  <ChevronDown
                    size={16}
                    className={`text-[var(--color-text-muted)] transition-transform duration-200 ${isExpanded ? '' : '-rotate-90'}`}
                  />
                </button>

                {/* Tweaks in category */}
                {isExpanded && (
                  <div className="border-t border-[var(--color-border-subtle)]">
                    {catTweaks.map((tweak, i) => {
                      const detection = getStateForTweak(tweak.id)
                      const state = detection?.state || 'Unknown'
                      const isApplied = state === 'Applied'
                      const isSpecial = isSpecialTweak(tweak)

                      const stateIcon = isApplied
                        ? <CheckCircle2 size={14} className="text-[var(--color-success)]" />
                        : state === 'PartiallyApplied'
                        ? <AlertTriangle size={14} className="text-[var(--color-warning)]" />
                        : state === 'Incompatible'
                        ? <XCircle size={14} className="text-[var(--color-text-muted)]" />
                        : <Circle size={14} className="text-[var(--color-text-muted)]" />

                      return (
                        <div
                          key={tweak.id}
                          className={`flex items-center gap-3 px-4 py-3 border-t border-[var(--color-border-subtle)] transition-colors hover:bg-[rgba(255,255,255,0.015)] ${
                            isSpecial ? 'bg-[rgba(239,68,68,0.02)]' : ''
                          }`}
                          style={{ animation: `slideUp 250ms cubic-bezier(0.16,1,0.3,1) ${i * 25}ms both` }}
                        >
                          {/* Checkbox */}
                          <label className="checkbox-wrapper" onClick={(e) => e.stopPropagation()}>
                            <input
                              type="checkbox"
                              checked={selected.has(tweak.id)}
                              onChange={() => toggleSelect(tweak.id)}
                              disabled={isApplied}
                            />
                            <span className="checkbox-visual" />
                          </label>

                          {/* State icon */}
                          {stateIcon}

                          {/* Tweak info */}
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2">
                              <span className="text-[13px] font-semibold truncate">{tweak.name}</span>
                              <RiskBadge risk={tweak.risk} />
                              {isSpecial && (
                                <span className="special-badge">
                                  <AlertOctagon size={9} /> Special
                                </span>
                              )}
                            </div>
                            <p className="text-[11px] text-[var(--color-text-muted)] truncate mt-0.5">{tweak.description}</p>
                          </div>

                          {/* Evidence */}
                          <div className="flex items-center gap-1 flex-shrink-0">
                            <div className="evidence-dots">
                              {[1,2,3,4,5].map(i => (
                                <div key={i} className="evidence-dot" style={{
                                  background: i <= tweak.evidence ? 'linear-gradient(135deg, #6366F1, #A855F7)' : 'var(--color-border)',
                                }} />
                              ))}
                            </div>
                          </div>

                          {/* Method badge */}
                          <span className="text-[10px] px-2 py-0.5 rounded bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)] flex-shrink-0">
                            {methodLabels[tweak.method] || tweak.method}
                          </span>

                          {/* Action buttons */}
                          <div className="flex items-center gap-1.5 flex-shrink-0">
                            {isApplied ? (
                              <button
                                onClick={() => handleRollbackSingle(tweak.id)}
                                className="btn btn-sm btn-ghost text-[11px] gap-1"
                                title="Rollback this tweak"
                              >
                                <RotateCcw size={12} />
                                Undo
                              </button>
                            ) : (
                              <button
                                onClick={() => handleApplySingle(tweak.id)}
                                className="btn btn-sm btn-primary text-[11px] gap-1"
                                title="Apply this tweak now"
                              >
                                <Zap size={12} />
                                Apply
                              </button>
                            )}
                          </div>
                        </div>
                      )
                    })}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {/* Empty state */}
      {tweaks.length === 0 && !scanning && (
        <div className="card flex flex-col items-center justify-center py-16 animate-fade-in">
          <ScanSearch size={40} className="text-[var(--color-text-muted)] mb-3 opacity-40" />
          <p className="text-[14px] font-semibold text-[var(--color-text-muted)]">No tweaks loaded</p>
          <p className="text-[12px] text-[var(--color-text-muted)] mt-1 opacity-70">Click Scan to detect system tweak states</p>
        </div>
      )}

      {/* Pre-scan state */}
      {!scanned && !scanning && tweaks.length > 0 && (
        <div className="card flex flex-col items-center justify-center py-16 animate-fade-in">
          <div className="w-16 h-16 rounded-2xl flex items-center justify-center mb-4" style={{ background: 'rgba(99,102,241,0.1)' }}>
            <ScanSearch size={32} className="text-[var(--color-primary)]" />
          </div>
          <p className="text-[15px] font-semibold">Ready to scan</p>
          <p className="text-[12px] text-[var(--color-text-muted)] mt-1 mb-4">
            {tweaks.length} tweaks available across {categories.length} categories
          </p>
          <button onClick={runScan} className="btn btn-primary gap-2">
            <ScanSearch size={16} />
            Scan System
          </button>
        </div>
      )}

      {/* Apply result feedback */}
      {applyResult && (
        <div className={`card animate-slide-up border ${applyResult.fail > 0 ? 'border-[rgba(239,68,68,0.3)]' : 'border-[rgba(34,197,94,0.3)]'}`}
             style={{ background: applyResult.fail > 0 ? 'rgba(239,68,68,0.05)' : 'rgba(34,197,94,0.05)' }}>
          <div className="flex items-center gap-3">
            {applyResult.fail > 0 ? (
              <AlertOctagon size={16} className="text-[var(--color-danger)] flex-shrink-0" />
            ) : (
              <CheckCircle2 size={16} className="text-[var(--color-success)] flex-shrink-0" />
            )}
            <div className="flex-1 min-w-0">
              <div className="text-[13px] font-bold">
                {[
                  `${applyResult.ok} applied`,
                  applyResult.fail > 0 ? `${applyResult.fail} failed` : null,
                  applyResult.skipped > 0 ? `${applyResult.skipped} skipped` : null,
                  applyResult.needAdmin > 0 ? `${applyResult.needAdmin} need administrator` : null,
                  applyResult.blocked > 0 ? `${applyResult.blocked} held back` : null,
                ].filter(Boolean).join(' · ')}
              </div>
              {applyResult.needAdmin > 0 && (
                <div className="mt-1 text-[11px] text-[var(--color-warning)]">
                  Restart the app as administrator to apply {applyResult.needAdmin === 1 ? 'that tweak' : 'those tweaks'} — they are unchanged, not broken.
                </div>
              )}
              {applyResult.blocked > 0 && (
                <div className="mt-1 text-[11px] text-[var(--color-warning)]">
                  {applyResult.blocked === 1 ? 'One tweak was' : `${applyResult.blocked} tweaks were`} held back before anything ran, because another tweak in the run contradicts {applyResult.blocked === 1 ? 'it' : 'them'} or {applyResult.blocked === 1 ? 'its' : 'their'} dependency is missing. They are untouched, not failed.
                </div>
              )}
              {applyResult.errors.length > 0 && (
                <div className="mt-1 text-[10px] text-[var(--color-text-muted)] max-h-[60px] overflow-y-auto">
                  {applyResult.errors.slice(0, 5).map((e, i) => (
                    <div key={i}>{e}</div>
                  ))}
                  {applyResult.errors.length > 5 && <div>+{applyResult.errors.length - 5} more</div>}
                </div>
              )}
            </div>
            <button onClick={() => setApplyResult(null)} className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1 text-[10px]">
              Dismiss
            </button>
          </div>
        </div>
      )}

      {/* Apply bar - inline */}
      {scanned && selected.size > 0 && !applying && (
        <div className="card animate-slide-up" style={{ boxShadow: '0 -4px 32px rgba(0,0,0,0.3), 0 0 40px rgba(99,102,241,0.06)' }}>
          <div className="flex items-center gap-4">
            <div className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0" style={{ background: 'rgba(99,102,241,0.15)' }}>
              <Zap size={16} className="text-[var(--color-primary)]" />
            </div>
            <div className="flex-1 min-w-0">
              <div className="text-[13px] font-bold">
                {selected.size} tweak{selected.size !== 1 ? 's' : ''} selected
              </div>
              <div className="flex items-center gap-2 text-[10px] text-[var(--color-text-muted)]">
                {Object.entries(riskBreakdown).map(([risk, count]) => (
                  <span key={risk}>{count} {risk.toLowerCase()}</span>
                ))}
              </div>
            </div>
            <div className="flex items-center gap-2 flex-shrink-0">
              <button onClick={deselectAll} className="btn btn-ghost btn-sm text-[11px]">
                Clear
              </button>
              <button onClick={() => setShowConfirm(true)} className="btn btn-primary gap-2 px-5 py-2.5">
                <Zap size={15} />
                Apply
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Applying progress - inline */}
      {applying && (
        <div className="card animate-slide-up">
          <div className="flex items-center gap-4">
            <div className="w-5 h-5 border-2 border-[var(--color-primary)] border-t-transparent rounded-full animate-spin flex-shrink-0" />
            <div className="flex-1 min-w-0">
              <div className="text-[13px] font-bold">Applying tweaks...</div>
              {applyProgress && (
                <div className="flex items-center gap-2 mt-1">
                  <div className="flex-1 h-1.5 rounded-full bg-[var(--color-bg-elevated)] overflow-hidden">
                    <div className="h-full rounded-full transition-all duration-300"
                         style={{ width: `${(applyProgress.current / applyProgress.total) * 100}%`, background: 'linear-gradient(90deg, #6366F1, #A855F7)' }} />
                  </div>
                  <span className="text-[11px] text-[var(--color-primary)] tabular-nums font-semibold">
                    {applyProgress.current}/{applyProgress.total}
                  </span>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Confirmation Modal */}
      <ConfirmModal
        open={showConfirm}
        title="Confirm Optimization"
        subtitle={`You are about to apply ${selected.size} tweak${selected.size !== 1 ? 's' : ''}`}
        tweaks={buildConfirmItems()}
        plan={plan}
        resolveName={id => tweaks.find(t => t.id === id)?.name ?? id}
        onIncludeTweak={includeTweak}
        onKeepConflict={keepConflict}
        onConfirm={handleConfirmApply}
        onCancel={() => setShowConfirm(false)}
      />
    </div>
  )
}
