# The test runner of a package host, see docs/reference/extension-packages.md#the-runner-protocol.
# Usage: python3 -I -B -c <this script> <built copy> <SDK folder> <relative path of a test file>...
# It runs each test file with unittest and prints one JSON line with the outcome of each test.
import json
import os
import sys
import traceback
import unittest

# The result keeps the real standard output. Whatever else writes to file descriptor 1, a print in a test or a native
# library, goes to standard error. This runs before any test is imported.
out = os.fdopen(os.dup(1), 'w')
os.dup2(2, 1)
sys.stdout = sys.stderr

copy, sdk, paths = sys.argv[1], sys.argv[2], sys.argv[3:]
os.chdir(copy)
sys.path.insert(0, sdk)
sys.path.insert(0, copy)


class Outcomes(unittest.TestResult):
    """Keeps the outcome of each test by its id. A skipped test is left out and a failed one stays failed."""

    def __init__(self):
        super().__init__()
        self.tests = {}

    def _record(self, test, outcome, message=None):
        if self.tests.get(test.id(), {}).get('outcome') != 'failed':
            self.tests[test.id()] = {'name': test.id(), 'outcome': outcome, 'message': message}

    def _failed(self, test, err):
        self._record(test, 'failed', traceback.format_exception_only(*err[:2])[-1].strip())

    def addSuccess(self, test):
        self._record(test, 'passed')

    def addExpectedFailure(self, test, err):
        self._record(test, 'passed')

    def addUnexpectedSuccess(self, test):
        self._record(test, 'failed', 'The test was expected to fail, but it passed.')

    def addFailure(self, test, err):
        self._failed(test, err)

    def addError(self, test, err):
        self._failed(test, err)

    def addSkip(self, test, reason):
        pass

    def addSubTest(self, test, subtest, err):
        if err is not None:
            self._failed(test, err)


loader = unittest.TestLoader()
suite = unittest.TestSuite(loader.loadTestsFromName(path[:-len('.py')].replace('/', '.')) for path in paths)
result = Outcomes()
suite.run(result)
out.write(json.dumps({'tests': list(result.tests.values())}) + '\n')
out.flush()
