import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import ts from 'typescript';

const authoredRoots = ['src', 'tests'];

function normalizePath(filePath) {
  const normalized = filePath.replaceAll('\\', '/');
  return normalized.startsWith('./') ? normalized.slice(2) : normalized;
}

function isAuthoredTypeScript(filePath) {
  const normalized = normalizePath(filePath);
  if (!(normalized.endsWith('.ts') || normalized.endsWith('.tsx'))) {
    return false;
  }
  if (!authoredRoots.some((root) => normalized === root || normalized.startsWith(`${root}/`))) {
    return false;
  }
  return !(
    normalized === 'src/theme.generated.ts' ||
    normalized === 'src/routeTree.gen.ts' ||
    normalized.startsWith('src/lib/api-generated/') ||
    normalized.startsWith('src/components/ui/')
  );
}

function scriptKind(filePath) {
  return filePath.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
}

function isLiteralTextNode(node) {
  return (
    ts.isStringLiteral(node) ||
    ts.isNoSubstitutionTemplateLiteral(node) ||
    node.kind === ts.SyntaxKind.TemplateHead ||
    node.kind === ts.SyntaxKind.TemplateMiddle ||
    node.kind === ts.SyntaxKind.TemplateTail
  );
}

function isWhitespace(character) {
  return (
    character === ' ' ||
    character === '\n' ||
    character === '\r' ||
    character === '\t' ||
    character === '\f' ||
    character === '\v'
  );
}

function tokenizeLiteral(text) {
  const tokens = [];
  let start = -1;
  for (let index = 0; index <= text.length; index += 1) {
    const atBoundary = index === text.length || isWhitespace(text[index]);
    if (!atBoundary && start === -1) {
      start = index;
    }
    if (atBoundary && start !== -1) {
      tokens.push({ token: text.slice(start, index), offset: start });
      start = -1;
    }
  }
  return tokens;
}

function baseUtility(token) {
  let bracketDepth = 0;
  let lastVariantSeparator = -1;
  for (let index = 0; index < token.length; index += 1) {
    const character = token[index];
    if (character === '[') {
      bracketDepth += 1;
    } else if (character === ']') {
      bracketDepth = Math.max(0, bracketDepth - 1);
    } else if (character === ':' && bracketDepth === 0) {
      lastVariantSeparator = index;
    }
  }

  let utility = token.slice(lastVariantSeparator + 1);
  while (utility.startsWith('!') || utility.startsWith('-')) {
    utility = utility.slice(1);
  }
  return utility;
}

function isRawAxisStyleUtility(token) {
  const utility = baseUtility(token);
  if (utility.startsWith('data-axis-') || utility.startsWith('[data-axis-')) {
    return false;
  }
  if (utility.includes('://')) {
    return false;
  }
  if (utility.includes('.') && !utility.startsWith('[')) {
    return false;
  }

  const axisMarker = utility.indexOf('-axis-');
  const forwardSlash = utility.indexOf('/');
  const backwardSlash = utility.indexOf('\\');
  if (
    (forwardSlash !== -1 && forwardSlash < axisMarker) ||
    (backwardSlash !== -1 && backwardSlash < axisMarker)
  ) {
    return false;
  }

  const segments = utility.split('-');
  const axisIndex = segments.indexOf('axis');
  if (axisIndex <= 0 || axisIndex >= segments.length - 1) {
    return false;
  }

  return true;
}

export function findAxisStyleConsumptionIssues(files) {
  const issues = [];
  const authoredFiles = [...files]
    .map((file) => ({ path: normalizePath(file.path), source: file.source }))
    .filter((file) => isAuthoredTypeScript(file.path))
    .sort((left, right) => left.path.localeCompare(right.path));

  for (const file of authoredFiles) {
    const sourceFile = ts.createSourceFile(
      file.path,
      file.source,
      ts.ScriptTarget.Latest,
      true,
      scriptKind(file.path),
    );

    function visit(node) {
      if (isLiteralTextNode(node)) {
        for (const { token, offset } of tokenizeLiteral(node.text)) {
          if (!isRawAxisStyleUtility(token)) {
            continue;
          }
          const position = Math.min(node.getStart(sourceFile) + 1 + offset, sourceFile.end);
          const { line, character } = sourceFile.getLineAndCharacterOfPosition(position);
          issues.push({
            path: file.path,
            line: line + 1,
            column: character + 1,
            token,
            message: `raw Axis style utility '${token}' bypasses axisStyles`,
          });
        }
      }
      ts.forEachChild(node, visit);
    }

    visit(sourceFile);
  }

  return issues;
}

async function collectAuthoredFiles(frontendRoot) {
  const files = [];

  async function walk(relativeDirectory) {
    const absoluteDirectory = path.join(frontendRoot, relativeDirectory);
    let entries;
    try {
      entries = await fs.readdir(absoluteDirectory, { withFileTypes: true });
    } catch (error) {
      if (error.code === 'ENOENT') {
        return;
      }
      throw error;
    }

    entries.sort((left, right) => left.name.localeCompare(right.name));
    for (const entry of entries) {
      const relativePath = path.posix.join(relativeDirectory, entry.name);
      if (entry.isDirectory()) {
        await walk(relativePath);
      } else if (entry.isFile() && isAuthoredTypeScript(relativePath)) {
        files.push({
          path: relativePath,
          source: await fs.readFile(path.join(frontendRoot, relativePath), 'utf8'),
        });
      }
    }
  }

  for (const root of authoredRoots) {
    await walk(root);
  }
  return files;
}

async function main() {
  const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const issues = findAxisStyleConsumptionIssues(await collectAuthoredFiles(frontendRoot));
  if (issues.length === 0) {
    console.log('Axis style consumption check passed.');
    return;
  }

  for (const issue of issues) {
    console.error(`${issue.path}:${issue.line}:${issue.column} ${issue.message}`);
  }
  process.exitCode = 1;
}

const invokedPath = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : undefined;
if (invokedPath === import.meta.url) {
  await main();
}
