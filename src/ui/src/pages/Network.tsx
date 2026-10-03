import { useCallback, useEffect, useMemo, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  Globe,
  Loader2,
  Network as NetworkIcon,
  Play,
  RefreshCw,
  Route,
  Search,
  Server,
  Shield,
  Terminal,
  Wifi,
} from 'lucide-react'
import type {
  DnsProvider,
  DnsStatus,
  LookupReport,
  NetworkChange,
  NetworkOverview,
  PingReport,
  TraceReport,
} from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

/** One confirmation dialog. The body is the command list, not a description. */
type Pending =
  | { kind: 'dns-set'; provider: DnsProvider }
  | { kind: 'dns-revert' }
  | { kind: 'flush' }
  | { kind: 'fix'; level: string }
  | { kind: 'restart'; name: string }
  | { kind: 'public-ip' }

type Tool = 'ping' | 'trace' | 'lookup' | 'reverse'

export default function Network() {
  const [overview, setOverview] = useState<NetworkOverview | null>(null)
  const [dns, setDns] = useState<DnsStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<NetworkChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [pendingChange, setPendingChange] = useState<NetworkChange | null>(null)

  const [tool, setTool] = useState<Tool>('ping')
  const [target, setTarget] = useState('1.1.1.1')
  const [recordType, setRecordType] = useState('A')
  const [resolveName, setResolveName] = useState('example.com')
  const [pingOut, setPingOut] = useState<PingReport | null>(null)
  const [traceOut, setTraceOut] = useState<TraceReport | null>(null)
  const [lookupOut, setLookupOut] = useState<LookupReport | null>(null)
  const [dnsTest, setDnsTest] = useState<LookupReport | null>(null)

  const load = useCallback(async () => {
    const [status, net] = await Promise.all([
      invokeJson<DnsStatus>('dns_action', { action: 'status' }),
      invokeJson<NetworkOverview>('net_action', { action: 'status' }),
    ])
    setDns(status)
    setOverview(net)
  }, [])

  const run = useCallback(async <T,>(key: string, fn: () => Promise<T>): Promise<T | null> => {
    setBusy(key)
    setError(null)
    try {
      return await fn()
    } catch (err) {
      setError(errMsg(err))
      return null
    } finally {
      setBusy(null)
    }
  }, [])

  useEffect(() => {
    void run('load', load)
  }, [run, load])

  /**
   * Ask the CLI what the change would run, then show that. The preview comes
   * from the same code that will execute it, so the dialog cannot describe
   * something different from what happens.
   */
  async function prepare(p: Pending) {
    setPendingChange(null)
    const res = await run(`prep:${p.kind}`, () => {
      switch (p.kind) {
        case 'dns-set':
          return invokeJson<NetworkChange>('dns_action', {
            action: 'set',
            provider: p.provider.id,
            adapter: null,
            doh: !!p.provider.dohTemplate,
            confirm: false,
          })
        case 'dns-revert':
          return invokeJson<NetworkChange>('dns_action', { action: 'revert', confirm: false })
        case 'fix':
          return invokeJson<NetworkChange>('net_action', { action: 'fix', level: p.level, confirm: false })
        case 'restart':
          return invokeJson<NetworkChange>('net_action', { action: 'restart', name: p.name, confirm: false })
        case 'public-ip':
          return invokeJson<NetworkChange>('net_action', { action: 'public-ip', confirm: false })
        case 'flush':
          // No preview needed: one command, no configuration changed.
          return Promise.resolve({
            action: 'dns-flush',
            success: true,
            unchanged: false,
            message: 'The resolver cache will be dropped.',
            affected: 0,
            needsElevation: false,
            log: '',
            preview: ['Clear-DnsClientCache', 'ipconfig /flushdns'],
            restartRequired: false,
          } satisfies NetworkChange)
      }
    })
    if (!res) return
    setPendingChange(res)
    setPending(p)
  }

  async function confirm() {
    if (!pending) return
    const p = pending
    setPending(null)
    const res = await run(`do:${p.kind}`, () => {
      switch (p.kind) {
        case 'dns-set':
          return invokeJson<NetworkChange>('dns_action', {
            action: 'set',
            provider: p.provider.id,
            adapter: null,
            doh: !!p.provider.dohTemplate,
            confirm: true,
          })
        case 'dns-revert':
          return invokeJson<NetworkChange>('dns_action', { action: 'revert', confirm: true })
        case 'flush':
          return invokeJson<NetworkChange>('dns_action', { action: 'flush' })
        case 'fix':
          return invokeJson<NetworkChange>('net_action', { action: 'fix', level: p.level, confirm: true })
        case 'restart':
          return invokeJson<NetworkChange>('net_action', { action: 'restart', name: p.name, confirm: true })
        case 'public-ip': {
          // The public address is data, not a change: it is shown inline.
          return invokeJson<{ ip: string | null; note: string | null }>('net_action', {
            action: 'public-ip',
            confirm: true,
          }).then(r =>
            ({
              action: 'public-ip',
              success: !!r.ip,
              unchanged: false,
              message: r.ip ? `${r.ip} — ${r.note ?? ''}` : (r.note ?? 'No answer.'),
              affected: 1,
              needsElevation: false,
              log: '',
              preview: [],
              restartRequired: false,
            }) satisfies NetworkChange,
          )
        }
      }
    })
    if (res) setChange(res)
    await load()
  }

  async function measure() {
    const res = await run('measure', () =>
      invokeJson<{ providers: DnsProvider[] }>('dns_action', { action: 'latency' }),
    )
    if (res) {
      setDns(d => (d ? { ...d, providers: res.providers } : d))
      setChange({
        action: 'latency',
        success: true,
        unchanged: false,
        message: 'Round trip measured from this machine just now. This is one machine at one moment — not a ranking.',
        affected: res.providers.length,
        needsElevation: false,
        log: '',
        preview: [],
        restartRequired: false,
      })
    }
  }

  async function resolve() {
    const res = await run('resolve', () =>
      invokeJson<LookupReport>('dns_action', {
        action: 'test',
        name: resolveName.trim() || 'example.com',
      }),
    )
    if (res) setDnsTest(res)
  }

  async function runTool() {
    const t = target.trim()
    if (!t) return
    if (tool === 'ping') {
      setTraceOut(null)
      setLookupOut(null)
      setPingOut(null)
      const res = await run('ping', () => invokeJson<PingReport>('net_action', { action: 'ping', host: t }))
      setPingOut(res)
    } else if (tool === 'trace') {
      setPingOut(null)
      setLookupOut(null)
      setTraceOut(null)
      const res = await run('trace', () =>
        invokeJson<TraceReport>('net_action', { action: 'trace', host: t, hops: 30 }),
      )
      setTraceOut(res)
    } else if (tool === 'lookup') {
      setPingOut(null)
      setTraceOut(null)
      setLookupOut(null)
      const res = await run('lookup', () =>
        invokeJson<LookupReport>('net_action', { action: 'lookup', name: t, kind: recordType }),
      )
      setLookupOut(res)
    } else {
      setPingOut(null)
      setTraceOut(null)
      setLookupOut(null)
      const res = await run('reverse', () =>
        invokeJson<LookupReport>('net_action', { action: 'reverse', value: t }),
      )
      setLookupOut(res)
    }
  }

  const activeName = useMemo(() => {
    if (!dns) return null
    const found = dns.providers.find(p => p.id === dns.activeProvider)
    return found ? found.name : dns.activeProvider === 'none' ? 'nothing' : dns.activeProvider
  }, [dns])

  const adapters = overview?.adapters ?? []

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <NetworkIcon size={22} className="text-[var(--color-primary)]" />
            Network
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            DNS, adapters, routes and the tools to tell which of them is lying
          </p>
        </div>
        <button
          className="btn btn-secondary btn-sm"
          onClick={() => void run('load', load)}
          disabled={busy !== null}
        >
          {busy === 'load' ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
          Refresh
        </button>
      </div>

      {error && (
        <div className="card flex items-start gap-3 border-[var(--color-danger)] animate-slide-up stagger-1">
          <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          <div className="text-[13px] text-[var(--color-danger)] break-words flex-1">{error}</div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setError(null)}>
            Dismiss
          </button>
        </div>
      )}

      {change && (
        <div
          className="card flex items-start gap-3 animate-slide-up stagger-1"
          style={{ borderColor: change.success ? 'rgba(34,197,94,0.35)' : 'rgba(239,68,68,0.4)' }}
        >
          {change.success ? (
            <CheckCircle2 size={16} className="text-[var(--color-success)] mt-0.5 flex-shrink-0" />
          ) : (
            <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          )}
          <div className="min-w-0 flex-1">
            <div className="text-[13px] break-words">{change.message}</div>
            {change.needsElevation && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1">
                Administrator rights are required, so nothing was changed.
              </div>
            )}
            {change.restartRequired && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1">
                A restart is needed to finish this.
              </div>
            )}
          </div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setChange(null)}>
            Dismiss
          </button>
        </div>
      )}

      {/* ── DNS ───────────────────────────────────────────────────── */}
      {dns && (
        <div className="card animate-slide-up stagger-1">
          <div className="flex items-center gap-2 mb-1 flex-wrap">
            <Globe size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Resolver</h3>
            <span className="badge badge-recommended">{activeName ?? dns.activeProvider}</span>
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {dns.ipv6Reachable
                ? 'IPv6 reachable'
                : 'no IPv6 route — v6 resolvers cannot be reached here'}
            </span>
            <button
              className="ml-auto btn btn-secondary btn-sm"
              onClick={() => void prepare({ kind: 'dns-revert' })}
              disabled={busy !== null}
            >
              Put DNS back
            </button>
          </div>

          <div className="space-y-2 mt-3">
            {dns.adapters.map(a => (
              <div
                key={a.index}
                className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2"
              >
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-[13px] font-medium">{a.name}</span>
                  <span className={`badge ${a.status === 'Up' ? 'badge-safe' : 'badge-optional'}`}>
                    {a.status}
                  </span>
                  {a.networkCategory && <span className="badge badge-optional">{a.networkCategory}</span>}
                  {a.changedByNovimize && <span className="badge badge-experimental">changed by Novimize</span>}
                  <span className="text-[11px] text-[var(--color-text-muted)] font-mono ml-auto">
                    {a.gateway ?? 'no gateway'}
                  </span>
                </div>
                <div className="text-[12px] font-mono mt-1 text-[var(--color-text-muted)]">
                  v4 {a.ipv4.length ? a.ipv4.join(', ') : '—'}
                  {a.dhcp4 && ' (DHCP)'}
                </div>
                <div className="text-[12px] font-mono text-[var(--color-text-muted)]">
                  v6 {a.ipv6.length ? a.ipv6.join(', ') : '—'}
                  {a.dhcp6 && ' (DHCP)'}
                </div>
              </div>
            ))}
          </div>

          <div className="mt-3 grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                DNS-over-HTTPS known to Windows
              </div>
              {dns.doh.length === 0 ? (
                <div className="text-[12px] text-[var(--color-text-muted)]">
                  Nothing configured supports DoH yet.
                </div>
              ) : (
                <div className="text-[11px] font-mono space-y-0.5 text-[var(--color-text-muted)]">
                  {dns.doh.slice(0, 6).map(d => (
                    <div key={d.address}>
                      {d.address} → {d.template ?? '—'}
                    </div>
                  ))}
                  {dns.doh.length > 6 && <div>… and {dns.doh.length - 6} more</div>}
                </div>
              )}
            </div>
            <div>
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                DNS-over-TLS
              </div>
              <div className="text-[12px] text-[var(--color-text-muted)] leading-relaxed">
                Windows’ built-in resolver does not speak DoT. DoH is the encrypted transport this OS
                offers; a DoT client would have to be installed and run separately.
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── Resolvers ─────────────────────────────────────────────── */}
      {dns && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-1">
            <Server size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Resolvers</h3>
            <button
              className="ml-auto btn btn-secondary btn-sm"
              onClick={() => void measure()}
              disabled={busy !== null}
              title="Ping each resolver from this machine"
            >
              {busy === 'measure' ? <Loader2 size={13} className="animate-spin" /> : <Play size={13} />}
              Measure
            </button>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
            Measured here, now. Not a ranking — latency depends on where you are and what your ISP
            does with plain DNS.
          </p>

          <div className="space-y-2">
            {dns.providers.map(p => (
              <div
                key={p.id}
                className="flex flex-wrap items-start gap-x-3 gap-y-2 rounded-lg border border-[var(--color-border)] px-3 py-2.5"
                style={
                  p.id === dns.activeProvider
                    ? { borderColor: 'rgba(34,197,94,0.45)' }
                    : undefined
                }
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="text-[13px] font-semibold">{p.name}</span>
                    {p.id === dns.activeProvider && <span className="badge badge-safe">in use</span>}
                    <span className="badge badge-optional">
                      {p.dohTemplate ? 'DoH' : 'plain DNS only'}
                    </span>
                    {p.latencyMs != null && (
                      <span className="badge badge-recommended">{p.latencyMs} ms</span>
                    )}
                  </div>
                  <div className="text-[12px] font-mono text-[var(--color-text-muted)] mt-1 break-all">
                    {p.ipv4.join(' · ')}
                    {p.ipv6.length > 0 && <> · {p.ipv6.join(' · ')}</>}
                  </div>
                  {p.note && (
                    <div className="text-[11px] text-[var(--color-text-muted)] mt-1">{p.note}</div>
                  )}
                </div>
                <button
                  className="btn btn-primary btn-sm flex-shrink-0"
                  onClick={() => void prepare({ kind: 'dns-set', provider: p })}
                  disabled={busy !== null}
                >
                  Use this…
                </button>
              </div>
            ))}
          </div>

          <div className="mt-3 flex gap-2">
            <input
              className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors font-mono"
              placeholder="example.com"
              value={resolveName}
              onChange={e => setResolveName(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter' && resolveName.trim()) void resolve()
              }}
            />
            <button
              className="btn btn-secondary"
              onClick={() => void resolve()}
              disabled={busy !== null || !resolveName.trim()}
            >
              {busy === 'resolve' ? <Loader2 size={14} className="animate-spin" /> : <Search size={14} />}
              Resolve
            </button>
          </div>
          {dnsTest && (
            <div className="mt-2 rounded-md bg-[var(--color-bg-elevated)] border border-[var(--color-border)] px-3 py-2 text-[12px]">
              {dnsTest.error ? (
                <span className="text-[var(--color-danger)]">{dnsTest.error}</span>
              ) : (
                <>
                  <span className="font-medium">{dnsTest.query}</span> {dnsTest.type} in{' '}
                  {dnsTest.elapsedMs} ms
                  <div className="font-mono text-[var(--color-text-muted)] mt-0.5">
                    {dnsTest.records.map(r => (
                      <div key={`${r.name}-${r.data}`}>{r.data}</div>
                    ))}
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      )}

      {/* ── Toolbox ───────────────────────────────────────────────── */}
      <div className="card animate-slide-up stagger-3">
        <div className="flex items-center gap-2 mb-3 flex-wrap">
          <Route size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">Toolbox</h3>
          <div className="ml-auto flex gap-1">
            {(['ping', 'trace', 'lookup', 'reverse'] as Tool[]).map(t => (
              <button
                key={t}
                className={`btn btn-sm ${tool === t ? 'btn-primary' : 'btn-ghost'}`}
                onClick={() => setTool(t)}
              >
                {t}
              </button>
            ))}
          </div>
        </div>

        <div className="flex gap-2">
          <input
            className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors font-mono"
            placeholder={
              tool === 'reverse' ? '1.1.1.1' : tool === 'lookup' ? 'example.com' : 'host or address'
            }
            value={target}
            onChange={e => setTarget(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') void runTool()
            }}
          />
          {tool === 'lookup' && (
            <select
              className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-2 py-2 text-[12px] outline-none focus:border-[var(--color-primary)]"
              value={recordType}
              onChange={e => setRecordType(e.target.value)}
            >
              {['A', 'AAAA', 'PTR', 'MX', 'TXT', 'CNAME'].map(t => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          )}
          <button
            className="btn btn-primary"
            onClick={() => void runTool()}
            disabled={busy !== null || !target.trim()}
          >
            {busy === 'ping' || busy === 'trace' || busy === 'lookup' || busy === 'reverse' ? (
              <Loader2 size={14} className="animate-spin" />
            ) : (
              <Play size={14} />
            )}
            Run
          </button>
        </div>

        {pingOut && (
          <div className="mt-3 rounded-md bg-[var(--color-bg-elevated)] border border-[var(--color-border)] px-3 py-2 text-[12px]">
            {pingOut.error && pingOut.sent === 0 ? (
              <span className="text-[var(--color-danger)]">{pingOut.error}</span>
            ) : (
              <>
                <span className="font-medium">{pingOut.host}</span>: {pingOut.sent} sent,{' '}
                {pingOut.lost} lost (
                {Math.round((pingOut.lost * 100) / Math.max(1, pingOut.sent))}%)
                {pingOut.average != null && (
                  <> · min {pingOut.min} · avg {pingOut.average} · max {pingOut.max} ms</>
                )}
              </>
            )}
          </div>
        )}

        {traceOut && (
          <div className="mt-3 rounded-md bg-[var(--color-bg-elevated)] border border-[var(--color-border)] px-3 py-2 text-[12px] max-h-72 overflow-y-auto">
            {traceOut.error && traceOut.hops.length === 0 ? (
              <span className="text-[var(--color-danger)]">{traceOut.error}</span>
            ) : (
              <>
                <div className="font-medium mb-1">
                  {traceOut.host}
                  {traceOut.targetIp && <> ({traceOut.targetIp})</>}
                </div>
                {traceOut.hops.map(h => (
                  <div key={h.hop} className="font-mono flex gap-3">
                    <span className="w-6 text-right">{h.hop}</span>
                    <span className="w-40 truncate">{h.host}</span>
                    <span className="text-[var(--color-text-muted)]">
                      {h.times.length ? h.times.map(t => `${t} ms`).join('  ') : 'no reply'}
                    </span>
                  </div>
                ))}
                <div className="mt-1 text-[var(--color-text-muted)]">
                  {traceOut.reached
                    ? 'Reached the target.'
                    : 'The last hop is not the target — it stopped early or the target did not answer.'}
                </div>
              </>
            )}
          </div>
        )}

        {lookupOut && (tool === 'lookup' || tool === 'reverse') && (
          <div className="mt-3 rounded-md bg-[var(--color-bg-elevated)] border border-[var(--color-border)] px-3 py-2 text-[12px]">
            {lookupOut.error ? (
              <span className="text-[var(--color-danger)]">{lookupOut.error}</span>
            ) : (
              <>
                <span className="font-medium">{lookupOut.query}</span> {lookupOut.type} in{' '}
                {lookupOut.elapsedMs} ms
                <div className="font-mono text-[var(--color-text-muted)] mt-0.5">
                  {lookupOut.records.map(r => (
                    <div key={`${r.name}-${r.data}`}>{r.data}</div>
                  ))}
                </div>
              </>
            )}
          </div>
        )}
      </div>

      {/* ── Adapters, routes, profiles, TCP ───────────────────────── */}
      {overview && (
        <div className="card animate-slide-up stagger-4">
          <div className="flex items-center gap-2 mb-3">
            <Wifi size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Adapters and routes</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {overview.localIp ?? '—'} · gateway {overview.gateway ?? '—'} · MTU{' '}
              {overview.mtu ?? '—'}
            </span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-[12px]">
              <thead>
                <tr className="text-left text-[10px] uppercase tracking-wider text-[var(--color-text-muted)]">
                  <th className="py-1 pr-3">Adapter</th>
                  <th className="py-1 pr-3">Status</th>
                  <th className="py-1 pr-3">Address</th>
                  <th className="py-1 pr-3">DNS</th>
                  <th className="py-1 pr-3">MTU</th>
                  <th className="py-1 pr-3">Profile</th>
                </tr>
              </thead>
              <tbody>
                {adapters.map(a => (
                  <tr key={a.name} className="border-t border-[var(--color-border)]">
                    <td className="py-1.5 pr-3 font-medium">
                      {a.name}
                      {!a.physical && (
                        <span className="text-[10px] text-[var(--color-text-muted)] ml-1">virtual</span>
                      )}
                    </td>
                    <td className="py-1.5 pr-3">{a.status}</td>
                    <td className="py-1.5 pr-3 font-mono">{a.ipv4.join(', ') || '—'}</td>
                    <td className="py-1.5 pr-3 font-mono">{a.dns4 || '—'}</td>
                    <td className="py-1.5 pr-3">{a.mtu ?? '—'}</td>
                    <td className="py-1.5 pr-3">{a.networkCategory ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mt-4">
            <div>
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Connection profiles ({overview.profiles.length})
              </div>
              {overview.profiles.map(p => (
                <div key={p.interfaceAlias} className="text-[12px] py-0.5">
                  {p.interfaceAlias} — {p.category} · v4 {p.ipv4Connectivity} · v6 {p.ipv6Connectivity}
                </div>
              ))}
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mt-3 mb-1">
                TCP/IP (absent = Windows default)
              </div>
              <div className="text-[12px] font-mono space-y-0.5 text-[var(--color-text-muted)]">
                {overview.tcp.values
                  .filter(v => v.value)
                  .map(v => (
                    <div key={v.key}>{v.key} = {v.value}</div>
                  ))}
                {overview.tcp.congestionProvider && (
                  <div>congestion = {overview.tcp.congestionProvider}</div>
                )}
                {overview.tcp.initialWindow && <div>auto-tuning = {overview.tcp.initialWindow}</div>}
              </div>
            </div>
            <div>
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Active routes ({overview.routes.length})
              </div>
              <div className="text-[11px] font-mono space-y-0.5 text-[var(--color-text-muted)] max-h-56 overflow-y-auto">
                {overview.routes.slice(0, 40).map((r, i) => (
                  <div key={`${r.destination}-${i}`}>
                    {r.destination} → {r.nextHop || 'on-link'} via {r.interface} metric {r.metric}
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── Actions ───────────────────────────────────────────────── */}
      <div className="card animate-slide-up stagger-5">
        <div className="flex items-center gap-2 mb-1">
          <Terminal size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">Fix Network</h3>
        </div>
        <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
          Every one of these shows the exact command lines before it runs them.
        </p>

        <div className="flex flex-wrap gap-2">
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => void prepare({ kind: 'flush' })}
            disabled={busy !== null}
          >
            Flush DNS cache
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => void prepare({ kind: 'fix', level: 'quick' })}
            disabled={busy !== null}
          >
            Quick fix
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => void prepare({ kind: 'fix', level: 'full' })}
            disabled={busy !== null}
            title="Adds the TCP/IP and Winsock resets; needs a restart"
          >
            Full reset…
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => void prepare({ kind: 'restart', name: adapters.find(a => a.physical && a.status === 'Up')?.name ?? '' })}
            disabled={busy !== null || !adapters.some(a => a.physical && a.status === 'Up')}
          >
            Restart adapter
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => void prepare({ kind: 'public-ip' })}
            disabled={busy !== null}
            title="Makes one outbound request to ask what your public address is"
          >
            Public IP…
          </button>
        </div>
      </div>

      {/* ── Confirmation ─────────────────────────────────────────── */}
      {pending && pendingChange && (
        <div className="modal-backdrop" onClick={() => setPending(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{
                  background: pending.kind === 'flush' ? 'rgba(59,130,246,0.15)' : 'rgba(239,68,68,0.15)',
                }}
              >
                {pending.kind === 'flush' ? (
                  <Shield size={19} className="text-[var(--color-primary)]" />
                ) : (
                  <AlertTriangle size={19} className="text-[var(--color-danger)]" />
                )}
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{pendingChange.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  {pendingChange.affected} step{pendingChange.affected === 1 ? '' : 's'} · nothing has
                  run yet
                </p>
              </div>
              <button
                className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1"
                onClick={() => setPending(null)}
              >
                ×
              </button>
            </div>

            <div className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2.5 mb-3">
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Commands
              </div>
              <div className="font-mono text-[11px] space-y-1 break-all">
                {pendingChange.preview.length === 0 ? (
                  <div className="text-[var(--color-text-muted)]">Nothing to run.</div>
                ) : (
                  pendingChange.preview.map(line => <div key={line}>{line}</div>)
                )}
              </div>
            </div>

            {pendingChange.restartRequired && (
              <div className="flex items-start gap-2 rounded-lg bg-amber-500/10 border border-amber-500/25 px-3 py-2.5 mb-3">
                <AlertTriangle size={15} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
                <div className="text-[12px] text-[var(--color-warning)]">
                  A restart is needed to finish this.
                </div>
              </div>
            )}

            <div className="flex justify-end gap-2">
              <button className="btn btn-secondary" onClick={() => setPending(null)}>
                Cancel
              </button>
              <button
                className={pending.kind === 'flush' ? 'btn btn-primary' : 'btn btn-danger'}
                onClick={() => void confirm()}
                disabled={busy !== null}
              >
                {busy !== null && <Loader2 size={14} className="animate-spin" />}
                Run it
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
