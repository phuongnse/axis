# Frontend Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

> Full frontend rules. The non-negotiable enforcement rules are summarised in CLAUDE.md. This playbook covers patterns, examples, and rationale for TanStack Query, TypeScript, routing, component design, styling, security, and accessibility.

---

## Feature folder anatomy

Every feature lives under `frontend/src/features/{feature-name}/`:

```text
features/{feature-name}/
├── components/     # React components belonging to this feature
├── hooks/          # custom hooks (useXxx.ts)
├── api.ts          # all query/mutation functions for this feature
├── types.ts        # shared types for this feature
└── index.ts        # barrel export — public API of the feature
```

- Component files: `PascalCase.tsx`. Hook files: `camelCase.ts` with mandatory `use` prefix (`useWorkflows.ts`).
- Never import directly from another feature's `components/` or `hooks/` — only through its `index.ts`.
- Shared UI primitives (shadcn/ui): `src/components/ui/`. Shared utilities: `src/lib/`.

---

## State management

- **TanStack Query owns all server state.** Zustand owns global client-only state (UI flags, user preferences).
- Never store server data in Zustand; never cache client UI state in TanStack Query.
- **Forms**: `react-hook-form` + Zod. Define the Zod schema first (source of truth), infer TypeScript type via `z.infer<typeof schema>`.
- **Three async states required**: every data-fetching component handles loading (skeleton/spinner), empty (descriptive message), and error (message + retry). Silent empty render is a bug.

---

## TanStack Query patterns

- All `queryFn` and `mutationFn` definitions live in `features/{feature}/api.ts`. Never write them inline inside a component.
- Components call custom hooks — never call `useQuery`/`useMutation` directly with a `queryFn` in a component file.
- Each feature defines a **query key factory** to avoid magic strings:

```ts
export const workflowKeys = {
  all: ['workflows'] as const,
  list: (filters: WorkflowFilters) => [...workflowKeys.all, 'list', filters] as const,
  detail: (id: string) => [...workflowKeys.all, 'detail', id] as const,
}
```

- All mutations handle errors explicitly — surface via toast or inline message using the shared `ApiError` type.

---

## TypeScript discipline

- **No `any`**: strict mode on. Use `unknown` + type guards when shape is genuinely unknown. `any` is only acceptable at raw API response boundaries and must be typed away immediately.
- **No ungrounded `as T`**: requires an inline comment explaining why the compiler cannot infer it. Never use `as any` — use `as unknown as T` if a double assertion is truly necessary.
- **Entity IDs are `string`**: backend uses Guid serialised as string. Never type an entity ID as `number`.
- **No transformation in components**: if a different shape is needed, derive it in the hook or a selector — not inline in JSX.
- **Type co-location**: small prop interfaces and local type aliases may be co-located with the component that owns them. Shared types belong in a `types.ts` file within the feature folder.
- **Biome** is the single tool for linting and formatting (`frontend/biome.json`). Run `npm run lint:fix` to auto-fix, `npm run format` to format only.

---

## Routing

- All routes beyond the root are **lazy-loaded** — use TanStack Router's `lazy()` for code splitting.
- **Route protection**: auth guard logic lives in a root layout route (`loader` or `beforeLoad`), not inside individual page components.
- Global 401 handling (redirect to login) is wired once in `api.ts` / a root query error handler — never duplicated per feature.
- **Error Boundaries**: wrap every top-level route. Render a user-actionable fallback, never a blank screen.

---

## Component design

- **Single-purpose**: if a component both fetches server data AND contains complex conditional rendering, extract the data-fetching into a custom hook.
- **Composition over prop drilling**: use compound components or context for UI that shares state across more than two levels. Avoid prop chains longer than 2 hops.
- When props exceed 5 with no clear grouping, reconsider whether the component is doing too much — it likely needs to be split.
- Extract non-trivial logic into hooks. A component that is hard to test in isolation is a signal it carries too many responsibilities.

---

## Styling

- **Tailwind only** — no inline `style` prop.
- **`cn()` for conditional classes** — never string concatenation.
- Do not mix Tailwind utility classes and custom CSS on the same element.

---

## Security

- **No `dangerouslySetInnerHTML`** unless content is sanitised first (DOMPurify). Requires an explicit comment explaining the content source.
- **Environment variables**: public vars use `VITE_` prefix. Never expose secrets via `VITE_` — anything in `VITE_*` is bundled into the client.
- **No auth tokens in `localStorage`** — the backend uses `httpOnly` cookies via `credentials: 'include'` (already set in `fetchApi`).

---

## Performance — canvas and builder UIs

Large canvas or DnD builder features (React Flow, dnd-kit) require explicit render boundary management:

- Wrap node-level and item-level components in `React.memo` — rerenders in a graph propagate aggressively without it.
- Stabilise all callbacks passed as props with `useCallback`. Unstable function references bypass `memo` and cause full subtree rerenders.
- Never pass new object or array literals as props inline — extract them to `useMemo` or module-level constants.
- Keep store selectors granular: subscribe to the smallest slice of Zustand state needed. Coarse selectors cause the entire canvas to rerender on unrelated state changes.
- Never pass inline object or function literals as props into deeply nested builder components — they are new references on every render and bypass `memo` entirely.
- Memoize expensive derived structures (filtered node lists, computed edge maps, layout calculations) with `useMemo`. Recomputing them on every render compounds quickly in graph UIs.
- Profile with React DevTools before assuming performance is acceptable. Canvas/builder UIs hide rerender storms until data volume increases.

---

## Accessibility baseline

- Every form input must have a `<label>` or `aria-label`.
- Every icon-only button must have `aria-label`.
- Never use colour as the sole indicator — error/warning states require text or icon alongside colour.
