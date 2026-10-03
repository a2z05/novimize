import { useState } from 'react'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import Layout from './components/Layout'
import OnboardingWizard from './components/OnboardingWizard'
import Dashboard from './pages/Dashboard'
import ScanOptimize from './pages/ScanOptimize'
import Profiles from './pages/Profiles'
import Snapshots from './pages/Snapshots'
import Diagnostics from './pages/Diagnostics'
import Gaming from './pages/Gaming'
import Exclusions from './pages/Exclusions'
import Blocker from './pages/Blocker'
import Network from './pages/Network'
import Install from './pages/Install'
import Appearance from './pages/Appearance'
import Settings from './pages/Settings'

const ONBOARDING_KEY = 'novimize_onboarding_complete'

export default function App() {
  const [showOnboarding, setShowOnboarding] = useState(() => {
    try {
      return localStorage.getItem(ONBOARDING_KEY) !== 'true'
    } catch {
      return true
    }
  })

  function handleOnboardingComplete() {
    try {
      localStorage.setItem(ONBOARDING_KEY, 'true')
    } catch {}
    setShowOnboarding(false)
  }

  return (
    <BrowserRouter>
      {showOnboarding && (
        <OnboardingWizard onComplete={handleOnboardingComplete} />
      )}
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Dashboard />} />
          <Route path="/scan" element={<ScanOptimize />} />
          <Route path="/profiles" element={<Profiles />} />
          <Route path="/snapshots" element={<Snapshots />} />
          <Route path="/gaming" element={<Gaming />} />
          <Route path="/install" element={<Install />} />
          <Route path="/appearance" element={<Appearance />} />
          <Route path="/exclusions" element={<Exclusions />} />
          <Route path="/blocker" element={<Blocker />} />
          <Route path="/network" element={<Network />} />
          <Route path="/diagnostics" element={<Diagnostics />} />
          <Route path="/settings" element={<Settings />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
