import { createLazyFileRoute, Link } from '@tanstack/react-router';

export const Route = createLazyFileRoute('/')({
  component: Index,
});

function Index() {
  return (
    <div className="p-8 flex flex-col items-center justify-center min-h-screen gap-4">
      <h1 className="text-4xl font-bold">Welcome to Axis</h1>
      <p className="text-lg text-muted-foreground">Multi-tenant low-code SaaS platform</p>
      <Link
        to="/login"
        className="inline-flex h-8 items-center justify-center rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/80"
      >
        Sign in
      </Link>
    </div>
  );
}
