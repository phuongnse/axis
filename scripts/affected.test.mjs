import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import {
  chmodSync,
  existsSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { after, before, describe, test } from "node:test";
import {
  changedPaths,
  decideSafe,
  matches,
  safeSuites,
  sinceCommit,
} from "./lib/affected.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

const unreachable = () =>
  assert.fail("The changed paths should not be listed.");
const smoke = ["dev-script", "compose", "images", "onprem"];

describe("decideSafe", () => {
  const runs = (name, paths, base = "abc") =>
    decideSafe({ base, matchers: safeSuites[name], listChanged: () => paths })
      .run;

  test("skips integration when a change is only under web/src", () => {
    assert.equal(runs("integration", ["web/src/App.tsx"]), false);
  });

  test("runs integration when a change also touches a path the tests read", () => {
    const others = [
      "src/Axis.Server/Program.cs",
      "samples/apps/purchase-requests/application.json",
      "web/package.json",
      "web/package-lock.json",
      "tests/Axis.Integration.Tests/Foo.cs",
    ];
    for (const other of others) {
      assert.equal(
        runs("integration", ["web/src/App.tsx", other]),
        true,
        other,
      );
    }
  });

  test("skips integration for docs, Markdown and E2E spec changes", () => {
    for (const paths of [
      ["docs/delivery.md"],
      ["README.md"],
      ["tests/e2e/specs/x.spec.ts"],
      ["docs/x.md", "web/src/App.tsx"],
    ]) {
      assert.equal(runs("integration", paths), false, paths.join(" "));
    }
  });

  test("skips Playwright when every change is under docs or a Markdown file", () => {
    assert.equal(runs("playwright", ["docs/delivery.md"]), false);
    assert.equal(runs("playwright", ["README.md", "docs/x.md"]), false);
  });

  test("runs Playwright when a change touches the SPA or the E2E specs", () => {
    assert.equal(runs("playwright", ["web/src/App.tsx"]), true);
    assert.equal(runs("playwright", ["tests/e2e/specs/x.spec.ts"]), true);
  });

  test("runs every suite with no base, an empty base, a listing error or no changed path", () => {
    const listError = () => {
      throw new Error("bad revision");
    };
    assert.ok(smoke.every((name) => Object.hasOwn(safeSuites, name)));
    for (const name of Object.keys(safeSuites)) {
      const matchers = safeSuites[name];
      assert.equal(
        decideSafe({ base: undefined, matchers, listChanged: unreachable }).run,
        true,
      );
      assert.equal(
        decideSafe({ base: "", matchers, listChanged: unreachable }).run,
        true,
      );
      assert.equal(
        decideSafe({ base: "abc", matchers, listChanged: listError }).run,
        true,
      );
      assert.equal(
        decideSafe({ base: "abc", matchers, listChanged: () => [] }).run,
        true,
      );
    }
  });

  test("runs every smoke suite when a change is under the server, the data projects or the SPA", () => {
    for (const paths of [
      ["src/Axis.Server/Http/SecurityHeaders.cs"],
      ["src/Axis.Data/EntityProvisioner.cs"],
      ["web/src/App.tsx"],
    ]) {
      for (const name of smoke)
        assert.equal(runs(name, paths), true, `${name} ${paths}`);
    }
  });

  test("skips every smoke suite when every change is under docs or a Markdown file", () => {
    for (const paths of [
      ["docs/delivery.md"],
      ["README.md"],
      ["docs/x.md", "samples/apps/x/README.md"],
    ]) {
      for (const name of smoke)
        assert.equal(runs(name, paths), false, `${name} ${paths}`);
    }
  });

  test("skips every suite when a change touches only the GitHub workflows, NexKit settings or docs", () => {
    const paths = [
      ".github/workflows/ci.yml",
      ".nexkit/config.json",
      "docs/delivery.md",
    ];
    for (const name of Object.keys(safeSuites))
      assert.equal(runs(name, paths), false, name);
  });

  test("runs every smoke suite when a docs change also touches another path", () => {
    for (const name of smoke)
      assert.equal(runs(name, ["docs/x.md", "compose.yaml"]), true, name);
  });
});

describe("sinceCommit", () => {
  test("prefers the check's own passed commit, then the merge base", () => {
    const all = {
      NEXKIT_CHECK_VERIFIED_SHA: "check",
      NEXKIT_VERIFIED_SHA: "round",
      NEXKIT_BASE_SHA: "base",
    };
    assert.deepEqual(sinceCommit(all), {
      name: "NEXKIT_CHECK_VERIFIED_SHA",
      sha: "check",
    });
    assert.deepEqual(sinceCommit({ NEXKIT_BASE_SHA: "base" }), {
      name: "NEXKIT_BASE_SHA",
      sha: "base",
    });
  });

  test("never compares with the last green round, which may not have run this check or its command", () => {
    const noOwnCommit = {
      NEXKIT_CHECK_VERIFIED_SHA: "",
      NEXKIT_VERIFIED_SHA: "round",
      NEXKIT_BASE_SHA: "base",
    };
    assert.deepEqual(sinceCommit(noOwnCommit), {
      name: "NEXKIT_BASE_SHA",
      sha: "base",
    });
    assert.equal(sinceCommit({ NEXKIT_VERIFIED_SHA: "round" }), undefined);
  });

  test("gives no commit outside NexKit", () => {
    assert.equal(sinceCommit({}), undefined);
    assert.equal(
      sinceCommit({
        NEXKIT_CHECK_VERIFIED_SHA: "",
        NEXKIT_VERIFIED_SHA: "",
        NEXKIT_BASE_SHA: "",
      }),
      undefined,
    );
  });
});

// NexKit skips a whole check before its job starts when every changed path is in the check's
// `unaffected_by` (NexKit's nexkit/globs.py). The scripts skip each suite inside the job by the safe
// lists above. Both must agree, or one would skip what the other runs.
describe("the unaffected_by lists in .nexkit/config.json", () => {
  // A NexKit glob: `*` and `?` stay in a segment, and `**`, a whole segment, crosses folders.
  const globRegExp = (pattern) => {
    const segments = pattern.split("/");
    const source = segments
      .map((segment, index) => {
        const last = index === segments.length - 1;
        if (segment === "**") return last ? ".*" : "(?:[^/]+/)*";
        const text = segment
          .replace(/[.+^${}()|\\]/g, "\\$&")
          .replace(/\*/g, "[^/]*")
          .replace(/\?/g, "[^/]");
        return last ? text : `${text}/`;
      })
      .join("");
    return new RegExp(`^${source}$`);
  };
  // The last pattern that matches a path decides, and a `!` pattern takes the path back out.
  const unaffected = (filePath, patterns) =>
    patterns.reduce((listed, pattern) => {
      const negated = pattern.startsWith("!");
      return globRegExp(negated ? pattern.slice(1) : pattern).test(filePath)
        ? !negated
        : listed;
    }, false);

  const config = JSON.parse(
    readFileSync(path.join(root, ".nexkit/config.json"), "utf8"),
  );
  // The suites each check runs. A check skips only when all of them would.
  const suitesOf = {
    smoke,
    integration: ["integration"],
    "e2e-1": ["playwright"],
    "e2e-2": ["playwright"],
    "e2e-3": ["playwright"],
  };
  const samples = [
    "README.md",
    "docs/delivery.md",
    "docs/decisions/d105.md",
    "samples/apps/purchase-requests/README.md",
    ".github/workflows/ci.yml",
    ".nexkit/config.json",
    "tests/e2e/specs/form.spec.ts",
    "tests/e2e/spec-durations.json",
    "web/src/App.tsx",
    "web/package.json",
    "web/package-lock.json",
    "web/keycloak-theme/login.ftl",
    "web/keycloak-theme/messages/messages_en.properties",
    "web/vite.config.ts",
    "web/vite.keycloak-theme.config.ts",
    "src/Axis.Server/Program.cs",
    "scripts/integration.sh",
    "scripts/lib/affected.mjs",
    "samples/apps/purchase-requests/application.json",
    "compose.yaml",
  ];

  test("only the checks that run suites with a safe list have one", () => {
    const listed = config.checks
      .filter((check) => check.unaffected_by)
      .map((check) => check.name);
    assert.deepEqual(listed.sort(), Object.keys(suitesOf).sort());
  });

  for (const [name, suites] of Object.entries(suitesOf)) {
    test(`${name} skips exactly the paths its suites' safe lists skip`, () => {
      const patterns = config.checks.find(
        (check) => check.name === name,
      ).unaffected_by;
      for (const filePath of samples) {
        const scripts = suites.every((suite) =>
          matches(filePath, safeSuites[suite]),
        );
        assert.equal(unaffected(filePath, patterns), scripts, filePath);
      }
    });
  }

  test("the integration tests read the Keycloak theme that their script builds from web/", () => {
    const theme = [
      "web/keycloak-theme/login.ftl",
      "web/keycloak-theme/login-otp.ftl",
      "web/keycloak-theme/login-config-totp.ftl",
      "web/vite.config.ts",
      "web/vite.keycloak-theme.config.ts",
    ];
    for (const filePath of theme) {
      assert.equal(matches(filePath, safeSuites.integration), false, filePath);
    }
    assert.equal(matches("web/src/App.tsx", safeSuites.integration), true);
  });
});

describe("matches", () => {
  const matchers = ["docs/", "Dockerfile", /\.md$/];

  test("matches a directory prefix, an exact path and a RegExp", () => {
    assert.equal(matches("docs/delivery.md", matchers), true);
    assert.equal(matches("Dockerfile", matchers), true);
    assert.equal(matches("samples/apps/x/README.md", matchers), true);
  });

  test("does not match a path that only shares a prefix or an exact name", () => {
    assert.equal(matches("docs-old/x.txt", matchers), false);
    assert.equal(matches("Dockerfile.dev", matchers), false);
  });
});

describe("changedPaths", () => {
  let dir;

  before(() => {
    dir = mkdtempSync(path.join(tmpdir(), "affected-"));
    const git = (...args) =>
      execFileSync("git", args, { cwd: dir, stdio: "pipe" });
    git("init", "-q");
    writeFileSync(path.join(dir, "README.md"), "# Sample\n");
    git("add", ".");
    git(
      "-c",
      "user.name=t",
      "-c",
      "user.email=t@example.com",
      "-c",
      "commit.gpgsign=false",
      "commit",
      "-q",
      "-m",
      "Add readme",
    );
  });

  after(() => rmSync(dir, { recursive: true, force: true }));

  test("lists untracked files, so a new file outside the safe list runs the suite", () => {
    assert.deepEqual(changedPaths("HEAD", dir), []);
    writeFileSync(path.join(dir, "compose.yaml"), "services: {}\n");

    assert.deepEqual(changedPaths("HEAD", dir), ["compose.yaml"]);
    assert.equal(
      decideSafe({
        base: "HEAD",
        matchers: safeSuites.compose,
        listChanged: (base) => changedPaths(base, dir),
      }).run,
      true,
    );
  });

  test("throws when the base commit does not exist", () => {
    assert.throws(() =>
      changedPaths("0000000000000000000000000000000000000000", dir),
    );
  });
});

describe("check scripts", () => {
  let dir;
  let env;

  const writeExecutable = (name, body) => {
    writeFileSync(path.join(dir, name), `#!/bin/sh\n${body}`);
    chmodSync(path.join(dir, name), 0o755);
  };

  const scenarioSuites = () =>
    ["samples/apps", "samples/templates"].flatMap((group) =>
      readdirSync(path.join(root, group))
        .map((folder) => path.join(root, group, folder, "application.json"))
        .filter((manifest) => existsSync(manifest))
        .map(
          (manifest) =>
            `scenarios-${JSON.parse(readFileSync(manifest, "utf8")).name}`,
        ),
    );

  const run = (script, changed, extra = {}) =>
    spawnSync("bash", [script], {
      cwd: root,
      encoding: "utf8",
      timeout: 60_000,
      env: { ...env, FAKE_CHANGED: changed, ...extra },
    });

  const dotnetCalls = () => {
    const log = env.FAKE_DOTNET_LOG;
    const calls = existsSync(log)
      ? readFileSync(log, "utf8").split("\n").filter(Boolean)
      : [];
    rmSync(log, { force: true });
    return calls;
  };

  const integrationCall = (calls) => {
    const matching = calls.filter((call) =>
      call.includes("--project tests/Axis.Integration.Tests"),
    );
    assert.equal(matching.length, 1, calls.join("\n"));
    return matching[0];
  };

  before(() => {
    dir = mkdtempSync(path.join(tmpdir(), "check-scripts-"));
    writeExecutable(
      "git",
      `[ "$1" = diff ] && printf '%s\\n' "$FAKE_CHANGED"\nexit 0\n`,
    );
    writeExecutable(
      "docker",
      `printf '%s\\n' "$*" >>"$FAKE_DOCKER_LOG"\nexit 1\n`,
    );
    // Logs each call and writes a passing JUnit file where the call asks for one.
    writeExecutable(
      "dotnet",
      `printf '%s\\n' "$*" >>"$FAKE_DOTNET_LOG"
dir=
file=
prev=
for arg in "$@"; do
  case "$prev" in
    --results-directory) dir="$arg" ;;
    --report-xunit-junit-filename) file="$arg" ;;
  esac
  prev="$arg"
done
if [ -n "$dir" ] && [ -n "$file" ]; then
  mkdir -p "$dir"
  printf '%s' '<testsuites><testsuite name="t" tests="1" failures="0"><testcase classname="SearchPerformanceTests" name="t" time="0.1"/></testsuite></testsuites>' >"$dir/$file"
fi
exit 0
`,
    );
    env = {
      ...process.env,
      PATH: `${dir}${path.delimiter}${process.env.PATH}`,
      NEXKIT_BASE_SHA: "abc",
      AXIS_TEST_RESULTS_DIR: path.join(dir, "results"),
      FAKE_DOCKER_LOG: path.join(dir, "docker-calls"),
      FAKE_DOTNET_LOG: path.join(dir, "dotnet-calls"),
    };
    // A run inside a NexKit check inherits its commits, which would decide in place of the fake base.
    delete env.NEXKIT_CHECK_VERIFIED_SHA;
    delete env.NEXKIT_VERIFIED_SHA;
    delete env.GITHUB_ACTIONS;
    delete env.GITHUB_STEP_SUMMARY;
  });

  after(() => rmSync(dir, { recursive: true, force: true }));

  test("integration.sh skips the integration tests and every scenario suite for a web/src change", () => {
    const result = run("scripts/integration.sh", "web/src/x.ts");

    assert.equal(result.status, 0, result.stdout + result.stderr);
    const suites = ["Axis.Integration.Tests", ...scenarioSuites()];
    assert.ok(scenarioSuites().length > 0);
    for (const suite of suites) {
      assert.ok(
        result.stdout.includes(
          `${suite} (skipped: not affected by this change)`,
        ),
        suite,
      );
    }
  });

  test("integration.sh skips for a web/src change since the check's own passed commit alone", () => {
    const result = run("scripts/integration.sh", "web/src/x.ts", {
      NEXKIT_BASE_SHA: "",
      NEXKIT_CHECK_VERIFIED_SHA: "def",
    });

    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.ok(
      result.stdout.includes(
        "Axis.Integration.Tests (skipped: not affected by this change)",
      ),
      result.stdout,
    );
    assert.match(result.stderr, /since def \(NEXKIT_CHECK_VERIFIED_SHA\)/);
  });

  test("integration.sh says which changed path makes it run in full", () => {
    const result = run("scripts/integration.sh", "src/Axis.Engine/Foo.cs", {
      NEXKIT_CHECK_VERIFIED_SHA: "def",
    });

    assert.match(
      result.stderr,
      /Running integration in full: src\/Axis\.Engine\/Foo\.cs changed\. Compared with def \(NEXKIT_CHECK_VERIFIED_SHA\)\./,
    );
    integrationCall(dotnetCalls());
  });

  test("integration.sh leaves the performance tests out under NexKit", () => {
    run("scripts/integration.sh", "src/Axis.Engine/Foo.cs");

    const call = integrationCall(dotnetCalls());
    assert.ok(call.includes("--filter-not-class *PerformanceTests"), call);
    assert.ok(!call.includes("--filter-class"), call);
  });

  test("integration.sh leaves the performance tests out without NexKit", () => {
    const result = run("scripts/integration.sh", "src/Axis.Engine/Foo.cs", {
      NEXKIT_BASE_SHA: "",
    });

    const call = integrationCall(dotnetCalls());
    assert.ok(call.includes("--filter-not-class *PerformanceTests"), call);
    assert.ok(!call.includes("--filter-class"), call);
    // The dev script's smoke test runs in scripts/smoke.sh.
    assert.doesNotMatch(result.stdout, /dev-script/);
  });

  test("performance.sh runs only the performance tests and reports them", () => {
    const result = run("scripts/performance.sh", "src/Axis.Engine/Foo.cs");

    assert.equal(result.status, 0, result.stdout + result.stderr);
    const call = integrationCall(dotnetCalls());
    assert.ok(call.includes("--filter-class *PerformanceTests"), call);
    assert.ok(
      call.includes("--report-xunit-junit-filename performance.xml"),
      call,
    );
    assert.match(result.stdout, /^\| performance \| 1 \| 0 \| 0 \| /m);
  });

  test("e2e.sh skips Playwright and starts no container for a docs change", () => {
    const result = run("scripts/e2e.sh", "docs/x.md");

    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.ok(
      result.stdout.includes(
        "playwright (skipped: not affected by this change)",
      ),
      result.stdout,
    );
    const calls = existsSync(env.FAKE_DOCKER_LOG)
      ? readFileSync(env.FAKE_DOCKER_LOG, "utf8")
      : "";
    assert.doesNotMatch(calls, /^run /m);
  });
});
