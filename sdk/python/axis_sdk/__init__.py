"""The Python SDK of an Axis extension package, see docs/reference/extension-packages.md#contracts.

An adapter function is ``def operation(input, context) -> dict``. It gets the input as a dict keyed by field name and
an OperationContext, and returns the output as a dict keyed by field name. It maps the operation onto a library that
does not change. Raise RetryableError to say that the attempt may succeed later. Any other exception fails the call.
"""

import urllib.error
import urllib.request
import uuid
from urllib.parse import urljoin, urlsplit

__all__ = ['HttpClient', 'OperationContext', 'RetryableError']

_DEFAULT_PORTS = {'http': 80, 'https': 443}


class RetryableError(Exception):
    """Raised by an operation to say that the attempt may succeed later."""


class CapabilityError(Exception):
    """Raised when an operation asks for a binding or a host that it does not declare. Its message holds no value."""


class _PassThrough(urllib.request.HTTPErrorProcessor):
    """Returns every response unchanged, so a non-2xx response is returned and no redirect is followed."""

    def http_response(self, request, response):
        return response

    https_response = http_response


def _origin(url):
    parts = urlsplit(url)
    scheme = (parts.scheme or '').lower()
    return scheme, (parts.hostname or '').lower(), parts.port or _DEFAULT_PORTS.get(scheme)


class OperationContext:
    """What the platform gives an operation. It holds only the bindings that the operation declares."""

    def __init__(self, operation_id, urls, secrets):
        self.operation_id = operation_id if isinstance(operation_id, uuid.UUID) else uuid.UUID(operation_id)
        self._urls = {name.casefold(): value for name, value in urls.items()}
        self._secrets = {name.casefold(): value for name, value in secrets.items()}
        self.violation = None

    def get_secret(self, name):
        """The value of the declared secret binding ``name``."""
        try:
            return self._secrets[name.casefold()]
        except KeyError:
            raise self._refuse(name, 'The operation asked for a secret binding that it does not declare.') from None

    def get_http_client(self, name):
        """A client for the declared url binding ``name``, which may send requests only to a declared host."""
        try:
            return HttpClient(self._urls[name.casefold()], name, self)
        except KeyError:
            raise self._refuse(name, 'The operation asked for a url binding that it does not declare.') from None

    def _refuse(self, binding, message):
        """Keeps the first violation and returns the exception that refuses it."""
        if self.violation is None:
            self.violation = binding
        return CapabilityError(message)

    def _declares(self, url):
        origin = _origin(url)
        return any(origin == _origin(declared) for declared in self._urls.values())


class HttpClient:
    """Sends requests to the host of a url binding. It follows no redirects and goes through the runner's proxy variables."""

    def __init__(self, base_url, binding, context):
        self.base_url = base_url if base_url.endswith('/') else base_url + '/'
        self._binding = binding
        self._context = context
        self._opener = urllib.request.build_opener(_PassThrough())

    def request(self, method, url, *, headers=None, body=None):
        """Sends the request and returns the response, whatever its status. ``url`` is resolved against the base URL."""
        target = urljoin(self.base_url, url)
        if not self._context._declares(target):
            raise self._context._refuse(
                self._binding, 'The operation sent a request to a host that none of its url bindings declares.')
        request = urllib.request.Request(target, data=body, headers=dict(headers or {}), method=method)
        return self._opener.open(request)
