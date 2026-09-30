interface RiskBadgeProps {
  risk: string
}

const riskConfig: Record<string, { className: string; label: string }> = {
  Safe: { className: 'badge-safe', label: 'Safe' },
  Recommended: { className: 'badge-recommended', label: 'Recommended' },
  Optional: { className: 'badge-optional', label: 'Optional' },
  Experimental: { className: 'badge-experimental', label: 'Experimental' },
  Risky: { className: 'badge-risky', label: 'Risky' },
  Dangerous: { className: 'badge-dangerous', label: 'Dangerous' },
  Deprecated: { className: 'badge-dangerous', label: 'Deprecated' },
  Myth: { className: 'badge-optional', label: 'Myth' },
}

export default function RiskBadge({ risk }: RiskBadgeProps) {
  const config = riskConfig[risk] || riskConfig.Optional
  return <span className={`badge ${config.className}`}>{config.label}</span>
}
