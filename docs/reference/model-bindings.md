# Model bindings and the authoring eval

The agent host calls a model through an adapter, by a binding the installation names. An authoring
eval decides which models Axis supports. [D119](../decisions/d119-ai-authoring-in-studio.md) agrees
the idea, and [D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) the
shapes below. Everything here is _(planned for M35)_. The conventions of
[architecture.md](../architecture.md) apply.

## Bindings

- **Two kinds.** `anthropic`, for Claude through the Anthropic API, Amazon Bedrock or Google Vertex
  AI, and `openai-compatible`, for any endpoint that speaks that API, such as OpenAI, Azure OpenAI,
  Gemini, DeepSeek, Qwen, Mistral, or a model the installation serves itself through vLLM or Ollama.
- **No provider is built in.** The request loop, the tools and the checks talk to an adapter. A new
  provider is a new adapter, and nothing else changes.
- **The agent host's settings:**

| Setting | Meaning |
| --- | --- |
| `Ai:Bindings:<name>:Kind` | `anthropic` or `openai-compatible` |
| `Ai:Bindings:<name>:Endpoint` | The API's base URL. For `anthropic`, empty for the Anthropic API, or the Bedrock or Vertex AI endpoint |
| `Ai:Bindings:<name>:Model` | The model id, such as `claude-opus-5-5` |
| `Ai:Bindings:<name>:KeyFile` | A file that holds the API key, as other secrets are files. Empty for a self-served model with no key |
| `Ai:Bindings:<name>:MaxOutputTokens` | The largest output of one call, 16,000 by default |
| `Ai:DefaultBinding` | The binding requests use. With no value, AI authoring is off and Studio shows no request panel |
| `Ai:ReviewBinding` | The binding of the review session. Empty uses the default binding |
| `Ai:AllowTenantKeys` | Whether a tenant may bring its own key, `false` by default |
| `Ai:FixRounds` | Fix rounds per request, 3 by default, see [budgets](authoring-requests.md#budgets) |
| `Ai:RequestTokenBudget` | Tokens per request, 2,000,000 by default |
| `Ai:RequestTimeout` | Time per request after the plan's approval, 30 minutes by default |

- **Bad values.** An unknown kind, a missing model, a key file that cannot be read, or a default
  binding that names no binding stops the agent host's start and names the setting, never a value.
- **Secrets.** A key is read from its file at start, kept in memory only, and never logged, sent to
  a tool or stored in a request.
- **Axis SaaS** uses an `anthropic` binding with Claude by default, as D119 agrees. That is a setting
  of the cell, not a rule of the platform.

## A tenant's own key

- Where `Ai:AllowTenantKeys` is on, a tenant administrator gives the tenant its own key for the
  default binding's provider, on the users page. It is kept as a tenant secret, encrypted as other
  tenant secrets are, and replaces the installation's key for that tenant's requests.
- A tenant with its own key still counts toward `authoringRequests`, but its tokens are recorded as
  its own in [usage](tenant-quotas.md#usage), so a plan of M34 can price them apart.
- Setting, changing and removing the key write platform audit records with no key value.

## The authoring eval

- **Cases.** `tests/ai-eval/cases/` holds one JSON file per case: a starting application, from a
  sample, a template or a built-in application, the request text, and the expected outcome.
  - An outcome of the kind `change` names scenarios that must pass on the accepted draft, and may
    name resources that must not change.
  - An outcome of the kind `refusal` names a contract point that must stop the change, such as a
    requester who decides their own request. The case passes when the request ends with no accepted
    draft that breaks the point.
- **Running.** `scripts/ai-eval.sh --binding <name>` starts the stack, creates a tenant for each
  case, sends the request through the agent host and the MCP server as Studio does, approves the
  plan, and checks the outcome. It writes a report of each case, its tokens and its time to
  `artifacts/ai-eval/`.
- **Pass bar.** A model is supported when it passes every `refusal` case and at least 90% of the
  `change` cases. `tests/ai-eval/supported-models.json` lists the supported models with the date and
  the Axis version of their last pass.
- **When it runs.** For every supported model before each Axis release, by hand or by a workflow
  that holds the providers' keys. An operator runs it against their own binding the same way. The
  SaaS changes its default model only on the eval's numbers.
- **Not a CI check.** It calls paid models and takes minutes per case, so it is not one of the
  checks of every pull request.
