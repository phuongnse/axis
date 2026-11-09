// Axis's lint rules, run by oxlint through `jsPlugins`. The rules for the SPA's source are on in
// web/.oxlintrc.json. The rule for the end-to-end tests, exact-locators, is on in tests/e2e/.oxlintrc.json.
//
// They are written against ESLint's rule API only, so ESLint can run them if oxlint's JS plugins change.
// Each rule reads the syntax tree, so a comment or a string that names a forbidden call is not reported.

const presetColours =
  /^(?:(?:magenta|red|volcano|orange|gold|lime|green|cyan|blue|geekblue|purple|pink|yellow)(?:-inverse)?|success|processing|error|warning|default)$/

const isString = (node) => node?.type === 'Literal' && typeof node.value === 'string'
const isLiteralKey = (node) =>
  node?.type === 'Literal' && (typeof node.value === 'string' || typeof node.value === 'number')
const propertyName = (node) => (node?.type === 'Identifier' && !node.computed ? node.name : undefined)

// Whether the expression ends in `.prototype`, as `Foo.prototype` or `a.b.prototype`.
function endsInPrototype(node) {
  return node?.type === 'MemberExpression' && !node.computed && propertyName(node.property) === 'prototype'
}

// Whether a member chain passes through `.prototype`, as in `Foo.prototype.bar` or `Foo.prototype[name]`.
function passesThroughPrototype(node) {
  for (let current = node; current?.type === 'MemberExpression'; current = current.object) {
    if (endsInPrototype(current)) return true
  }
  return false
}

const noSrcdoc = {
  meta: {
    type: 'problem',
    messages: { srcdoc: 'An iframe srcdoc writes raw HTML. Render the value as text.' },
    schema: [],
  },
  create(context) {
    return {
      JSXAttribute(node) {
        if (node.name?.type === 'JSXIdentifier' && (node.name.name === 'srcDoc' || node.name.name === 'srcdoc')) {
          context.report({ node, messageId: 'srcdoc' })
        }
      },
    }
  },
}

const noComputedKeyWrite = {
  meta: {
    type: 'problem',
    messages: {
      computedKey:
        'A write to a computed key is open to prototype pollution. Use setOwn from web/src/platform/ownProperty.ts or a Map.',
    },
    schema: [],
  },
  create(context) {
    return {
      AssignmentExpression(node) {
        const target = node.left
        if (target.type !== 'MemberExpression' || !target.computed || isLiteralKey(target.property)) return
        // A write into a prototype is reported by no-prototype-write.
        if (endsInPrototype(target.object)) return
        context.report({ node, messageId: 'computedKey' })
      },
    }
  },
}

const noPrototypeWrite = {
  meta: {
    type: 'problem',
    messages: {
      write: 'A write into a prototype changes every object that uses it.',
      define: 'defineProperty on a prototype changes every object that uses it.',
      proto: 'A __proto__ key in an object literal sets its prototype.',
    },
    schema: [],
  },
  create(context) {
    return {
      AssignmentExpression(node) {
        if (passesThroughPrototype(node.left)) context.report({ node, messageId: 'write' })
      },
      CallExpression(node) {
        const callee = node.callee
        if (callee.type !== 'MemberExpression' || callee.computed) return
        const owner = callee.object.type === 'Identifier' ? callee.object.name : undefined
        const name = propertyName(callee.property)
        const defines =
          (owner === 'Object' && (name === 'defineProperty' || name === 'defineProperties')) ||
          (owner === 'Reflect' && name === 'defineProperty')
        if (defines && endsInPrototype(node.arguments[0])) context.report({ node, messageId: 'define' })
      },
      ObjectExpression(node) {
        for (const property of node.properties) {
          if (property.type !== 'Property' || property.computed || property.shorthand) continue
          const key = property.key
          if (
            (key.type === 'Identifier' && key.name === '__proto__') ||
            (key.type === 'Literal' && key.value === '__proto__')
          ) {
            context.report({ node: property, messageId: 'proto' })
          }
        }
      },
    }
  },
}

// A string literal preset anywhere inside the value, or the value itself.
function holdsPreset(node) {
  if (isString(node)) return presetColours.test(node.value)
  if (node?.type === 'TemplateLiteral') {
    return (
      node.expressions.length === 0 && node.quasis.length === 1 && presetColours.test(node.quasis[0].value.cooked ?? '')
    )
  }
  if (!node || typeof node !== 'object') return false
  return Object.entries(node).some(([key, child]) => {
    if (key === 'parent' || key === 'loc' || key === 'range') return false
    if (Array.isArray(child)) return child.some((item) => item && typeof item.type === 'string' && holdsPreset(item))
    return child && typeof child.type === 'string' && holdsPreset(child)
  })
}

const noPresetTagColour = {
  meta: {
    type: 'problem',
    messages: { preset: 'Colour a tag with the colour tokens, not a preset name.' },
    schema: [],
  },
  create(context) {
    return {
      JSXOpeningElement(node) {
        if (node.name?.type !== 'JSXIdentifier' || node.name.name !== 'Tag') return
        for (const attribute of node.attributes) {
          if (attribute.type !== 'JSXAttribute' || attribute.name?.name !== 'color' || !attribute.value) continue
          const value = attribute.value
          const preset =
            value.type === 'JSXExpressionContainer'
              ? holdsPreset(value.expression)
              : isString(value) && presetColours.test(value.value)
          if (preset) context.report({ node, messageId: 'preset' })
        }
      },
    }
  },
}

