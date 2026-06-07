import { cn } from '@/lib/utils';

const nodes = [
  { id: 'identity', x: 150, y: 155, tone: 'primary' },
  { id: 'session', x: 345, y: 120, tone: 'accent' },
  { id: 'boundary', x: 555, y: 190, tone: 'blue' },
  { id: 'workspace', x: 810, y: 135, tone: 'muted' },
  { id: 'console', x: 1045, y: 225, tone: 'primary' },
  { id: 'roles', x: 285, y: 430, tone: 'muted' },
  { id: 'policy', x: 510, y: 505, tone: 'accent' },
  { id: 'handoff', x: 785, y: 455, tone: 'blue' },
  { id: 'control', x: 1085, y: 510, tone: 'muted' },
  { id: 'signin', x: 190, y: 632, tone: 'muted' },
  { id: 'verify', x: 448, y: 650, tone: 'blue' },
  { id: 'scope', x: 718, y: 632, tone: 'accent' },
  { id: 'enter', x: 1010, y: 652, tone: 'primary' },
] as const;

const links = [
  { path: 'M150 155 C235 95 275 102 345 120', tone: 'primary' },
  { path: 'M345 120 C435 116 492 155 555 190', tone: 'accent' },
  { path: 'M555 190 C650 215 718 150 810 135', tone: 'blue' },
  { path: 'M810 135 C910 128 985 175 1045 225', tone: 'primary' },
  { path: 'M345 120 C470 210 180 326 285 430', tone: 'muted' },
  { path: 'M285 430 C365 455 435 492 510 505', tone: 'accent' },
  { path: 'M510 505 C615 455 690 435 785 455', tone: 'blue' },
  { path: 'M785 455 C880 425 1010 455 1085 510', tone: 'muted' },
  { path: 'M555 190 C585 315 682 405 785 455', tone: 'blue' },
  { path: 'M190 632 C275 658 365 660 448 650', tone: 'muted' },
  { path: 'M448 650 C540 630 625 622 718 632', tone: 'blue' },
  { path: 'M718 632 C820 660 930 665 1010 652', tone: 'accent' },
  { path: 'M785 455 C770 535 760 585 718 632', tone: 'blue' },
  { path: 'M1085 510 C1098 590 1070 630 1010 652', tone: 'primary' },
] as const;

const zones = [
  { id: 'access', x: 72, y: 66, width: 510, height: 224 },
  { id: 'handoff', x: 462, y: 306, width: 500, height: 242 },
  { id: 'control', x: 858, y: 132, width: 280, height: 410 },
  { id: 'entry', x: 112, y: 574, width: 1038, height: 96 },
] as const;

const toneColor: Record<(typeof nodes)[number]['tone'], string> = {
  primary: 'hsl(var(--primary))',
  accent: 'hsl(var(--accent))',
  blue: 'hsl(202 53% 43%)',
  muted: 'hsl(var(--muted-foreground))',
};

const nodeBoxPath = 'M-28 -12 H21 Q28 -12 28 -5 V5 Q28 12 21 12 H-28 Z';

export function TopologyBackdrop({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}
    >
      <svg
        className="h-full w-full"
        viewBox="0 0 1240 700"
        preserveAspectRatio="xMidYMid slice"
        role="presentation"
      >
        <g opacity="0.5">
          {zones.map((zone) => (
            <rect
              key={zone.id}
              x={zone.x}
              y={zone.y}
              width={zone.width}
              height={zone.height}
              rx="12"
              fill="none"
              stroke="hsl(var(--border) / 0.5)"
              strokeDasharray="2 14"
            />
          ))}
        </g>

        <g opacity="0.64">
          {links.map((link) => (
            <path
              key={link.path}
              d={link.path}
              fill="none"
              stroke={toneColor[link.tone]}
              strokeDasharray="5 9"
              strokeLinecap="round"
              strokeOpacity="0.36"
              strokeWidth="1.25"
            />
          ))}
        </g>

        <g>
          {nodes.map((node) => (
            <g key={node.id} transform={`translate(${node.x} ${node.y})`} opacity="0.78">
              <path
                d={nodeBoxPath}
                fill="hsl(var(--card) / 0.64)"
                stroke="hsl(var(--muted-foreground) / 0.3)"
              />
              <rect
                x="-28"
                y="-12"
                width="3"
                height="24"
                rx="1.5"
                fill={toneColor[node.tone]}
                opacity="0.82"
              />
              <circle cx="-8" cy="0" r="2" fill={toneColor[node.tone]} opacity="0.64" />
              <path
                d="M2 0 H18"
                fill="none"
                stroke="hsl(var(--muted-foreground) / 0.4)"
                strokeLinecap="round"
                strokeWidth="1.4"
              />
            </g>
          ))}
        </g>
      </svg>
      <div className="absolute inset-0 bg-background/10 dark:bg-background/30" />
    </div>
  );
}
