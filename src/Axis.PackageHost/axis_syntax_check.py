# The syntax check of a package host's Python build, see docs/reference/extension-packages.md#building-a-package.
# Usage: python3 -I -B -c <this script> <folder>
# It byte-compiles each .py file under the folder, and prints one last JSON line:
# {"compiled": n, "problems": [{"file", "line", "column", "type", "message"}]}. A file is a path relative to the folder that uses '/'.
# compile() only parses, so no package code runs, and with -B no .pyc file is written.
import json
import os
import sys
import warnings

warnings.simplefilter('ignore')


def main(folder):
    paths = []
    for root, directories, names in os.walk(folder):
        directories.sort()
        for name in sorted(names):
            if name.endswith('.py'):
                paths.append(os.path.join(root, name))

    problems = []
    for path in paths:
        relative = os.path.relpath(path, folder).replace(os.sep, '/')
        try:
            with open(path, 'rb') as file:
                source = file.read()
            compile(source, relative, 'exec', dont_inherit=True)
        except SyntaxError as error:
            problems.append({
                'file': relative,
                'line': error.lineno,
                'column': error.offset,
                'type': type(error).__name__,
                'message': error.msg,
            })
        except Exception as error:
            problems.append({
                'file': relative,
                'line': None,
                'column': None,
                'type': type(error).__name__,
                'message': str(error),
            })

    print(json.dumps({'compiled': len(paths) - len(problems), 'problems': problems}))


main(sys.argv[1])
