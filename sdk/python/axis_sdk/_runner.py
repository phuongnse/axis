"""Runs the operations of one installed Python package version, see docs/reference/extension-packages.md#the-runner-protocol.

The protocol goes over the standard input and the stream that axis_runner.py saved. Each ``run`` gets exactly one
answer. The exit code is 0 when the input ends or a ``stop`` arrives, 1 when the version does not load, and 2 when a
line is not a ``run`` or a ``stop``. The context hands out only the bindings of the ``run``, so an operation that asks
for another binding gets a ``failure`` with that binding, even when it catches the error. Any other error is a
``failure`` with the name of its type, never its message.
"""

import importlib.util
import json
import os
import re
import sys
import uuid
from datetime import date, datetime
from decimal import Decimal

from . import OperationContext, RetryableError

STOPPED = 0
NOT_LOADED = 1
BAD_INPUT = 2

_INTEGER = re.compile(r'[+-]?[0-9]+')
_DECIMAL = re.compile(r'[+-]?[0-9]+(\.[0-9]+)?')
_DATE = re.compile(r'[0-9]{4}-[0-9]{2}-[0-9]{2}')
_FRACTION = re.compile(r'(\.[0-9]{6})[0-9]+')
_LONG_MIN = -(2 ** 63)
_LONG_MAX = 2 ** 63 - 1


def main(folder, protocol):
    """Loads the version in ``folder``, answers ``ready`` and serves the ``run`` lines of the standard input."""
    try:
        with open(os.path.join(folder, 'axis-package.json'), encoding='utf-8') as file:
            manifest = json.load(file)
        adapter = _load_adapter(folder, manifest['adapter'])
    except Exception as error:
        print(_error_name(error), file=sys.stderr)
        return NOT_LOADED

    try:
        _write(protocol, {'type': 'ready'})
        for raw in sys.stdin.buffer:
            try:
                message = json.loads(raw.decode('utf-8'))
                kind = message['type']
            except (ValueError, KeyError, TypeError):
                return BAD_INPUT
            if kind == 'stop':
                return STOPPED
            if kind != 'run':
                return BAD_INPUT
            try:
                operation, operation_id, values, bindings = (
                    message['operation'], uuid.UUID(message['operationId']), message['input'], message['bindings'])
                urls, secrets = bindings['urls'], bindings['secrets']
                if not (isinstance(operation, str) and isinstance(values, dict) and isinstance(urls, dict) and isinstance(secrets, dict)):
                    return BAD_INPUT
            except (ValueError, KeyError, TypeError):
                return BAD_INPUT
            _write(protocol, _answer(manifest, adapter, operation, OperationContext(operation_id, urls, secrets), values))
    except BrokenPipeError:
        # The connection closed, so the host is killing this runner.
        pass
    return STOPPED


def _load_adapter(folder, file_name):
    spec = importlib.util.spec_from_file_location('axis_adapter', os.path.join(folder, file_name))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _answer(manifest, adapter, name, context, values):
    """The one answer to a ``run``."""
    try:
        declared = _find_operation(manifest, name)
        function = getattr(adapter, declared['type'])
        arguments = _decode(declared.get('input', []), values)
        output = function(arguments, context)
    except Exception as error:
        return _failed(context, error)

    # The operation may have swallowed the error, so a refused binding fails the attempt whatever it returned.
    if context.violation is not None:
        return {'type': 'failure', 'binding': context.violation}
    try:
        return {'type': 'result', 'output': _encode(declared.get('output', []), output)}
    except Exception as error:
        return _failed(context, error)


def _failed(context, error):
    if context.violation is not None:
        return {'type': 'failure', 'binding': context.violation}
    # The message can hold input values or secrets, so only the type is sent.
    return {'type': 'retryable' if isinstance(error, RetryableError) else 'failure', 'error': _error_name(error)}


def _find_operation(manifest, name):
    for operation in manifest['operations']:
        if operation['name'].casefold() == name.casefold():
            return operation
    raise LookupError('The package has no such operation.')


def _error_name(error):
    """``module.QualName``, with the module left out for a built-in exception."""
    kind = type(error)
    return kind.__qualname__ if kind.__module__ == 'builtins' else f'{kind.__module__}.{kind.__qualname__}'


def _write(protocol, message):
    data = (json.dumps(message, ensure_ascii=False, separators=(',', ':')) + '\n').encode('utf-8')
    view = memoryview(data)
    while view:
        view = view[protocol.write(view) or 0:]


def _decode(fields, values):
    """Reads the fields of the manifest from ``values``. A missing field is None. A mismatch raises ValueError."""
    result = {}
    for field in fields:
        value = values.get(field['name'])
        result[field['name']] = None if value is None else _decode_value(field['type'], value)
    return result


def _decode_value(kind, value):
    if kind == 'boolean':
        if isinstance(value, bool):
            return value
        raise ValueError('The value is not a valid boolean.')
    if not isinstance(value, str):
        raise ValueError(f'The value is not a valid {kind}.')
    if kind == 'text':
        return value
    if kind == 'integer' and _INTEGER.fullmatch(value) and _LONG_MIN <= int(value) <= _LONG_MAX:
        return int(value)
    if kind == 'decimal' and _DECIMAL.fullmatch(value):
        return Decimal(value)
    if kind == 'date' and _DATE.fullmatch(value):
        return date.fromisoformat(value)
    if kind == 'date-time':
        # The protocol allows a Z and more than six fractional digits, which older Pythons do not read.
        text = _FRACTION.sub(r'\1', value[:-1] + '+00:00' if value.endswith(('Z', 'z')) else value)
        parsed = datetime.fromisoformat(text)
        if parsed.tzinfo is not None:
            return parsed
    raise ValueError(f'The value is not a valid {kind}.')


def _encode(fields, output):
    """Writes the fields of the manifest that ``output`` holds. A value of another type than its field's raises TypeError."""
    if not isinstance(output, dict):
        raise TypeError('The operation returned no output dict.')
    result = {}
    for field in fields:
        if field['name'] in output:
            value = output[field['name']]
            result[field['name']] = None if value is None else _encode_value(field['type'], value)
    return result


def _encode_value(kind, value):
    if kind == 'text' and isinstance(value, str):
        return value
    if kind == 'integer' and isinstance(value, int) and not isinstance(value, bool):
        if not _LONG_MIN <= value <= _LONG_MAX:
            raise ValueError('The integer is outside the 64-bit range.')
        return str(value)
    if kind == 'decimal' and isinstance(value, (Decimal, int)) and not isinstance(value, bool):
        if isinstance(value, Decimal) and not value.is_finite():
            raise ValueError('The decimal is not finite.')
        return format(value, 'f') if isinstance(value, Decimal) else str(value)
    if kind == 'boolean' and isinstance(value, bool):
        return value
    if kind == 'date' and isinstance(value, date) and not isinstance(value, datetime):
        return value.isoformat()
    if kind == 'date-time' and isinstance(value, datetime) and value.tzinfo is not None and value.utcoffset() is not None:
        return value.isoformat()
    raise TypeError(f'The value is not of the Python type of a {kind} field.')
