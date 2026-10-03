import { useCallback, useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  Settings as SettingsIcon,
  Folder,
  Terminal,
  Monitor,
  Loader2,
  RefreshCw,
} from 'lucide-react'
import type { AppUpdateStatus } from '../types'

export default function Settings() {
  const [cliPath, setCliPath] = useState('')
  const [autoScan, setAutoScan] = useState(false)
  const [release, setRelease] = useState<AppUpdateStatus | null>(null)
  const [checking, setChecking] = useState(false)

  /**
   * The version on screen comes from the check rather than a string in the
   * file, so it cannot drift from what the updater compares against — and
   * the check runs once, on load, because a page that phones home on every
   * render is a page that phones home.
   */
  const check = useCallback(async () => {
    setChecking(true)
    try {
      setRelease(await invokeJson<AppUpdateStatus>('appupdate', { action: 'check' }))
    } catch {
      setRelease(null)
    } finally {
      setChecking(false)
    }
  }, [])

  useEffect(() => { void check() }, [check])

  return (
    <div className="space-y-6 max-w-2xl">
      <div>
        <h1 className="text-2xl font-bold">Settings</h1>
        <p className="text-sm text-[var(--color-text-muted)] mt-1">
          Configure Novimize preferences
        </p>
      </div>

      {/* General */}
      <div className="card">
        <div className="flex items-center gap-2 mb-4">
          <SettingsIcon size={16} className="text-[var(--color-primary)]" />
          <h2 className="font-semibold text-sm">General</h2>
        </div>

        <div className="space-y-4">
          {/* Auto-scan */}
          <div className="flex items-center justify-between">
            <div>
              <div className="text-sm font-medium">Auto-Scan on Startup</div>
              <div className="text-xs text-[var(--color-text-muted)]">
                Automatically scan system tweaks when the app starts
              </div>
            </div>
            <button
              onClick={() => setAutoScan(!autoScan)}
              className={`relative w-11 h-6 rounded-full transition-colors ${
                autoScan ? 'bg-[var(--color-primary)]' : 'bg-[var(--color-border)]'
              }`}
            >
              <div className={`absolute top-1 w-4 h-4 rounded-full bg-white transition-transform ${
                autoScan ? 'translate-x-6' : 'translate-x-1'
              }`} />
            </button>
          </div>
        </div>
      </div>

      {/* Theme */}
      <div className="card">
        <div className="flex items-center gap-2 mb-4">
          <Monitor size={16} className="text-[var(--color-primary)]" />
          <h2 className="font-semibold text-sm">Appearance</h2>
        </div>

        <div>
          <div className="text-sm font-medium mb-2">Theme</div>
          <div className="flex gap-3">
            <button className="flex-1 card border-[var(--color-primary)] py-3 text-center text-sm font-medium">
              Dark
            </button>
            <button className="flex-1 card py-3 text-center text-sm font-medium text-[var(--color-text-muted)] opacity-50 cursor-not-allowed">
              Light (coming soon)
            </button>
          </div>
        </div>
      </div>

      {/* Advanced */}
      <div className="card">
        <div className="flex items-center gap-2 mb-4">
          <Terminal size={16} className="text-[var(--color-primary)]" />
          <h2 className="font-semibold text-sm">Advanced</h2>
        </div>

        <div className="space-y-4">
          {/* CLI Path */}
          <div>
            <div className="text-sm font-medium mb-2">CLI Path</div>
            <div className="flex gap-2">
              <input
                type="text"
                value={cliPath}
                onChange={e => setCliPath(e.target.value)}
                placeholder="Auto-detect"
                className="flex-1 bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-3 py-2 text-sm outline-none focus:border-[var(--color-primary)] transition-colors"
              />
              <button className="btn btn-secondary">
                <Folder size={14} />
                Browse
              </button>
            </div>
            <div className="text-xs text-[var(--color-text-muted)] mt-1">
              Leave empty to auto-detect from installation directory
            </div>
          </div>
        </div>
      </div>

      {/* About */}
      <div className="card">
        <div className="text-sm text-[var(--color-text-muted)]">
          <div className="flex justify-between py-1">
            <span>Version</span>
            <span>{release?.currentVersion ?? '—'}</span>
          </div>
          <div className="flex justify-between py-1">
            <span>Latest published</span>
            <span className="flex items-center gap-2">
              {checking ? (
                <Loader2 size={12} className="animate-spin" />
              ) : release?.checkFailed ? (
                <span className="text-[var(--color-danger)]">could not check</span>
              ) : release ? (
                release.updateAvailable ? (
                  <span className="text-[var(--color-warning)]">
                    {release.latestVersion} available
                  </span>
                ) : (
                  <span className="text-[var(--color-success)]">
                    {release.latestVersion} — up to date
                  </span>
                )
              ) : (
                '—'
              )}
              <button
                className="btn btn-ghost btn-sm px-1.5"
                onClick={() => void check()}
                disabled={checking}
                title="Check the published release"
              >
                <RefreshCw size={12} />
              </button>
            </span>
          </div>
          {release && !release.checkFailed && release.url && (
            <div className="flex justify-between py-1">
              <span>Release</span>
              <a
                className="text-[var(--color-primary)] hover:underline"
                href={release.url}
                target="_blank"
                rel="noreferrer"
                onClick={e => {
                  // The webview must not navigate the app itself away.
                  e.preventDefault()
                  void invokeJson('open_external', { url: release.url }).catch(() => undefined)
                }}
              >
                {release.tag || release.name}
              </a>
            </div>
          )}
          {release?.error && (
            <div className="flex justify-between py-1">
              <span>Last check</span>
              <span className="text-[var(--color-danger)] max-w-[60%] text-right break-words">
                {release.error}
              </span>
            </div>
          )}
          <div className="flex justify-between py-1">
            <span>Engine</span>
            <span>.NET 8</span>
          </div>
          <div className="flex justify-between py-1">
            <span>Shell</span>
            <span>Tauri v2</span>
          </div>
          <div className="flex justify-between py-1 pt-2 mt-2 border-t border-[var(--color-border)]">
            <span>Created by</span>
            <span className="text-[var(--color-text)] font-medium">a2z</span>
          </div>
        </div>
      </div>
    </div>
  )
}
