# D51. Creating applications and resources from Studio

Status: **Agreed**

Studio creates a new application, blank or from an installed application
template, and starts a resource of every kind. D46 plans this for M11. The
`Studio:ApplicationsPath` setting, `POST /api/studio/apps`, `studioCreate` and
the Studio New application dialog are built. Application templates and `GET /api/studio/templates`,
New resource with its starters, and the `request-approval` sample template are built too.
Development and Compose setting the folder is built too. See the
[authoring API](../reference/authoring.md#creating-an-application), the
[application templates](../reference/modules-and-templates.md#application-templates) and
the [Studio section of the frontend](../reference/studio.md). The owner
confirmed these points on issue #778 on 2026-10-20.

- **Where new applications go:**
  - A new server setting, `Studio:ApplicationsPath`, names one folder. Studio
    creates new application folders in it.
  - Every direct subfolder of it that holds an `application.json` is listed by
    `GET /api/studio/apps`, after the folders of `Studio:Folders`. The same
    first-match rule applies to names.
  - Studio is on when `Studio:Folders` is not empty or `Studio:ApplicationsPath`
    is set. `GET /api/me` answers `studioCreate: true` when the user holds the
    author role and the setting is set.
  - A missing folder is created at start. The server stops if it cannot.
  - Under several server replicas, the folder is on the shared Studio folder
    ([D49](d49-several-server-replicas-behind-a-tls-proxy.md)).
  - E2E's journey server sets it to a temporary folder, and Development and Compose set it to
    `artifacts/studio-apps`, which git ignores.
- **Creating an application:**
  - `POST /api/studio/apps` takes `{ name, template? }`. It needs the author
    role and the CSRF header.
  - The new folder is the kebab-case `name`. A name or folder that already
    exists is `409`. A `name` that does not match the manifest name pattern is
    `400`. So is a name whose kebab-case form is longer than 60 characters or
    is a reserved site path (`api`, `health`, `assets`, `studio`, `account`, `users`).
  - The whole folder is written under the write lock, to a hidden temporary
    folder that is then renamed. A reader never sees half an application.
  - The answer is `201` with the application's `name`, a `Location` and the
    folder's diagnostics. A new application is not activated. The author
    publishes it ([D47](d47-publishing-from-studio.md)).
- **Blank application:**
  - It holds a manifest with a new UUID `id`, one site at `/<kebab-name>` with
    the default locale `en`, one `Home` page with a `taskInbox` widget, and an
    `en` text resource.
  - The text resource holds three keys: the site title, the page title and the
    navigation label.
  - The site schema needs at least one navigation entry that names a page. So a
    blank folder without a page would not compile.
- **Application templates:**
  - An application template is an application folder with a `template.json`:
    `id`, a SemVer `version`, a title text and a description text, in `en` and
    `vi`.
  - Operators install templates under `Templates:Path/<id>/<version>/`, like
    modules. A missing setting means no templates. A folder named in it that
    does not exist stops the server.
  - A template must hold exactly one site. A template with a bad
    `template.json`, folder names that differ from it, or any other number of
    sites is left out and logged as a warning at start.
  - `GET /api/studio/templates` lists the newest version of each template. The
    create body names a template by `id` only, and the server uses the newest
    installed version.
  - A copy takes every `*.json` file except `template.json`. Each resource's
    top-level `id` becomes a new UUID. Seed record ids stay, because they are
    row ids in the new application's own tables.
  - The manifest gets the new `name`, and the site path becomes the kebab-case
    name. References are by name, so they still resolve.
  - The copy has the same diagnostics as the template. A template that does not
    compile is listed, and its diagnostics come with the create answer.
  - Sample templates live under `samples/templates/`. `scripts/build.sh`
    installs them into `artifacts/templates`. The production images hold none.
    The sample `request-approval` has one entity with a sequence number, a form
    page, a table page, a two-step approval process, two roles and their
    policies, texts in `en` and `vi`, and one test scenario.
  - Templates are installed, not uploaded. An author cannot save an application
    as a template in M11.
- **Creating a resource:**
  - Studio's **New resource** asks for the kind and the name, or the locale for
    a text resource.
  - It sends the kind's starter: the smallest content that passes the kind's
    schema. References the author must still choose are left as empty strings.
    The schema's name patterns reject an empty string, so they show up as
    diagnostics.
  - The starters live in the SPA, next to the schemas it bundles. The create
    route stays as it is. A Vitest test checks each starter against its schema.
  - A new text resource copies every key and text of the default locale of the
    first site, so every label still resolves. The author then translates.
- **Effect on [D38](d38-studio-edits-application-folders-through-an-authoring-api.md):**
  - `Studio:Folders` stays for folders kept in git. New applications live in
    `Studio:ApplicationsPath`, a folder that is kept out of git.

_Why:_ an author who cannot start an application or a resource without
hand-written JSON cannot use Studio alone. A second folder setting keeps the
git-kept folders apart from the ones Studio creates, and a fixed list could not
grow. Writing the folder to a hidden name and renaming it means a reader never
sees half an application. Installing templates like modules reuses a rule the
operator knows, and the cost is that authors cannot share their own in M11.
New ids on a copy mean two applications made from one template share no
resource id, so they never clash in one tenant. Starters in the SPA keep the
server's create route simple. The cost is that a starter broken by a schema
change shows up in the Vitest tests, not in a server check.
