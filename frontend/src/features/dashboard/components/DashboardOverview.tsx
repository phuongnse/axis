import { Button } from '@/components/ui/button';

const stats = [
  { label: 'Active workflows', value: '12' },
  { label: 'Running executions', value: '3' },
  { label: 'Pending form tasks', value: '5' },
  { label: 'Records created (7d)', value: '248' },
];

const activity = [
  { text: 'Alex Brown created workflow "Order Processing"', time: '2 hours ago' },
  { text: 'Jane Smith updated data model "Customer"', time: 'Yesterday' },
];

export function DashboardOverview() {
  return (
    <div className="space-y-8 max-w-6xl">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <h1 className="text-xl font-semibold text-foreground">Dashboard Overview</h1>
        <Button variant="cta" size="lg" disabled>
          + New Workflow
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {stats.map((stat) => (
          <div key={stat.label} className="rounded-xl border border-border bg-card p-5 shadow-sm">
            <p className="text-xs text-muted-foreground">{stat.label}</p>
            <p className="mt-2 text-2xl font-semibold text-foreground">{stat.value}</p>
          </div>
        ))}
      </div>

      <div className="rounded-xl border border-border bg-card shadow-sm overflow-hidden">
        <div className="border-b border-border bg-muted/40 px-5 py-3">
          <h2 className="text-sm font-medium text-foreground">Recent Activity</h2>
        </div>
        <ul className="divide-y divide-border px-5 py-2">
          {activity.map((row) => (
            <li
              key={row.text}
              className="flex flex-wrap items-center justify-between gap-2 py-4 text-sm"
            >
              <span className="text-foreground/90">{row.text}</span>
              <span className="text-xs text-muted-foreground">{row.time}</span>
            </li>
          ))}
        </ul>
        <div className="px-5 py-4 border-t border-border">
          <button type="button" className="text-sm text-primary hover:underline" disabled>
            View all activity →
          </button>
        </div>
      </div>
    </div>
  );
}
