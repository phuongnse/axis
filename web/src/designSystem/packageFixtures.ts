// Generated from samples/packages for the `studio` preview's package states, see #3978. Each version is the
// SHA-256 of the content, as docs/reference/studio-packages.md#the-authoring-api says.
import type { StudioPackageFile } from '../platform/studio'

/** The files of the Typical folder's packages, sorted by package and then by path. */
export const typicalPackageFiles: StudioPackageFile[] = [
  {
    package: 'payment-references',
    path: 'CreditorReferenceOperation.cs',
    version: '9727bab49fbe8992b6d011db48fdabbfde689ed1bbfd3d20a605c1331eab0866',
    content:
      'using System.Globalization;\nusing System.Text;\nusing Axis.Extensions;\n\nnamespace PaymentReferences;\n\n/// <summary>\n/// Turns a reference, such as a purchase request number, into an ISO 11649 creditor reference: <c>RF</c>,\n/// two mod-97 check digits, then the reference without separators.\n/// </summary>\npublic sealed class CreditorReferenceOperation : IOperation\n{\n    private const int MaxBodyLength = 21;\n\n    public Task<IReadOnlyDictionary<string, object?>> RunAsync(\n        IReadOnlyDictionary<string, object?> input,\n        IOperationContext context,\n        CancellationToken cancellationToken)\n    {\n        ArgumentNullException.ThrowIfNull(input);\n\n        IReadOnlyDictionary<string, object?> output = new Dictionary<string, object?>\n        {\n            ["creditorReference"] = Create((string)input["reference"]!),\n        };\n        return Task.FromResult(output);\n    }\n\n    /// <summary>\n    /// The creditor reference of <paramref name="reference"/>. Only its ASCII letters and digits count, in\n    /// upper case, and at most 21 of them fit.\n    /// </summary>\n    /// <exception cref="ArgumentException">The reference has no letter or digit, or more than 21.</exception>\n    public static string Create(string reference)\n    {\n        ArgumentNullException.ThrowIfNull(reference);\n\n        var body = new StringBuilder(reference.Length);\n        foreach (var character in reference)\n        {\n            if (char.IsAsciiLetterOrDigit(character))\n            {\n                body.Append(char.ToUpperInvariant(character));\n            }\n        }\n\n        if (body.Length is 0 or > MaxBodyLength)\n        {\n            throw new ArgumentException($"The reference must hold 1 to {MaxBodyLength} letters or digits, but \'{reference}\' holds {body.Length}.", nameof(reference));\n        }\n\n        var digits = body.ToString();\n        var check = 98 - Mod97(digits + "RF00");\n        return "RF" + check.ToString("D2", CultureInfo.InvariantCulture) + digits;\n    }\n\n    /// <summary>The remainder after dividing by 97 the number that <paramref name="text"/> stands for, with A as 10 up to Z as 35.</summary>\n    private static int Mod97(string text)\n    {\n        var remainder = 0;\n        foreach (var character in text)\n        {\n            var value = char.IsAsciiDigit(character) ? character - \'0\' : character - \'A\' + 10;\n            remainder = (value < 10 ? remainder * 10 + value : remainder * 100 + value) % 97;\n        }\n\n        return remainder;\n    }\n}\n',
  },
  {
    package: 'payment-references',
    path: 'PaymentReferences.csproj',
    version: '063d9db298d60644327d5106c88cb9939090014c67efc953aa26621bd88615db',
    content:
      '<Project Sdk="Microsoft.NET.Sdk">\n\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <AssemblyName>PaymentReferences</AssemblyName>\n    <RootNamespace>PaymentReferences</RootNamespace>\n    <Nullable>enable</Nullable>\n    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>\n  </PropertyGroup>\n\n  <ItemGroup>\n    <PackageReference Include="Axis.Extensions" Version="1.0.0" />\n  </ItemGroup>\n\n</Project>\n',
  },
  {
    package: 'payment-references',
    path: 'axis-package.json',
    version: '3b1b485cbb98c9e1333177de7ecb817a3fe71c82d47fa0b6ab3e9bc6ed6d0898',
    content:
      '{\n  "id": "payment-references",\n  "version": "1.0.0",\n  "label": "Payment references",\n  "assembly": "PaymentReferences.dll",\n  "lock": "packages.lock.json",\n  "operations": [\n    {\n      "name": "creditorReference",\n      "type": "PaymentReferences.CreditorReferenceOperation",\n      "idempotent": true,\n      "timeout": "PT10S",\n      "input": [{ "name": "reference", "type": "text", "required": true }],\n      "output": [{ "name": "creditorReference", "type": "text", "required": true }]\n    }\n  ]\n}\n',
  },
  {
    package: 'payment-references',
    path: 'packages.lock.json',
    version: 'cc96d0dfea548fbee4ab935c835c5bf78f8f50da4031a1ce8eb78f6d8cbbb152',
    content:
      '{\n  "version": 2,\n  "dependencies": {\n    "net10.0": {\n      "Axis.Extensions": {\n        "type": "Direct",\n        "requested": "[1.0.0, )",\n        "resolved": "1.0.0",\n        "contentHash": "q1m4Zp8vV0cJm3Hc5mK2y3pT1nXlqvJ8h2bR4yN6sWfE9aD7uC0oL5iG3tK8eZ1xQ2wB4rY6nM9jH7fV5dS3gA=="\n      }\n    }\n  }\n}\n',
  },
  {
    package: 'payment-terms',
    path: 'axis-package.json',
    version: 'c82dcf7e011e1aaf1a3e407156ba788d35d52a186866918e9922d7fbf13eced6',
    content:
      '{\n  "id": "payment-terms",\n  "version": "1.0.0",\n  "label": "Payment terms",\n  "language": "node",\n  "adapter": "index.js",\n  "lock": "package-lock.json",\n  "operations": [\n    {\n      "name": "dueDate",\n      "type": "dueDate",\n      "idempotent": true,\n      "input": [{ "name": "from", "type": "date-time", "required": true }],\n      "output": [{ "name": "dueDate", "type": "date", "required": true }]\n    }\n  ]\n}\n',
  },
  {
    package: 'payment-terms',
    path: 'index.js',
    version: '224ed796bc5610d788349334a09d0d39bedd33259d95556687bae428b523fdd1',
    content:
      '"use strict";\nvar __importDefault = (this && this.__importDefault) || function (mod) {\n    return (mod && mod.__esModule) ? mod : { "default": mod };\n};\nObject.defineProperty(exports, "__esModule", { value: true });\nexports.dueDate = dueDate;\n// A sample extension package that wraps dayjs, see docs/reference/extension-packages.md#sample-and-test-packages.\nconst dayjs_1 = __importDefault(require("dayjs"));\nconst utc_1 = __importDefault(require("dayjs/plugin/utc"));\ndayjs_1.default.extend(utc_1.default);\nfunction dueDate(input) {\n    return { dueDate: dayjs_1.default.utc(input.from).add(30, "day").endOf("month").format("YYYY-MM-DD") };\n}\n',
  },
  {
    package: 'payment-terms',
    path: 'index.test.mjs',
    version: '1e4f99df119448f906bc7c18e5001ac1793fa12a786ed2fca348f02f8ba795c2',
    content:
      "// Unit test of the sample package payment-terms, see docs/reference/extension-packages.md#sample-and-test-packages.\n// It runs in the installed copy under artifacts/extensions, where dayjs is in node_modules.\nimport assert from 'node:assert/strict'\nimport test from 'node:test'\nimport dayjs from 'dayjs'\nimport utc from 'dayjs/plugin/utc.js'\nimport { dueDate } from './index.js'\n\ndayjs.extend(utc)\n\nconst cases = [\n  ['a month end', '2026-01-31T10:00:00Z', '2026-03-31'],\n  ['a leap year', '2028-01-30T00:00:00Z', '2028-02-29'],\n  ['a year that is not a leap year', '2027-01-30T00:00:00Z', '2027-03-31'],\n  ['an offset that moves the UTC day', '2026-01-01T20:00:00-05:00', '2026-02-28'],\n]\n\nfor (const [name, from, expected] of cases) {\n  test(`dueDate is the last day of the month 30 days after ${name}`, () => {\n    const { dueDate: actual } = dueDate({ from })\n\n    assert.equal(actual, expected)\n    assert.equal(actual, dayjs.utc(from).add(30, 'day').endOf('month').format('YYYY-MM-DD'))\n  })\n}\n",
  },
  {
    package: 'payment-terms',
    path: 'index.ts',
    version: '1fa8fa8292ca837beb9d1c7a188967d57379536af44eda992eb9afcd01e7263d',
    content:
      '// A sample extension package that wraps dayjs, see docs/reference/extension-packages.md#sample-and-test-packages.\nimport dayjs from "dayjs";\nimport utc from "dayjs/plugin/utc";\n\ndayjs.extend(utc);\n\nexport function dueDate(input: { from: string }): { dueDate: string } {\n  return { dueDate: dayjs.utc(input.from).add(30, "day").endOf("month").format("YYYY-MM-DD") };\n}\n',
  },
  {
    package: 'payment-terms',
    path: 'package-lock.json',
    version: '177f02cdfabf44d383697830f37d8f05acd3add381eca74ab01a32d7a4f054d8',
    content:
      '{\n  "name": "payment-terms",\n  "version": "1.0.0",\n  "lockfileVersion": 3,\n  "requires": true,\n  "packages": {\n    "": {\n      "name": "payment-terms",\n      "version": "1.0.0",\n      "dependencies": {\n        "dayjs": "file:vendor/dayjs-1.11.23.tgz"\n      }\n    },\n    "node_modules/dayjs": {\n      "version": "1.11.23",\n      "resolved": "file:vendor/dayjs-1.11.23.tgz",\n      "integrity": "sha512-QDTCU0M0MxR3hQfnlDJfwekQiaanm1ubOD231u73WBckQ/fsamwRLiE2GBz6D3a/xF1NgfiDLJjXBa1hYOYTtQ==",\n      "license": "MIT"\n    }\n  }\n}\n',
  },
  {
    package: 'payment-terms',
    path: 'package.json',
    version: 'c1645fcbeb5f517d4df6bb08d3607826e1b32bfa00e3df9c1a23c609cab2bb39',
    content:
      '{\n  "name": "payment-terms",\n  "version": "1.0.0",\n  "private": true,\n  "dependencies": {\n    "dayjs": "file:vendor/dayjs-1.11.23.tgz"\n  }\n}\n',
  },
  {
    package: 'payment-terms',
    path: 'tsconfig.json',
    version: 'b1cdc4416e4568f72b5deabb51eb71dc347880c08d9328461ace37567a493f01',
    content:
      '{\n  "compilerOptions": {\n    "target": "ES2022",\n    "module": "Node16",\n    "strict": true,\n    "esModuleInterop": true,\n    "skipLibCheck": true,\n    "outDir": "."\n  },\n  "files": ["index.ts"]\n}\n',
  },
  {
    package: 'phone-numbers',
    path: 'adapter.py',
    version: 'b0e43632ef7670a7dcbddbbf3ea144f9678146126e3d1f74ad97266d966a840c',
    content:
      'import phonenumbers\n\n# The sample Python package, see docs/reference/extension-packages.md#sample-and-test-packages. It leaves the\n# library\'s errors alone: a number that cannot be parsed fails the call with the type name of NumberParseException.\n\n\ndef normalize(input, context):\n    parsed = phonenumbers.parse(input["number"], input.get("region"))\n    return {"number": phonenumbers.format_number(parsed, phonenumbers.PhoneNumberFormat.E164)}\n',
  },
  {
    package: 'phone-numbers',
    path: 'axis-package.json',
    version: '42d82dd70f259a6131b0531fbcd5808eb727ef88d95b80f80b087b2696a2b66b',
    content:
      '{\n  "id": "phone-numbers",\n  "version": "1.0.0",\n  "label": "Phone numbers",\n  "language": "python",\n  "adapter": "adapter.py",\n  "lock": "requirements.txt",\n  "operations": [\n    {\n      "name": "normalize",\n      "type": "normalize",\n      "idempotent": true,\n      "input": [\n        { "name": "number", "type": "text", "required": true },\n        { "name": "region", "type": "text" }\n      ],\n      "output": [{ "name": "number", "type": "text", "required": true }]\n    }\n  ]\n}\n',
  },
  {
    package: 'phone-numbers',
    path: 'requirements.txt',
    version: 'e6ac08b692b56e502114534fc8b55376844c2066857a18bfc697a43b070b41f1',
    content: 'phonenumbers==9.0.43 --hash=sha256:349bc799f15e010e35bd2c7d28149950ae74479cb1e11fb054ed9a95ecb4b437\n',
  },
  {
    package: 'phone-numbers',
    path: 'tests/test_adapter.py',
    version: 'c8081e98ec5f79f7c16b1c63eed2e2f4e0ce31114e067f1e80cf0ec295072318',
    content:
      'import pytest\nfrom phonenumbers import NumberParseException\n\nfrom adapter import normalize\n\n\ndef test_a_national_number_with_its_region_becomes_e164():\n    assert normalize({"number": "020 7946 0018", "region": "GB"}, None) == {"number": "+442079460018"}\n\n\ndef test_an_international_number_needs_no_region():\n    assert normalize({"number": "+1 206 555 0100"}, None) == {"number": "+12065550100"}\n\n\ndef test_a_number_that_cannot_be_parsed_fails_the_call():\n    with pytest.raises(NumberParseException):\n        normalize({"number": "not a number"}, None)\n',
  },
]
