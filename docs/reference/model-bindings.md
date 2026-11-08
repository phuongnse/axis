# Model bindings and the authoring eval

The agent host calls a model through an adapter, by a binding that the installation names, so no
model provider is built into Axis and a closed network can use a model of its own. An authoring eval
decides which models Axis supports: a model earns its place by what it does, whoever makes it. This
page gives the bindings and their settings, a tenant's own key, and the eval. The reasons are in
[D119](../decisions/d119-ai-authoring-in-studio.md).

## Bindings

A binding names a model and how to reach it. Two kinds of binding stand side by side, and any
installation chooses either.

- **`anthropic`**, for Claude through the Anthropic API, or any endpoint that speaks its Messages
  API. The adapter sends the key in an `x-api-key` header.
- **`openai-compatible`**, for any endpoint that speaks that API, such as OpenAI, Azure OpenAI, Gemini,
  DeepSeek, Qwen, Mistral, or a model the installation serves itself through vLLM or Ollama.
- **Amazon Bedrock and Google Vertex AI** _(planned for M35)_ come with their own sign-in, which a key
  file read once does not cover. Until then an `anthropic` binding reaches them only through a gateway
  that speaks the Messages API.
- **Who pays the provider.** On premises the customer pays its model provider. In Axis SaaS the
  plan includes a number of requests, see [tenant quotas](tenant-quotas.md).
- **No provider is built in.** The request loop, the tools and the checks talk to an adapter and do
  not know which model is behind it. A call hands the adapter the system text, the messages, the tool
  definitions and an output limit, and gets back text, tool calls and token counts. The limit is never
  above the binding's `MaxOutputTokens`. A new provider is a new adapter, and nothing else changes.
- **Retries.** When the provider answers that it is rate limited, with status 429 or, for `anthropic`,
  529, the call waits as `Retry-After` asks, at most 60 seconds, or 1, 2 and 4 seconds, and tries
  again. After four attempts, or on any other error, it fails with a message that names the binding
  and the status code, never the response body or a header.
- **Tokens.** Each call reports its input and output tokens and writes one log line with the binding,
  the model and the tokens.
- **Tests.** A scripted adapter replays recorded answers for every binding, for the request loop's
  tests and the journey. The host refuses to register it outside Development and Testing.

The agent host reads these settings:

| Setting                              | Meaning                                                                                                |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| `Ai:Bindings:<name>:Kind`            | `anthropic` or `openai-compatible`                                                                     |
| `Ai:Bindings:<name>:Endpoint`        | The API's base URL. Empty for the provider's own API, which is Anthropic's or OpenAI's by the kind       |
| `Ai:Bindings:<name>:Model`           | The model id, such as `claude-opus-5-5`                                                                |
| `Ai:Bindings:<name>:KeyFile`         | A file that holds the API key, as other secrets are files. Empty for a self-served model with no key   |
| `Ai:Bindings:<name>:MaxOutputTokens` | The largest output of one call, 16,000 by default                                                      |
| `Ai:DefaultBinding`                  | The binding requests use. With no value, AI authoring is off and Studio shows no request panel         |
| `Ai:ReviewBinding`                   | The binding of the review session. Empty uses the default binding                                      |
| `Ai:AllowTenantKeys`                 | Whether a tenant may bring its own key, `false` by default _(planned for M35)_                         |
| `Ai:FixRounds`                       | Fix rounds per request, 3 by default, see [budgets](authoring-requests.md#budgets) _(planned for M35)_ |
| `Ai:RequestTokenBudget`              | Tokens per request, 2,000,000 by default _(planned for M35)_                                           |
| `Ai:RequestTimeout`                  | Time per request after the plan's approval, 30 minutes by default _(planned for M35)_                  |

- **Bad values.** An unknown kind, a missing model, a key file that cannot be read, a
  `MaxOutputTokens` that is not a whole number of at least 1, or a default or review binding that names
  no binding stops the agent host's start and names the setting, never a value.
- **Secrets.** A key is read from its file at start, kept in memory only, and never logged, sent to a
  tool or stored in a request.
- **Axis SaaS** _(planned for M35)_ uses an `anthropic` binding with Claude by default, because it does well at long runs
  of tool calls. That is a setting of the cell, not a rule of the platform. Each SaaS plan includes a
  number of requests, the `authoringRequests` [quota](tenant-quotas.md#quotas), while on premises the
  customer pays its model provider.

## A tenant's own key

_(planned for M35)_

Where `Ai:AllowTenantKeys` is on, a tenant may pay for its own model use.

- **The key.** A tenant administrator gives the tenant its own key for the default binding's provider,
  on the [users page](user-administration.md). It is kept as a tenant secret, encrypted as other tenant
  secrets are, and replaces the installation's key for that tenant's requests.
- **Usage.** A tenant with its own key still counts toward `authoringRequests`, but its tokens are
  recorded as its own in [usage](tenant-quotas.md#usage), so that a plan can price them apart.
- **Audit.** Setting, changing and removing the key write platform audit records with no key value.

## The authoring eval

_(planned for M35)_

The authoring eval is a set of authoring requests with known outcomes, run through the MCP server
against any model.

- **Cases.** `tests/ai-eval/cases/` holds one JSON file per case: a starting application, from a
  sample, a template or a built-in application, the request text, and the expected outcome.
  - An outcome of the kind `change` names scenarios that must pass on the accepted draft, and may name
    resources that must not change.
  - An outcome of the kind `refusal` names a contract point that must stop the change, such as a
    requester who decides their own request. The case passes when the request ends with no accepted
    draft that breaks the point.
- **Running.** `scripts/ai-eval.sh --binding <name>` starts the stack, creates a tenant for each case,
  sends the request through the agent host and the MCP server as Studio does, approves the plan, and
  checks the outcome. It writes a report of each case, its tokens and its time to
  `artifacts/ai-eval/`.
- **Pass bar.** A model is supported when it passes every `refusal` case and at least 90% of the
  `change` cases. `tests/ai-eval/supported-models.json` lists the supported models with the date and
  the Axis version of their last pass.
- **When it runs.** For every supported model before each Axis release, by hand or by a workflow that
  holds the providers' keys. An operator runs it against their own binding the same way. The SaaS
  changes its default model only on the eval's numbers.
- **Not a CI check.** It calls paid models and takes minutes per case, so it is not one of the checks
  of every pull request.
