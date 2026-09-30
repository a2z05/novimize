import React, { useState, useCallback } from 'react'
import type { TweakDef, DetectionResult } from '../types'
import RiskBadge from './RiskBadge'
import {
  ChevronDown,
  ChevronRight,
  CheckCircle2,
  Circle,
  XCircle,
  AlertTriangle,
  Zap,
  RotateCcw,
  Link,
  ArrowRight,
  FileText,
  Settings,
  Wifi,
  Terminal,
  Package,
  Calendar,
  Code,
  Gauge,
} from 'lucide-react'

interface TweakDetailCardProps {
  tweak: TweakDef
  detection?: DetectionResult
  selected: boolean
  onSelect: () => void
  onApply?: (id: string) => void
  onRollback?: (id: string) => void
  isSpecial?: boolean
  staggerIndex?: number
  disableCheckbox?: boolean
}

const stateConfig: Record<string, { icon: React.ReactNode; color: string; label: string }> = {
  Applied: { icon: <CheckCircle2 size={15} />, color: 'text-[var(--color-success)]', label: 'Applied' },
  NotApplied: { icon: <Circle size={15} />, color: 'text-[var(--color-text-muted)]', label: 'Not Applied' },
  PartiallyApplied: { icon: <AlertTriangle size={15} />, color: 'text-[var(--color-warning)]', label: 'Partial' },
  Incompatible: { icon: <XCircle size={15} />, color: 'text-[var(--color-text-muted)]', label: 'Incompatible' },
}

const methodIcons: Record<string, React.ReactNode> = {
  Registry: <FileText size={14} />,
  Service: <Settings size={14} />,
  PowerCfg: <Gauge size={14} />,
  NetSh: <Wifi size={14} />,
  PowerShell: <Terminal size={14} />,
  Dism: <Package size={14} />,
  AppX: <Package size={14} />,
  TaskScheduler: <Calendar size={14} />,
  Script: <Code size={14} />,
}

function getTargetLocation(tweak: TweakDef): string | null {
  if (tweak.method === 'Registry' && tweak.apply?.registryKey) {
    return `${tweak.apply.registryKey}\\${tweak.apply.registryValue || ''}`
  }
  if (tweak.method === 'Service' && tweak.apply?.serviceName) {
    return `Service: ${tweak.apply.serviceName}`
  }
  if (tweak.apply?.command) {
    return `Command: ${tweak.apply.command.substring(0, 60)}${tweak.apply.command.length > 60 ? '...' : ''}`
  }
  return null
}

