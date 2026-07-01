import { useEffect, useMemo, useRef } from 'react';

import { cn } from '@/lib/utils';

interface BackdropLine {
  id: number;
  xRatio: number;
  yRatio: number;
  lengthRatio: number;
  angle: number;
  minAngle: number;
  maxAngle: number;
  speedX: number;
  speedY: number;
  angleSpeed: number;
  color: string;
  opacity: number;
  thickness: number;
  glow: number;
}

interface RuntimeLine extends BackdropLine {
  centerX: number;
  centerY: number;
  length: number;
}

interface DistributionSlot {
  column: number;
  row: number;
  columns: number;
  rows: number;
}

const motionLineColors = [
  'hsl(var(--primary) / 0.46)',
  'hsl(var(--secondary) / 0.42)',
  'hsl(var(--chart-3) / 0.34)',
  'hsl(var(--muted-foreground) / 0.32)',
  'hsl(var(--foreground) / 0.2)',
];

const lineCount = 12;
const initialSpacingPasses = 10;
const minimumSkewAngle = 18;

function randomBetween(min: number, max: number) {
  return min + Math.random() * (max - min);
}

function randomSign() {
  return Math.random() > 0.5 ? 1 : -1;
}

function shuffleItems<T>(items: T[]) {
  const shuffledItems = [...items];

  for (let index = shuffledItems.length - 1; index > 0; index -= 1) {
    const nextIndex = Math.floor(Math.random() * (index + 1));
    [shuffledItems[index], shuffledItems[nextIndex]] = [
      shuffledItems[nextIndex],
      shuffledItems[index],
    ];
  }

  return shuffledItems;
}

function createDistributionSlots(count: number): DistributionSlot[] {
  const columns = 4;
  const rows = Math.ceil(count / columns);
  const slots = Array.from({ length: rows }, (_, row) =>
    Array.from({ length: columns }, (_, column) => ({
      column,
      row,
      columns,
      rows,
    })),
  ).flat();

  return shuffleItems(slots).slice(0, count);
}

function getSlotRatio(slot: DistributionSlot) {
  return {
    xRatio: (slot.column + randomBetween(0.2, 0.8)) / slot.columns,
    yRatio: (slot.row + randomBetween(0.2, 0.8)) / slot.rows,
  };
}

function createBackdropLines(): BackdropLine[] {
  const slots = createDistributionSlots(lineCount);

  return Array.from({ length: lineCount }, (_, id) => {
    const angleSign = randomSign();
    const angleSwing = randomBetween(3, 7);
    const absoluteAngle = randomBetween(minimumSkewAngle + angleSwing, 42);
    const angle = angleSign * absoluteAngle;
    const minAngle = angleSign > 0 ? absoluteAngle - angleSwing : -(absoluteAngle + angleSwing);
    const maxAngle = angleSign > 0 ? absoluteAngle + angleSwing : -(absoluteAngle - angleSwing);
    const { xRatio, yRatio } = getSlotRatio(slots[id]);

    return {
      id,
      xRatio,
      yRatio,
      lengthRatio: randomBetween(0.14, 0.56),
      angle,
      minAngle,
      maxAngle,
      speedX: randomSign() * randomBetween(3, 8),
      speedY: randomSign() * randomBetween(2, 6),
      angleSpeed: randomSign() * randomBetween(0.06, 0.22),
      color: motionLineColors[Math.floor(Math.random() * motionLineColors.length)],
      opacity: randomBetween(0.16, 0.34),
      thickness: randomBetween(0.45, 0.85),
      glow: randomBetween(3, 8),
    };
  });
}

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function getProjectedBounds(line: RuntimeLine) {
  const angle = (Math.abs(line.angle) * Math.PI) / 180;
  const width =
    Math.abs(Math.cos(angle)) * line.length + Math.abs(Math.sin(angle)) * line.thickness;
  const height =
    Math.abs(Math.sin(angle)) * line.length + Math.abs(Math.cos(angle)) * line.thickness;

  return {
    halfWidth: width / 2,
    halfHeight: height / 2,
  };
}

