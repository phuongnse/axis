import { createLazyFileRoute } from '@tanstack/react-router';

export const Route = createLazyFileRoute('/')({
  component: Index,
});

function Index() {
  return (
    <div className="p-8 flex flex-col items-center justify-center min-h-screen">
      <h1 className="text-4xl font-bold mb-4">Welcome to Axis</h1>
      <p className="text-lg text-muted-foreground">Multi-tenant low-code SaaS platform</p>
    </div>
  );
}
