import { createLazyFileRoute } from '@tanstack/react-router';

export const Route = createLazyFileRoute('/_authenticated/dashboard')({
  component: DashboardPage,
});

function DashboardPage() {
  return (
    <div>
      <h1 className="text-2xl font-bold mb-2">Workspace</h1>
      <p className="text-muted-foreground">
        You are signed in. Module screens (workflows, data models, forms) will appear here as they
        are implemented.
      </p>
    </div>
  );
}