function getMinimumLineDistance(width: number, height: number) {
  return clamp(Math.min(width, height) * 0.22, 76, 180);
}

function constrainLine(line: RuntimeLine, width: number, height: number) {
  const projectedBounds = getProjectedBounds(line);

  line.centerX = clamp(line.centerX, projectedBounds.halfWidth, width - projectedBounds.halfWidth);
  line.centerY = clamp(
    line.centerY,
    projectedBounds.halfHeight,
    height - projectedBounds.halfHeight,
  );
}

function limitLineSpeed(line: RuntimeLine) {
  const maxSpeed = 10;
  const speed = Math.hypot(line.speedX, line.speedY);

  if (speed > maxSpeed) {
    const scale = maxSpeed / speed;
    line.speedX *= scale;
    line.speedY *= scale;
  }
}

function separateLines(
  lines: RuntimeLine[],
  width: number,
  height: number,
  strength: number,
  adjustVelocity: boolean,
) {
  const minimumDistance = getMinimumLineDistance(width, height);

  for (let index = 0; index < lines.length; index += 1) {
    for (let nextIndex = index + 1; nextIndex < lines.length; nextIndex += 1) {
      const currentLine = lines[index];
      const nextLine = lines[nextIndex];
      const xDistance = nextLine.centerX - currentLine.centerX;
      const yDistance = nextLine.centerY - currentLine.centerY;
      const distance = Math.hypot(xDistance, yDistance) || 1;

      if (distance >= minimumDistance) {
        continue;
      }

      const xDirection = xDistance / distance;
      const yDirection = yDistance / distance;
      const pushDistance = ((minimumDistance - distance) / 2) * strength;

      currentLine.centerX -= xDirection * pushDistance;
      currentLine.centerY -= yDirection * pushDistance;
      nextLine.centerX += xDirection * pushDistance;
      nextLine.centerY += yDirection * pushDistance;

      if (adjustVelocity) {
        const velocityPush = strength * 0.42;

        currentLine.speedX -= xDirection * velocityPush;
        currentLine.speedY -= yDirection * velocityPush;
        nextLine.speedX += xDirection * velocityPush;
        nextLine.speedY += yDirection * velocityPush;
        limitLineSpeed(currentLine);
        limitLineSpeed(nextLine);
      }

      constrainLine(currentLine, width, height);
      constrainLine(nextLine, width, height);
    }
  }
}

function applyLineTransform(element: HTMLDivElement, line: RuntimeLine) {
  element.style.width = `${line.length}px`;
  element.style.transform = `translate3d(${line.centerX}px, ${line.centerY}px, 0) translate(-50%, -50%) rotate(${line.angle}deg)`;
}

