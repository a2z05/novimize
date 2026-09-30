import { Outlet, useLocation } from 'react-router-dom'
import { useEffect, useState } from 'react'
import Sidebar from './Sidebar'
import TitleBar from './TitleBar'

export default function Layout() {
  const location = useLocation()
  const [animKey, setAnimKey] = useState(0)

  useEffect(() => {
    setAnimKey(k => k + 1)
  }, [location.pathname])

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      <TitleBar />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar />
        <main className="flex-1 overflow-y-auto p-6" key={animKey}
              style={{ animation: 'slideUp 300ms cubic-bezier(0.16,1,0.3,1) both' }}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}
