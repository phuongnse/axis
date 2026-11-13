# The Python runner of a package host, see docs/reference/extension-packages.md#the-runner-protocol.
# Usage: python3 -I -B axis_runner.py <version folder>
import os
import sys

# The protocol keeps the real standard output. Whatever else writes to file descriptor 1, a print or a native
# library, goes to standard error. This runs before anything else is imported.
protocol = os.fdopen(os.dup(1), 'wb', buffering=0)
os.dup2(2, 1)
sys.stdout = sys.stderr

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, sys.argv[1])

from axis_sdk import _runner  # noqa: E402

sys.exit(_runner.main(sys.argv[1], protocol))
