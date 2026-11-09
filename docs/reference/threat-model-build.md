# Threat model: the way Axis is built

This page holds the threats to the way Axis is built, the **B** scope of the
[threat model](threat-model.md). How its rows are found, rated and checked is in
[threat model method](threat-model-method.md).

## The way Axis is built

The way Axis is built covers NexKit, CI, the AI agents that write to the repository, and the
repository itself. The overview shows how they connect, and each boundary below has its own table.

```mermaid
flowchart LR
    subgraph nexkit [NexKit]
        Runner[NexKit runs the work]
    end
    subgraph agents [AI agents]
        Writer[Agents that write changes]
    end
    subgraph repository [Repository]
        Repo[Git repository on GitHub]
    end
    subgraph ci [CI]
        Checks[Checks and workflows]
    end
    Runner -->|runs| Writer
    Writer -->|commits| Repo
    Runner -->|pull requests| Repo
    Repo -->|triggers| Checks
```

### NexKit and repository

NexKit turns an issue into a pull request, and the rules on `main` decide when it merges.

```mermaid
flowchart LR
    subgraph issues [Issues and comments]
        Text[Issue text, comments and repository files]
    end
    subgraph pipeline [NexKit pipeline]
        Run[NexKit workflow pinned to a release tag]
    end
    subgraph review [Pull request]
        Branch[Branch with a nexkit prefix and its pull request]
    end
    subgraph trunk [Main branch]
        Rules[Rules on main require NexKit checks]
        Main[main]
    end
    Text -->|read by| Run
    Run -->|pushes| Branch
    Branch -->|merges once checks pass| Main
    Rules -->|guard| Main
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| B4 | A dependency, or a new version of one, brings in malicious code. | NexKit and repository | Tampering | High | **Mitigated** | [Dependency updates](../development.md#dependency-updates) pin the NuGet versions and the npm lock files, and take packages only from nuget.org and the npm registry. The CI, CodeQL, Performance, Load test and Docs audit workflows pin their GitHub Actions to commit hashes, NexKit's cache action too. NexKit's workflow pins its pipeline to a release tag in `.github/workflows/nexkit.yml`, not to a hash. CI's dependency scan runs `scripts/scan.sh`, as [D107](../decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) says. |
| B8 | A change merges with no record of its issue, plan or review. | NexKit and repository | Repudiation | Low | **Mitigated** | Each change follows [the life of an issue](../delivery.md#the-life-of-an-issue), from plan through implementation and review to merge. |
| B9 | Runaway agent runs use up the runners or the usage limit. | NexKit and repository | Denial of service | Low | **Mitigated** | Autopilot's `max_parallel` caps how many issues run at once, and a second Claude subscription takes over at the usage limit, see [delivery decisions](../delivery.md#decisions). |

Spoofing does not apply to the pipeline's own steps, which act as NexKit's GitHub App with a token that lasts an hour. The secrets the pipeline reads are rated in B6.

### CI and secrets

The check workflows run with read access and no secret, and the docs audit gives its Claude token only to a job that reads. NexKit's own jobs hold the write access and the secrets.

```mermaid
flowchart LR
    subgraph pushes [Pushes]
        Push[A hand-made pull request, or NexKit's start on main]
    end
    subgraph checks [Check workflows]
        Ci[CI, CodeQL, Performance and Docs audit with read access]
    end
    subgraph nexkitjobs [NexKit jobs]
        Jobs[NexKit jobs with write access]
        Secrets[Secrets that the config names for each job]
    end
    Push -->|runs| Ci
    Ci -->|a finished run starts a sweep| Jobs
    Jobs -->|reads only| Secrets
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| B3 | A workflow token pushes to `main` without the checks. | CI and secrets | Elevation of privilege | High | **Mitigated** | CI, CodeQL, Performance and Docs audit run with `contents: read`, and only the docs audit's job with no model may open issues. The [rules on main](../delivery.md#decisions) require NexKit's checks, and NexKit's App is not on their bypass list. Only NexKit's jobs that write mint the App's token, and none of them runs an agent or the repository's code. |
| B6 | A secret, such as an agent's subscription token, leaks into a log or reaches a job that does not need it. | CI and secrets | Information disclosure | High | **Mitigated** | The CI, CodeQL, Performance and Load test workflows use no secret. The Docs audit workflow passes the Claude token only to its audit job, whose checkout keeps no GitHub token. `.github/workflows/nexkit.yml` says the NexKit pipeline passes each job only the secrets that `.nexkit/config.json` names for it, so the App's key reaches only the jobs that write, and the agent jobs get only their agents' secrets. |

Spoofing does not apply to a workflow run, which signs in no one and acts through its own token. Denial of service on the runners is rated in B9.

### Agents

The agents read the issue and the repository and write commits. They cannot change the protected paths.

```mermaid
flowchart LR
    subgraph sources [Issues, comments and files]
        Text[Issue text, comments and repository files]
    end
    subgraph agents [Agents]
        Agent[Plan, implement, fix and review agents]
    end
    subgraph limits [Limits]
        Checks[NexKit checks and the AI review]
        Protected[Protected paths for workflows and NexKit settings]
    end
    Text -->|read by| Agent
    Agent -->|commits and runs checks| Checks
    Agent -.->|cannot change| Protected
```

| Id | Threat | Boundary | Category | Rating | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| B1 | An agent adds an exception to a security check, or loosens one, to turn a red check green, such as in `scripts/scan-accepted.json` or `scripts/check-docs.mjs`. | Agents | Tampering | High | **Mitigated** | The owner guards the security checks, their exceptions and the authentication default with `.github/CODEOWNERS`: a pull request that changes one of those files waits for the owner's review. An item of NexKit's review checklist refuses a change that adds an exception to any other check or loosens one, see [delivery decisions](../delivery.md#decisions). The raw HTML and prototype pollution rules are oxlint rules, and their only exceptions are `overrides` entries in `web/.oxlintrc.json`. `scripts/lint.sh` refuses a disable comment that names one. |
| B2 | Text in an issue, a comment or a repository file steers an agent. This is prompt injection. | Agents | Tampering | High | **Mitigated** | Autopilot takes only the issues that [how issues run](../delivery.md#how-issues-run) trusts, and an edit by anyone else ends that trust. The [delivery decisions](../delivery.md#decisions) limit what an agent can change: protected paths, no GitHub token that writes in the jobs that run an agent, and NexKit's checks and review on every pull request, also on hand-made ones. These limit the damage. They do not stop the steering. |
| B5 | The AI review shares the writer's blind spots, because the same kind of model writes and reviews the code. | Agents | Tampering | Medium | **Accepted** | Accepted on 2026-10-29. The owner keeps NexKit's AI review as the only gate before a merge, with the checks and code owners of [D137](../decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md) around it. |
| B7 | An agent turns off or narrows a CI job or a CodeQL check. | Agents | Tampering | High | **Mitigated** | `.github/` and `.nexkit/` are protected paths, see [delivery decisions](../delivery.md#decisions). A change to them is a hand-made pull request, opened by the hourly driver or the local agent session. The owner reviews and merges each one. |

Spoofing does not apply to an agent, which holds no GitHub token and acts only through NexKit's App. Denial of service on the runners is rated in B9.
