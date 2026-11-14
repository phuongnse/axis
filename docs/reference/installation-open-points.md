# Installation open points

This page holds what the [installation](installation.md) page leaves open.

## Open points

The first install differs from an upgrade in one way: the `platform` tables do not exist yet. The
server applies the platform migrations at start, see [platform database](storage.md#platform-database).
`tenant create` does not create the `platform` tables and exits with code 2 without them, so start the
server once first. `migrate` before the `platform` schema exists does nothing, because there is no
tenant to migrate. It writes a note to standard error and exits with code 0, so running it first, as
[the order](installation.md#the-order) does, is safe on a first install.
