# Frontend conventions

The React/Vite application follows a feature-oriented architecture adapted from
[Bulletproof React](https://github.com/alan2207/bulletproof-react/blob/master/AGENTS.md).

## Commands

Run commands from `src/frontend`:

- `npm run lint`
- `npm run format:check`
- `npm run build`

From the repository root, `mise run test` runs the required frontend checks and
the .NET suite. `mise run format-check` is also mandatory before completion.

## Structure and dependency direction

```text
src/
  app/          # Router, application providers and app-wide composition
  api/          # Typed HTTP client, requests and transport/domain interfaces
  components/   # Shared, feature-agnostic UI primitives
  features/     # Self-contained product capabilities
  hooks/        # Shared hooks only
  i18n/         # Locales and translation infrastructure
  state/        # Small persisted UI preferences
```

Feature code is colocated in `features/<feature>/{components,hooks,utils}`.
Shared code must not import a feature. Features must not import another feature;
compose them from `app/` instead. Keep state local unless multiple routes need
it, then place the provider in `app/`.

## Components and state

- Prefer composition and `children` over prop flags or "god" components.
- One component has one responsibility. Pages compose features and shared shell
  components; dialogs and feature controls belong to their feature.
- Use `useState` for simple local state, `useReducer` for coupled transitions.
- Server requests remain in typed `api/requests` functions. Do not add a server
  state dependency until there are multiple consumers or cache/revalidation
  needs.
- Define props/types before implementation. Keep TypeScript strict.

## Project-specific exceptions

- The existing Windows 3.1 mockup is authoritative: do not redesign it without
  the user's approval.
- Do not add automated React component tests unless the user explicitly changes
  the current smoke-test preference.
- Use PascalCase for React component files to match the existing codebase;
  use camelCase for hooks and utilities.
- Import directly from source files; do not introduce broad barrel exports.
