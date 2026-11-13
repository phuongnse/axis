from axis_fixture_dependency import greeting
from axis_sdk import RetryableError

# The test package's adapter. It imports its one dependency from the folder it sits in, and the SDK from the runner.
# What it prints at import and while it runs goes to standard error.
print(greeting("Axis"))


def greet(input, context):
    print("Greeting " + input["name"])
    return {"greeting": greeting(input["name"])}


def echo(input, context):
    return dict(input)


def probe(input, context):
    mode = input["mode"]
    if mode == "declared":
        response = context.get_http_client("TestUrl").request(
            "POST",
            "probe",
            headers={
                "Authorization": "Bearer " + context.get_secret("TestToken"),
                "Idempotency-Key": str(context.operation_id),
            },
            body=b"{}",
        )
        return {"status": response.status}
    if mode == "otherHost":
        context.get_http_client("TestUrl").request("GET", "https://other.example.test/probe")
    elif mode == "undeclaredSecret":
        context.get_secret("OtherToken")
    elif mode == "swallow":
        try:
            context.get_secret("OtherToken")
        except Exception:
            pass
    return {"status": 0}


def flaky(input, context):
    raise RetryableError("Attempt failed")


def fail(input, context):
    raise ValueError(f"Bad value {input['a']}")
