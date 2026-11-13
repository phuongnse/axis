import phonenumbers

# The sample Python package, see docs/reference/extension-packages.md#sample-and-test-packages. It leaves the
# library's errors alone: a number that cannot be parsed fails the call with the type name of NumberParseException.


def normalize(input, context):
    parsed = phonenumbers.parse(input["number"], input.get("region"))
    return {"number": phonenumbers.format_number(parsed, phonenumbers.PhoneNumberFormat.E164)}
