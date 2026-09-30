interface TierBadgeProps {
  tier: string
}

const tierClass: Record<string, string> = {
  Ultra: 'tier-ultra',
  High: 'tier-high',
  Mid: 'tier-mid',
  Low: 'tier-low',
  VeryLow: 'tier-verylow',
}

export default function TierBadge({ tier }: TierBadgeProps) {
  return (
    <span className={`tier-badge ${tierClass[tier] || 'tier-mid'}`}>
      {tier}
    </span>
  )
}