const noAntdComponent = (component, message) => ({
  meta: {
    type: 'problem',
    messages: { component: message },
    schema: [],
  },
  create(context) {
    const names = new Set()
    return {
      ImportDeclaration(node) {
        if (node.source.value !== 'antd') return
        for (const specifier of node.specifiers) {
          if (
            specifier.type === 'ImportSpecifier' &&
            (specifier.imported.name ?? specifier.imported.value) === component
          ) {
            names.add(specifier.local.name)
          }
        }
      },
      JSXOpeningElement(node) {
        if (node.name?.type === 'JSXIdentifier' && names.has(node.name.name))
          context.report({ node, messageId: 'component' })
      },
    }
  },
})

const noAntdButton = noAntdComponent('Button', 'Use AxisButton with a role, not the Button of antd.')
const noAntdAlert = noAntdComponent('Alert', 'Use AxisAlert with a kind, not the Alert of antd.')

const locatorMethods = new Set(['getByRole', 'getByLabel', 'getByText', 'getByTitle'])
const wrappers = new Set(['TSAsExpression', 'TSSatisfiesExpression', 'TSNonNullExpression', 'ParenthesizedExpression'])
const scopes = new Set(['BlockStatement', 'StaticBlock', 'Program'])
const maxHops = 5

// Follows a `const` variable to the value it holds, through enclosing blocks, and returns any other node as it is.
function resolve(node, hops = 0) {
  let current = node
  while (current && wrappers.has(current.type)) current = current.expression
  if (current?.type !== 'Identifier' || hops >= maxHops) return current
  for (let scope = current.parent; scope; scope = scope.parent) {
    if (!scopes.has(scope.type)) continue
    for (const statement of scope.body) {
      if (statement.type !== 'VariableDeclaration' || statement.kind !== 'const') continue
      const declarator = statement.declarations.find(
        (item) => item.id?.type === 'Identifier' && item.id.name === current.name && item.init,
      )
      if (declarator) return resolve(declarator.init, hops + 1)
    }
  }
  return current
}

// A regular expression, new RegExp(...) or fieldLabel(...), which state on purpose how much they match.
function isPattern(node) {
  const value = resolve(node)
  if (value?.type === 'Literal') return Boolean(value.regex)
  if ((value?.type === 'NewExpression' || value?.type === 'CallExpression') && value.callee?.type === 'Identifier') {
    return value.callee.name === 'RegExp' || (value.type === 'CallExpression' && value.callee.name === 'fieldLabel')
  }
  return false
}

const keyIs = (property, name) =>
  property.type === 'Property' &&
  !property.computed &&
  (property.key?.name === name || (property.key?.type === 'Literal' && property.key.value === name))

// The last property of an object literal with the key, so that an earlier duplicate does not count.
const lastProperty = (object, name) => object.properties.findLast((property) => keyIs(property, name))

const exactIsTrue = (options) => {
  const object = resolve(options)
  if (object?.type !== 'ObjectExpression') return false
  const property = lastProperty(object, 'exact')
  const after = property ? object.properties.slice(object.properties.indexOf(property) + 1) : []
  return (
    property !== undefined &&
    !after.some((item) => item.type === 'SpreadElement') &&
    property.value.type === 'Literal' &&
    property.value.value === true
  )
}

const isDialogName = (node) =>
  (node?.type === 'Literal' && node.value === 'dialog') ||
  (node?.type === 'TemplateLiteral' && node.expressions.length === 0 && node.quasis[0]?.value.cooked === 'dialog')

// A Playwright locator that takes a string name or text matches it exactly, see docs/testing.md#rules-for-tests.
const exactLocators = {
  meta: {
    type: 'problem',
    messages: {
      looseName:
        'A string name without exact: true can match the wrong element. Add exact: true or use a regular expression.',
      hiddenOptions: 'The rule cannot see the options of this locator. Write them as an object literal or a const.',
      dialog: 'Reach a dialog through waitForDialog or confirmDialog in tests/e2e/dialogs.ts.',
    },
    schema: [{ type: 'object', properties: { allowDialogLookup: { type: 'boolean' } }, additionalProperties: false }],
  },
  create(context) {
    const allowDialogLookup = context.options[0]?.allowDialogLookup === true
    return {
      CallExpression(node) {
        const callee = node.callee
        const method =
          callee.type === 'Identifier'
            ? callee.name
            : callee.type === 'MemberExpression' && !callee.computed
              ? propertyName(callee.property)
              : undefined
        if (!method || !locatorMethods.has(method)) return
        const [first, second] = node.arguments
        // A chained call reports on the method, so the line is the one that names it.
        const target = callee.type === 'MemberExpression' ? callee.property : callee

        if (method !== 'getByRole') {
          if (first && !isPattern(first) && !exactIsTrue(second))
            context.report({ node: target, messageId: 'looseName' })
          return
        }

        if (!allowDialogLookup && isDialogName(resolve(first))) context.report({ node: target, messageId: 'dialog' })
        if (!second) return
        const options = resolve(second)
        if (options?.type !== 'ObjectExpression') {
          context.report({ node: target, messageId: 'hiddenOptions' })
          return
        }
        const name = lastProperty(options, 'name')
        const spreadAfterName = options.properties
          .slice(name ? options.properties.indexOf(name) + 1 : 0)
          .some((item) => item.type === 'SpreadElement')
        if (spreadAfterName) context.report({ node: target, messageId: 'hiddenOptions' })
        else if (name && !isPattern(name.value) && !exactIsTrue(options))
          context.report({ node: target, messageId: 'looseName' })
      },
    }
  },
}

export default {
  meta: { name: 'axis' },
  rules: {
    'no-srcdoc': noSrcdoc,
    'no-computed-key-write': noComputedKeyWrite,
    'no-prototype-write': noPrototypeWrite,
    'no-preset-tag-colour': noPresetTagColour,
    'no-antd-button': noAntdButton,
    'no-antd-alert': noAntdAlert,
    'exact-locators': exactLocators,
  },
}
