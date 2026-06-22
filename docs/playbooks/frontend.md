# Frontend Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

> Full frontend rules. The non-negotiable enforcement rules are summarised in AGENTS.md. This playbook covers patterns, examples, and rationale for TanStack Query, TypeScript, routing, component design, styling, security, and accessibility.

---

## UX-first product UI

UI exists to help users complete work. Visual style matters, but it is secondary to clarity, usefulness, and efficient task completion.

- Start every screen by naming the user goal, the decision the user must make, and the minimum information needed to make that decision.
- Keep content honest. Do not show fake operational data, workspace names, metrics, event streams, or statuses. Authenticated workspace screens may show workspace metrics only when they are backed by API data or a clearly labeled real product state.
- Do not describe architecture to end users. Replace internal terms such as "surface", "boundary", "session", or "gateway" with user-facing language such as "sign in", "verify access", and "open workspace".
- Every visible element must answer one of these questions: What is this? What can I do next? What do I need to know before acting? If not, remove it.
- A screen should lead the user toward action, not feel like it is explaining the system. If the screen starts reading like an architecture note, simplify the copy and move the explanation out of the UI.
- Decorative backgrounds should not carry unexplained text labels. If a background needs visual structure, prefer abstract shapes, paths, or texture; reserve readable text for the active task surface.
- Removing unnecessary content is not a reason to add filler. Fix sparse screens by tightening layout dimensions, improving hierarchy, moving actions closer to the decision point, and using purposeful whitespace.
- Prefer task copy over decorative copy. Labels, helper text, empty states, and errors should be short, specific, and action-oriented.
- Use visual design to support scanning and confidence. Avoid decorative panels, fake dashboards, oversized borders, or dense chrome when they do not improve the task.
- Public/auth screens should focus on access and trust. Authenticated workspace screens can show workspace data, operational status, and metrics.
- UI polish follows UX: spacing, color, icons, and motion should make the workflow easier to understand, not make the screen feel more complex.

Design-system rules live in [design-system.md](./design-system.md). Use that owner for tokens, component inventory, pixel-perfect criteria, and visual QA. This playbook owns frontend implementation patterns.

---

## Mobile-first layout and radius

- Build mobile-first: base classes must work on the smallest supported viewport, then add `sm:`, `md:`, `lg:`, and `xl:` enhancements. Do not make the desktop layout the only usable layout.
- Critical navigation and account actions must remain reachable on mobile. Desktop sidebars can collapse or move into a top/horizontal navigation, but the user must not lose the path to the main sections.
- Verify responsive surfaces at small phone, tablet, and desktop widths before claiming UI work is complete. Use `360px`, `768px`, and `1280px` as the default sanity check set unless the feature has a tighter target.
- Text must wrap or truncate intentionally at mobile widths. Do not rely on viewport-scaled font sizes to make text fit.
- Radius uses the shared Tailwind token scale: `rounded-sm` = 4px, `rounded-md` = 6px, `rounded-lg` = 8px. Use `rounded-md` for controls and small repeated items; use `rounded-lg` for panels, cards, dialogs, and major surfaces.
- Avoid radius above 8px for core work surfaces. `rounded-full` is reserved for true circles such as avatars, status dots, and soft decorative glows.
- Radius token drift and oversized-radius classes are checked by `python scripts/axis.py check frontend-style`. Mobile-first quality is review and visual-verification owned because regex gates cannot reliably prove responsive usability.

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
- **Forms**: `react-hook-form` + Zod. Define the Zod schema first (source of truth), infer TypeScript type via `z.infer` from the schema or schema factory. Do not hand-author `*FormValues` interfaces/types in schema files; `python scripts/axis.py check frontend-quality` enforces this.
- **Three async states required**: every data-fetching component handles loading (skeleton/spinner), empty (descriptive message), and error (message + retry). Silent empty render is a bug.

---

## Localization and theme preferences

- User-facing text in migrated SPA screens comes from `frontend/src/features/preferences/i18n-resources.ts`. Do not add new hard-coded labels, helper text, empty states, or client-generated error messages in components.
- Locale keys use `feature.section.key` naming and must have both `en` and `vi` entries. English is the fallback language.
- Localization is product copy, not word-by-word translation. Vietnamese copy must read naturally in context, stay concise, and guide the user to the next action. Avoid literal English structure and avoid internal implementation terms in user-facing text.
- Keep product terminology consistent in Vietnamese: `control plane` → "trung tâm vận hành", `data model` → "mô hình dữ liệu", `workflow` → "quy trình", `execution` → "lượt chạy", `form` → "biểu mẫu". Avoid exposing backend terms such as "workspace", "provisioning", "token", or "callback" unless the user genuinely needs that detail.
- Preference state is client-only and lives in Zustand. Persist only non-sensitive preferences (`axis.language`, `axis.theme`) in `localStorage`; never store auth tokens there.
- Theme mode supports `light`, `dark`, and `system`. `system` resolves through `prefers-color-scheme` and should update when the OS preference changes.
- Apply the initial theme before React mounts to avoid visible flash. Keep the inline bootstrap script in `frontend/index.html` in sync with the theme storage key.
- Preference controls should stay compact and action-oriented. Do not add explanatory panels just to advertise language or theme features.
- When adding a new frontend feature screen, add translations and test at least one user-facing path in the default English locale; add targeted tests for preference behavior when the screen owns new preference logic.
- Frontend test setup must await or return async preference/localization work. Do not use fire-and-forget `void` calls in frontend tests; `python scripts/axis.py check frontend-quality` enforces this generic async boundary.

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

