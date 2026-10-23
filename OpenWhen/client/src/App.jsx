import { useEffect, useState } from 'react'

export default function App() {
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false

    fetch('/api/hello')
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`Request failed (${response.status})`)
        }
        return response.json()
      })
      .then((body) => {
        if (!cancelled) {
          setMessage(body.message ?? '')
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load the message')
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main>
      <p className="eyebrow">Hobby hello</p>
      <h1>OpenWhen</h1>
      {error ? (
        <p className="message error" role="alert">{error}</p>
      ) : (
        <p className="message" aria-live="polite">
          {message || 'Loading…'}
        </p>
      )}
    </main>
  )
}
