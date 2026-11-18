# D162. Each entity's records open on one record page in each site

Status: **Agreed**

## Context

Every reference should open the record it names, as [opening things](../reference/actions-and-controls.md#opening-things)
says. A record opens as a page with its own address, so a link can be shared and reloaded. A record can sit on several
pages of a site, or on none, so the SPA needs one address for each entity in each site, and the site must say which
page that is.

## Decision

- **A site names the record page of each entity** _(planned)_. A site's `recordPages` maps an entity to a page of its
  application that opens a record of that entity at `/{site}/{page}/{id}`, through a form or a detail. The compiler
  checks it, as [sites](../reference/configuration.md#sites) says. A detail named as a record page takes its record
  from that `id` segment, as [routing](../reference/frontend.md#routing) says.
- **An entity with no named page opens on the default record page** _(planned)_. Its address is
  `/{site}/records/{entity}/{id}`, and it shows the entity's form over every field, as [routing](../reference/frontend.md#routing)
  says.
- **The site lists the record page of each entity the person may read** _(planned)_. `GET /api/sites/{path}` gives
  `recordPages` for those entities only, as [site endpoints](../reference/frontend.md#site-endpoints) says, so the SPA
  links only what the person can open.
- **A record page reads under the person's grants** _(planned)_. It reads the record through the record API, so it
  opens no data the person could not read before, as [D12](d12-authorization-is-server-side-and-default-deny.md) says.

## Consequences

- Every reference opens, even one to an entity that no page shows, such as a category.
- Adding a page never moves where a link opens. Only the site's `recordPages` does.
- Two sites may open one entity on different pages.
- The default record page shows every field in order, so an entity needs no page of its own to be read.

## Alternatives

- **Derive the record page from the site's pages.** Adding a page could then move every link, and two pages that show
  one entity would be ambiguous.
- **Open a record in a dialog.** [Opening things](../reference/actions-and-controls.md#opening-things) refuses it,
  because a record opens as a page that can be shared and reloaded.
- **Show no link where no page exists.** That leaves dead text where a person expects a link.
