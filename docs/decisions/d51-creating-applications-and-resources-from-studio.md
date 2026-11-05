# D51. Creating applications and resources from Studio

Status: **Agreed**

## Context

An author who cannot start an application or a resource without writing JSON by hand cannot use
Studio alone. [D38](d38-studio-edits-application-folders-through-an-authoring-api.md) lets Studio
edit only the folders that the installation lists, which are kept in git, and a fixed list cannot
grow when an author starts something new.

## Decision

- **New applications go in a folder of their own.** `Studio:ApplicationsPath` names one folder, kept
  out of git, where Studio creates application folders. `Studio:Folders` stays for the folders kept
  in git.
- **An application starts blank or from a template.** Blank is the smallest application that
  compiles with no error. A template is an application folder with a `template.json` manifest, which
  the operator installs per installation, as modules are installed.
- **A copy gets new resource ids.** Every resource of a copied template gets a new `id`, so two
  applications made from one template never clash in one tenant. References are by name, so they
  still resolve.
- **A new folder appears whole.** It is written under a hidden name and then renamed, so a reader
  never sees half an application. Creating an application never activates it: the author publishes
  it, as [D47](d47-publishing-from-studio.md) says.
- **A new resource starts from its kind's starter.** A starter is the smallest content that passes
  the kind's schema. It lives in the SPA, next to the schemas the SPA bundles, and the server's
  create route stays as it is. A reference the author must still choose is left empty, so it shows
  as a diagnostic.

The detail is in [creating an application](../reference/authoring.md#creating-an-application),
[application templates](../reference/modules-and-templates.md#application-templates), the
[Studio settings](../reference/host-settings.md#studio-settings) and
[Studio](../reference/studio.md).

## Consequences

- An installation decides which templates its authors can start from, and an author cannot share an
  application as a template.
- A schema change must change the starters too. Tests check each starter against its schema.

## Alternatives

- **Create applications in the folders of `Studio:Folders`.** It would mix folders kept in git with
  folders that Studio makes, and the list is fixed per installation.
- **Templates that authors save or upload.** Installing templates as modules are installed reuses a
  rule the operator knows.
