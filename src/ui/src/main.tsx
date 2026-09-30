import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import './index.css'

// Set window icon at runtime for taskbar
async function setWindowIcon() {
  try {
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    const { resolveResource } = await import('@tauri-apps/api/path')
    const iconPath = await resolveResource('icons/icon-runtime.png')
    const response = await fetch(iconPath)
    const blob = await response.blob()
    const buffer = await blob.arrayBuffer()
    const rgba = new Uint8Array(buffer)
    getCurrentWindow().setIcon(rgba)
  } catch {
    // Silently fail - icon not critical
  }
}
setWindowIcon()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)