- **Component-first design is non-negotiable**: every visible UI pattern starts as the smallest reusable component, then larger components compose those pieces. Do not copy visual geometry between screens.
- New visual rules start in the design system. Feature screens should consume existing tokens and shared components, or add the missing token/component before using the pattern.
- Route files are routing boundaries only. They import and render page components; they must not contain styled layout markup, Tailwind-heavy JSX, or screen design details.
- Shared patterns such as timelines, flow traces, panels, fields, buttons, badges, and status markers live in shared or feature components. If two screens need the same visual behaviour, extract the component before the second implementation lands.
- Standard controls must come from the shadcn/ui primitive layer in `frontend/src/components/ui/`. Feature code must not render native `<button>`, `<input>`, `<label>`, `<select>`, or `<textarea>` directly. If a needed primitive does not exist, add or adapt it in `src/components/ui/` first, then compose it from feature components.
- Custom components are allowed when they represent a domain-specific or cross-screen pattern, but they should compose shared primitives instead of recreating standard controls. A custom component that only restyles a button, field, or label belongs in `src/components/ui/`, not in a feature folder.
- Command buttons with visible text must include an icon child. Icon-only buttons and segmented/toggle controls are exempt because their symbol or selected state already carries the interaction affordance.
- Never rebuild connector/timeline geometry inline. Use `FlowTrace` for vertical flow/timeline displays so marker, connector spacing, state colour, and accessibility remain consistent.
- The public/auth access path pattern uses `AccessPathTrace`; do not redefine its step list, icons, or labels in page-specific components.
- Navigation CTAs on public/auth surfaces use `ActionLink`. Do not hand-code `Link` buttons with Tailwind classes; the shared component keeps icon presence, spacing, height, hover states, and accessible icon treatment consistent. Plain inline text links are still fine for secondary footer/help copy.
- `python scripts/axis.py check frontend-component-composition` enforces route composition, shared shadcn/ui primitive usage for standard controls, text-button icon consistency, detects duplicated flow/timeline geometry outside `FlowTrace`, and rejects duplicated access-path trace definitions outside `AccessPathTrace`.
- High-level visual consistency is not a single regex rule. Enforce concrete, repeatable pieces through shared components and gates; handle broader judgement with mobile/desktop visual review.
- **Single-purpose**: if a component both fetches server data AND contains complex conditional rendering, extract the data-fetching into a custom hook.
- **Composition over prop drilling**: use compound components or context for UI that shares state across more than two levels. Avoid prop chains longer than 2 hops.
- When props exceed 5 with no clear grouping, reconsider whether the component is doing too much — it likely needs to be split.
- Extract non-trivial logic into hooks. A component that is hard to test in isolation is a signal it carries too many responsibilities.

---

## Styling

- **Tailwind only** — no inline `style` prop.
- **`cn()` for conditional classes** — never string concatenation.
- Do not mix Tailwind utility classes and custom CSS on the same element.
- Use Tailwind opacity modifiers from the standard scale only: `/0`, `/5`, `/10`, ..., `/100` and `opacity-0`, `opacity-5`, ..., `opacity-100`. If a non-scale value is truly needed, use bracket syntax such as `/[0.28]`; bare values like `/28` or `opacity-58` can compile to no CSS and hide UI.
- `python scripts/axis.py check frontend-style` is the shared style gate. It currently enforces radius tokens and Tailwind opacity modifier syntax; add future objective style checks there instead of creating narrowly named gates.

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
- Never pass inline object / array / function literals as props — they are new references each render and bypass `memo` (worst in deeply nested builder components). Extract to `useMemo` or module-level constants.
- Keep store selectors granular: subscribe to the smallest slice of Zustand state needed. Coarse selectors cause the entire canvas to rerender on unrelated state changes.
- Memoize expensive derived structures (filtered node lists, computed edge maps, layout calculations) with `useMemo`. Recomputing them on every render compounds quickly in graph UIs.
- Profile with React DevTools before assuming performance is acceptable. Canvas/builder UIs hide rerender storms until data volume increases.

---

## Accessibility baseline

- Every form input must have a `<label>` or `aria-label`.
- Every icon-only button must have `aria-label`.
- Never use colour as the sole indicator — error/warning states require text or icon alongside colour.
