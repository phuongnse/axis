# D4. Modular monolith

Status: **Agreed**

Axis is a single codebase with explicit module boundaries.

- **M1–M2:** one ASP.NET Core host serves the API, the BFF and the SPA.
- **M3:** a separate worker host is added. It runs the same modules and
  executes background work.
- **Never needed:** a message broker or microservices.

_Why:_ a small team gets the most speed from a monolith. Module boundaries
keep the option to split hosts later.