export default function TweakDetailCard({
  tweak,
  detection,
  selected,
  onSelect,
  onApply,
  onRollback,
  isSpecial = false,
  staggerIndex = 0,
  disableCheckbox = false,
}: TweakDetailCardProps) {
  const [expanded, setExpanded] = useState(false)
  const state = detection?.state || 'Unknown'
  const stateCfg = stateConfig[state] || stateConfig.NotApplied
  const isApplied = state === 'Applied'
  const targetLocation = getTargetLocation(tweak)

  const toggleExpand = useCallback(() => setExpanded(e => !e), [])

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      toggleExpand()
    }
  }, [toggleExpand])

  return (
    <div
      className={`border-b border-[var(--color-border-subtle)] last:border-b-0 transition-all duration-200 ${
        isSpecial ? 'bg-[rgba(239,68,68,0.03)]' : expanded ? 'bg-[rgba(99,102,241,0.03)]' : ''
      }`}
      style={{ animation: `slideUp 300ms cubic-bezier(0.16,1,0.3,1) ${staggerIndex * 30}ms both` }}
    >
      {/* Collapsed Row */}
      <div
        role="button"
        tabIndex={0}
        aria-expanded={expanded}
        aria-label={`${tweak.name} — ${stateCfg.label}. ${expanded ? 'Click to collapse' : 'Click to expand details'}`}
        className="flex items-center gap-3 px-4 py-3 cursor-pointer hover:bg-[rgba(255,255,255,0.02)] transition-colors"
        onClick={toggleExpand}
        onKeyDown={handleKeyDown}
      >
        {/* Expand arrow */}
        <div className="w-5 flex-shrink-0 flex justify-center text-[var(--color-text-muted)]" aria-hidden="true">
          {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </div>

        {/* Checkbox */}
        <label
          className="checkbox-wrapper"
          onClick={(e) => e.stopPropagation()}
          onKeyDown={(e) => e.stopPropagation()}
        >
          <input
            type="checkbox"
            checked={selected}
            onChange={(e) => { e.stopPropagation(); onSelect() }}
            disabled={disableCheckbox || isApplied}
            aria-label={`Select ${tweak.name}`}
          />
          <span className="checkbox-visual" />
        </label>

        {/* State icon */}
        <div className={`flex-shrink-0 ${stateCfg.color}`} aria-hidden="true">
          {stateCfg.icon}
        </div>

        {/* Tweak info */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-[13px] font-semibold truncate">{tweak.name}</span>
            <RiskBadge risk={tweak.risk} />
            {isSpecial && (
              <span className="special-badge" role="status">
                <AlertTriangle size={10} aria-hidden="true" />
                Special
              </span>
            )}
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] truncate mt-0.5 leading-relaxed">
            {tweak.description}
          </p>
        </div>

        {/* Evidence dots */}
        <div className="flex-shrink-0">
          <div className="evidence-dots" role="img" aria-label={`Evidence score: ${tweak.evidence} out of 5`}>
            {[1, 2, 3, 4, 5].map(i => (
              <div
                key={i}
                className="evidence-dot"
                style={{
                  background: i <= tweak.evidence
                    ? 'linear-gradient(135deg, #6366F1, #A855F7)'
                    : 'var(--color-border)',
                }}
              />
            ))}
          </div>
          <span className="text-[9px] text-[var(--color-text-muted)] mt-0.5 block text-center">{tweak.evidence}/5</span>
        </div>

        {/* State label */}
        <span className={`text-[11px] font-semibold flex-shrink-0 ${stateCfg.color}`}>
          {stateCfg.label}
        </span>

        {/* Method icon */}
        <span className="flex-shrink-0 opacity-60" title={tweak.method} aria-label={`Method: ${tweak.method}`}>
          {methodIcons[tweak.method] || <Settings size={14} />}
        </span>
      </div>

      {/* Expanded Detail */}
      {expanded && (
        <div
          className="px-4 pb-4 pt-1 overflow-hidden"
          style={{ animation: 'expandDown 250ms cubic-bezier(0.16,1,0.3,1) both' }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="ml-8 space-y-3">
            {/* Info grid */}
            <div className="grid grid-cols-3 gap-2">
              <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-0.5">Method</div>
                <div className="text-[12px] font-medium flex items-center gap-1.5">
                  <span aria-hidden="true">{methodIcons[tweak.method]}</span> {tweak.method}
                </div>
              </div>
              <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-0.5">Category</div>
                <div className="text-[12px] font-medium capitalize">{tweak.category}</div>
              </div>
              <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-0.5">Evidence</div>
                <div className="text-[12px] font-medium flex items-center gap-1">
                  <span className="gradient-text font-bold">{tweak.evidence}/5</span>
                  <span className="text-[10px] text-[var(--color-text-muted)]">
                    {tweak.evidence >= 4 ? '(strong)' : tweak.evidence >= 2 ? '(moderate)' : '(weak)'}
                  </span>
                </div>
              </div>
            </div>

            {/* Values */}
            <div className="flex items-center gap-3">
              <div className="flex-1 rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-0.5">Current Value</div>
                <div className="text-[12px] font-mono text-[var(--color-text-muted)]">
                  {detection?.currentValue || '—'}
                </div>
              </div>
              <ArrowRight size={14} className="text-[var(--color-primary)] flex-shrink-0" aria-hidden="true" />
              <div className="flex-1 rounded-lg bg-[rgba(99,102,241,0.08)] border border-[rgba(99,102,241,0.2)] px-3 py-2">
                <div className="text-[9px] text-[var(--color-primary)] uppercase tracking-wider font-semibold mb-0.5">Target Value</div>
                <div className="text-[12px] font-mono text-[var(--color-text)]">
                  {tweak.targetValue}
                </div>
              </div>
            </div>

            {/* Target location */}
            {targetLocation && (
              <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-0.5">Target Location</div>
                <div className="text-[11px] font-mono text-[var(--color-text-muted)] break-all">{targetLocation}</div>
              </div>
            )}

            {/* Conflicts & Dependencies */}
            <div className="flex flex-wrap gap-3">
              {tweak.conflictsWith.length > 0 && (
                <div className="flex items-center gap-1.5 text-[11px]">
                  <AlertTriangle size={11} className="text-[var(--color-warning)]" aria-hidden="true" />
                  <span className="text-[var(--color-text-muted)]">Conflicts:</span>
                  {tweak.conflictsWith.map(c => (
                    <span key={c} className="category-tag">{c}</span>
                  ))}
                </div>
              )}
              {tweak.dependsOn.length > 0 && (
                <div className="flex items-center gap-1.5 text-[11px]">
                  <Link size={11} className="text-[var(--color-info)]" aria-hidden="true" />
                  <span className="text-[var(--color-text-muted)]">Depends on:</span>
                  {tweak.dependsOn.map(d => (
                    <span key={d} className="category-tag">{d}</span>
                  ))}
                </div>
              )}
            </div>

            {/* Profiles */}
            {tweak.profiles.length > 0 && (
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-[10px] text-[var(--color-text-muted)] font-semibold uppercase tracking-wider">Profiles:</span>
                {tweak.profiles.map(p => (
                  <span key={p} className="category-tag">{p}</span>
                ))}
              </div>
            )}

            {/* Action buttons */}
            {(onApply || onRollback) && (
              <div className="flex items-center gap-2 pt-1">
                {onApply && !isApplied && (
                  <button
                    onClick={() => onApply(tweak.id)}
                    className="btn btn-sm btn-primary"
                    aria-label={`Apply ${tweak.name}`}
                  >
                    <Zap size={12} aria-hidden="true" />
                    Apply
                  </button>
                )}
                {onRollback && isApplied && (
                  <button
                    onClick={() => onRollback(tweak.id)}
                    className="btn btn-sm btn-secondary"
                    aria-label={`Rollback ${tweak.name}`}
                  >
                    <RotateCcw size={12} aria-hidden="true" />
                    Rollback
                  </button>
                )}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
