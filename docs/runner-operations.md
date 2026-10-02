# Axis runner operations

Use the NexKit release pinned in [.nexkit/project.json](../.nexkit/project.json).
Keep its checkout outside the application repository. Set `NEXKIT_SOURCE` to that
checkout, with its exact accepted commit checked out.

The project selects Codex 0.160.0, GPT-6.1 Sol (`gpt-6.1-sol`) and Max reasoning
(`max`). The local Codex configuration preserves the same model and effort.
Authentication uses the independent ChatGPT login in the dedicated runner.

## Provision and maintain

Use the supplied NexKit image, runtime action and reusable workflows. Workflow
composition and control files must be accepted through an installation proposal
with exact hashes. Credentials, verification receipts and logs stay outside the
source tree.

```sh
python3 "$NEXKIT_SOURCE/scripts/provision_runner.py" \
  --config .nexkit/project.json --pipeline discovery --check-host
python3 "$NEXKIT_SOURCE/scripts/provision_runner.py" \
  --config .nexkit/project.json --pipeline discovery --apply
python3 "$NEXKIT_SOURCE/scripts/manage_runner.py" status \
  --config .nexkit/project.json --pipeline discovery
```

The binding uses the project label `axis-runner`. Provisioning records its
immutable image and policy directory. Reprovisioning requires deliberate cleanup
of the stopped old container, while preserving this consumer's private login.
Never copy credentials into another consumer or run concurrent session refreshers.

Stop the service before completing an official login, then start it again:

```sh
python3 "$NEXKIT_SOURCE/scripts/manage_runner.py" stop \
  --config .nexkit/project.json --pipeline discovery
python3 "$NEXKIT_SOURCE/scripts/manage_runner.py" login \
  --config .nexkit/project.json --pipeline discovery
python3 "$NEXKIT_SOURCE/scripts/manage_runner.py" start \
  --config .nexkit/project.json --pipeline discovery
```

Follow the pinned kit's `docs/self-hosted.md` for private login paths, pre-login
admission checks and the public-repository subscription support boundary.

## Verification

```sh
python3 "$NEXKIT_SOURCE/scripts/verify_docker_host.py" --codex-version 0.160.0
nexkit doctor --online --checks
```

Container checks use fake credentials, actual CLI tools and simulated model
responses. Verify live model access separately with a small clarification request.
A specification needs an authorized collaborator's approval before discovery.
Doctor does not prove model access or completion of the native task workflow.

There is no application implementation yet. Agree architecture and meaningful
application tests and end-to-end checks before configuring application delivery.
Infrastructure checks do not establish application behavior.
