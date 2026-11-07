// Axis's lint rules for the SPA's source, run by oxlint through `jsPlugins` in web/.oxlintrc.json.
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

const noAntdButton = {
  meta: {
    type: 'problem',
    messages: { button: 'Use AxisButton with a role, not the Button of antd.' },
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
            (specifier.imported.name ?? specifier.imported.value) === 'Button'
          ) {
            names.add(specifier.local.name)
          }
        }
      },
      JSXOpeningElement(node) {
        if (node.name?.type === 'JSXIdentifier' && names.has(node.name.name))
          context.report({ node, messageId: 'button' })
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
  },
}