export function TopologyBackdrop({ className }: { className?: string }) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const lineRefs = useRef<Array<HTMLDivElement | null>>([]);
  const lines = useMemo(createBackdropLines, []);

  useEffect(() => {
    const container = containerRef.current;

    if (!container) {
      return;
    }

    const motionQuery = window.matchMedia?.('(prefers-reduced-motion: reduce)');
    let frameId = 0;
    let lastTime = 0;
    let runtimeLines: RuntimeLine[] = [];

    const cancelAnimation = () => {
      if (frameId) {
        window.cancelAnimationFrame(frameId);
        frameId = 0;
      }
    };

    const resetLines = () => {
      const { width, height } = container.getBoundingClientRect();

      runtimeLines = lines.map((line) => {
        const length = clamp(width * line.lengthRatio, Math.min(width * 0.34, 120), width * 0.62);
        const runtimeLine = {
          ...line,
          centerX: line.xRatio * width,
          centerY: line.yRatio * height,
          length,
        };
        constrainLine(runtimeLine, width, height);

        return runtimeLine;
      });

      for (let pass = 0; pass < initialSpacingPasses; pass += 1) {
        separateLines(runtimeLines, width, height, 0.9, false);
      }

      for (const runtimeLine of runtimeLines) {
        const element = lineRefs.current[runtimeLine.id];

        if (element) {
          applyLineTransform(element, runtimeLine);
        }
      }
    };

    const animate = (time: number) => {
      const { width, height } = container.getBoundingClientRect();
      const deltaSeconds = Math.min((time - lastTime) / 1000 || 0, 0.08);

      lastTime = time;

      for (const runtimeLine of runtimeLines) {
        runtimeLine.centerX += runtimeLine.speedX * deltaSeconds;
        runtimeLine.centerY += runtimeLine.speedY * deltaSeconds;
        runtimeLine.angle += runtimeLine.angleSpeed * deltaSeconds;

        if (runtimeLine.angle < runtimeLine.minAngle || runtimeLine.angle > runtimeLine.maxAngle) {
          runtimeLine.angle = clamp(runtimeLine.angle, runtimeLine.minAngle, runtimeLine.maxAngle);
          runtimeLine.angleSpeed *= -1;
        }

        const projectedBounds = getProjectedBounds(runtimeLine);

        if (runtimeLine.centerX <= projectedBounds.halfWidth) {
          runtimeLine.centerX = projectedBounds.halfWidth;
          runtimeLine.speedX = Math.abs(runtimeLine.speedX);
        } else if (runtimeLine.centerX >= width - projectedBounds.halfWidth) {
          runtimeLine.centerX = width - projectedBounds.halfWidth;
          runtimeLine.speedX = -Math.abs(runtimeLine.speedX);
        }

        if (runtimeLine.centerY <= projectedBounds.halfHeight) {
          runtimeLine.centerY = projectedBounds.halfHeight;
          runtimeLine.speedY = Math.abs(runtimeLine.speedY);
        } else if (runtimeLine.centerY >= height - projectedBounds.halfHeight) {
          runtimeLine.centerY = height - projectedBounds.halfHeight;
          runtimeLine.speedY = -Math.abs(runtimeLine.speedY);
        }
      }

      separateLines(runtimeLines, width, height, Math.min(deltaSeconds * 2.2, 0.18), true);

      for (const runtimeLine of runtimeLines) {
        limitLineSpeed(runtimeLine);

        const element = lineRefs.current[runtimeLine.id];

        if (element) {
          applyLineTransform(element, runtimeLine);
        }
      }

      frameId = window.requestAnimationFrame(animate);
    };

    const start = () => {
      cancelAnimation();
      resetLines();

      if (!motionQuery?.matches) {
        lastTime = performance.now();
        frameId = window.requestAnimationFrame(animate);
      }
    };

    const resizeObserver = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(start);

    if (resizeObserver) {
      resizeObserver.observe(container);
    } else {
      window.addEventListener('resize', start);
    }

    motionQuery?.addEventListener('change', start);
    start();

    return () => {
      cancelAnimation();
      resizeObserver?.disconnect();
      window.removeEventListener('resize', start);
      motionQuery?.removeEventListener('change', start);
    };
  }, [lines]);

  return (
    <div
      ref={containerRef}
      aria-hidden
      className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}
    >
      <div className="absolute inset-0 bg-background" />
      <div className="absolute inset-0 opacity-35 [background-image:radial-gradient(circle_at_18%_22%,hsl(var(--primary)/0.18),transparent_26%),radial-gradient(circle_at_78%_18%,hsl(var(--secondary)/0.16),transparent_24%),linear-gradient(135deg,transparent_0%,hsl(var(--muted)/0.18)_45%,transparent_70%)]" />

      {lines.map((line) => (
        <div
          key={line.id}
          ref={(element) => {
            lineRefs.current[line.id] = element;
          }}
          className="absolute left-0 top-0 rounded-full will-change-transform"
          style={{
            height: `${line.thickness}px`,
            opacity: line.opacity,
            background: `linear-gradient(90deg, transparent, ${line.color}, transparent)`,
            boxShadow: `0 0 ${line.glow}px ${line.color}`,
            transform: `translate3d(${line.xRatio * 100}vw, ${line.yRatio * 100}vh, 0) translate(-50%, -50%) rotate(${line.angle}deg)`,
            width: `${line.lengthRatio * 100}%`,
          }}
        />
      ))}

      <div className="absolute inset-0 bg-[linear-gradient(180deg,hsl(var(--background)/0.22),transparent_42%,hsl(var(--background)/0.72))]" />
    </div>
  );
}
