# D147. A run downloads only what the caches miss, and a transient stop runs again after a wait

Status: **Agreed**

## Context

Every NexKit round and every check job starts on a fresh runner. Setup and the Docker suites download
from many outside services: NuGet, npm, the Ubuntu package mirror, Playwright's browser host, GitHub
releases, Helm's site and the container registries. When one of them fails, the job fails for a cause
outside Axis. Setup tries each step again within a minute, and NexKit runs a stopped command once
more at once, but an outage lasts from minutes to hours, so both tries fall inside it and the issue
waits for the owner. A download without a time limit also hangs until NexKit stops setup at its own
limit, so the one more try starts late.

## Decision

- **A run downloads only what the caches miss.** Every package, browser, tool, schema and container
  image that setup and the checks download lands in a folder that a NexKit cache entry names, so CI on
  `main` saves it and every NexKit job restores it. With current caches, setup and the checks reach no
  outside service. A new download comes with its cache entry, and the entry's key files are the files
  that pin what it holds, as [delivery](../delivery.md#nexkits-checks) says.
- **Setup installs no system packages on GitHub's runners.** The runner image has the libraries that
  Chromium needs, and the packages Playwright would add are fonts that no test reads.
- **Every outside download has a time limit,** so a service that does not answer fails its step in
  minutes, not at NexKit's limit for setup.
- **NexKit runs a command that stopped on a transient error again after growing waits,** long
  enough in all to outlast a usual outage of an outside service, before it asks for a person. Other
  issues run meanwhile. The waits are a NexKit setting, which the owner tunes in `.nexkit/config.json`
  alone, as
  [D145](d145-docs-stay-true-and-an-audit-reads-them-every-few-merges.md) agrees for every NexKit
  setting.

## Consequences

- An outage of an outside service delays a round, and asks for a person only when it outlasts the
  waits.
- The first runs after a lock file or a pin changes still download, until CI on `main` saves the new
  cache. GitHub drops a cache that nobody used for 7 days, so after a quiet week the first runs
  download again.
- The caches take more of the repository's cache storage, and the container images most of it.
- A transient stop holds its issue for as long as the waits last, where a person could have run it
  again sooner by hand.

## Alternatives

- **A prebuilt runner image** with the toolchains, browsers and tools. It removes most downloads at
  once, but NexKit's jobs would have to run in a container, the Docker suites would need Docker inside
  it, and the image is one more thing to build, scan and update.
- **More tries inside setup.** They cannot outlast an outage of an hour, and they spend NexKit's limit
  for setup.
- **A mirror or proxy for each registry.** It is a service to run and pay for, and one more outside
  service that can fail.
