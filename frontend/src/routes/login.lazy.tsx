import { createLazyFileRoute, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { buildAuthorizeUrl, createPkceSession } from '@/features/auth/pkce';

export const Route = createLazyFileRoute('/login')({
  component: LoginPage,
});

function LoginPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const pkce = createPkceSession();
      const authorizeUrl = await buildAuthorizeUrl(pkce.state, pkce.verifier);
      const body = new URLSearchParams({
        email,
        password,
        return_url: `${window.location.origin}${authorizeUrl}`,
      });

      const response = await fetch('/connect/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body,
        credentials: 'include',
        redirect: 'manual',
      });

      if (response.status === 401) {
        setError('Incorrect email or password');
        setLoading(false);
        return;
      }

      const location = response.headers.get('Location');
      if (location) {
        window.location.href = location.startsWith('http')
          ? location
          : `${window.location.origin}${location}`;
        return;
      }

      window.location.href = authorizeUrl;
    } catch {
      setError('Something went wrong. Please try again.');
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <form
        onSubmit={handleSubmit}
        className="w-full max-w-sm space-y-4 border rounded-lg p-6 shadow-sm bg-card"
      >
        <h1 className="text-2xl font-semibold text-center">Sign in to Axis</h1>
        {error ? <p className="text-sm text-destructive">{error}</p> : null}
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium">
            Email
          </label>
          <input
            id="email"
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="w-full rounded-md border px-3 py-2 text-sm"
          />
        </div>
        <div className="space-y-2">
          <label htmlFor="password" className="text-sm font-medium">
            Password
          </label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="w-full rounded-md border px-3 py-2 text-sm"
          />
        </div>
        <Button type="submit" className="w-full" disabled={loading}>
          {loading ? 'Signing in…' : 'Sign in'}
        </Button>
        <button
          type="button"
          className="w-full text-sm text-muted-foreground hover:underline"
          onClick={() => navigate({ to: '/' })}
        >
          Back to home
        </button>
      </form>
    </div>
  );
}
