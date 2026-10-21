# D6. In-configuration logic uses a typed expression language

Status: **Agreed**

Validation, computed fields, conditions, routing and policy filters use a small,
typed, side-effect-free expression language that Axis interprets.

- **Type checking:** expressions are checked when the release is compiled.
- **Cost:** evaluation has bounded cost.
- **Reuse:** named rules let an application reuse an expression.
- **No inline code:** no script, SQL or general-purpose code can be embedded
  in configuration.

_Why:_ see [knowledge](../domain/knowledge.md#logic-in-configuration).
