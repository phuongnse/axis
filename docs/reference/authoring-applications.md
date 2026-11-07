# Creating applications in the authoring API

The [authoring API](authoring.md) has one route that creates a whole application folder. It
checks the request in a fixed order, writes the folder in one step and never activates it.

## Creating an application

`POST /api/studio/apps` creates a new application folder in `Studio:ApplicationsPath`, or in a cell in
[the tenant's folder](host-settings.md#studio-settings), blank or from an installed
[application template](modules-and-templates.md#application-templates). The body is
`{ "name": "...", "template": "<template id>" }`, and without the optional `template` the application
is blank.

The route has its own order of checks, and the first failure is the response:

1. The CSRF header `X-Axis-Csrf: 1`, `403`.
   A write in a [read-only tenant](subscriptions.md#the-read-only-tenant) is refused next, before the session.
2. The session, `401`.
3. In a cell, the tenant over its storage quota, `403` with `reason` `quota-exceeded`, see
   [refusals](tenant-quotas.md#refusals).
4. The author role, `403`.
5. `Studio:ApplicationsPath` not set, `403`, titled "Studio cannot create applications here."
6. In a cell, the tenant at its applications quota, `403` with `reason` `quota-exceeded`.
7. A body that is not `application/json`, `415`.
8. A bad body, `400`. That is:
   - a body that is not a JSON object, or that has a key other than `name` and `template`;
   - a `name` that does not match `^[A-Za-z][A-Za-z0-9]*$` or is longer than 60 characters;
   - a `name` whose [kebab-case](authoring.md#new-files) form is longer than 60 characters, or is a reserved site
     path: `api`, `health`, `assets`, `studio`, `account`, `users` or `licences`;
   - a `template` that is not a string.
9. An unknown template, `404`: an `id` that no installed template has.
10. A write lock that cannot be taken, `503`, see [Versions](authoring.md#versions).
11. A name already used, ignoring letter case, by an application that Studio lists, or a target folder
    that already exists, `409`.

What it writes:

- **Atomic.** The whole folder is written to a hidden temporary folder, `.<kebab>-<random>`, inside
  `Studio:ApplicationsPath`, and then renamed to the kebab-case name, so a reader never sees half an
  application. The write lock is the folder write lock of [Versions](authoring.md#versions), keyed by
  `Studio:ApplicationsPath`. A temporary folder starts with `.`, so the application list skips it.
- **Blank.** Without a `template`, the folder holds the smallest application that compiles with no
  error, see [blank application](modules-and-templates.md#blank-application).
- **From a template.** With a `template`, the server uses the newest installed version, copies its
  files and changes the ids, the name and the site path, see
  [application templates](modules-and-templates.md#application-templates). The answer has the
  template's diagnostics, so a template that does not compile gives a copy that does not compile.
- **Never an activation.** As for every write, the author [publishes](authoring.md#publishing) the new
  application. The diagnostics in the answer are the folder's, as for [Diagnostics](authoring.md#diagnostics).
