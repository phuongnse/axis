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
  isSafe,
  readConfig,
  safeLists,
  sinceCommit,
  suiteScripts,
  unaffected,
} from "./lib/affected.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

const unreachable = () =>
  assert.fail("The changed paths should not be listed.");
const smoke = ["dev-script", "compose", "images", "onprem"];

describe("decideSafe", () => {
  const runs = (name, paths, base = "abc") =>
    decideSafe({
      base,
      lists: safeLists(name, readConfig()),
      listChanged: () => paths,
    }).run;

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

  test("runs integration when a change touches the Keycloak theme or the Vite configs its script builds it with", () => {
    for (const filePath of [
      "web/keycloak-theme/login.ftl",
      "web/keycloak-theme/login-otp.ftl",
      "web/keycloak-theme/login-config-totp.ftl",
      "web/vite.config.ts",
      "web/vite.keycloak-theme.config.ts",
    ]) {
      assert.equal(runs("integration", [filePath]), true, filePath);
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
    assert.ok(smoke.every((name) => Object.hasOwn(suiteScripts, name)));
    for (const name of Object.keys(suiteScripts)) {
      const lists = safeLists(name, readConfig());
      assert.equal(
        decideSafe({ base: undefined, lists, listChanged: unreachable }).run,
        true,
      );
      assert.equal(
        decideSafe({ base: "", lists, listChanged: unreachable }).run,
        true,
      );
      assert.equal(
        decideSafe({ base: "abc", lists, listChanged: listError }).run,
        true,
      );
      assert.equal(
        decideSafe({ base: "abc", lists, listChanged: () => [] }).run,
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
    for (const name of Object.keys(suiteScripts))
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

describe("unaffected", () => {
  test("a ** glob crosses folders", () => {
    assert.equal(unaffected("README.md", ["**/*.md"]), true);
    assert.equal(unaffected("samples/apps/x/README.md", ["**/*.md"]), true);
    assert.equal(unaffected("docs/a/b.md", ["docs/**"]), true);
  });

  test("a glob does not match a path that only shares a prefix", () => {
    assert.equal(unaffected("docs-old/x.txt", ["docs/**"]), false);
    assert.equal(unaffected("docs/a/b.md", ["docs/*"]), false);
  });

  test("a later ! pattern takes a path back out", () => {
    const patterns = ["web/**", "!web/package.json"];
    assert.equal(unaffected("web/src/App.tsx", patterns), true);
    assert.equal(unaffected("web/package.json", patterns), false);
  });

  test("a path in no list is not unaffected", () => {
    assert.equal(
      unaffected("src/Axis.Server/Program.cs", ["docs/**", "**/*.md"]),
      false,
    );
  });
});

describe("safeLists", () => {
  const config = {
    checks: [
      { name: "build", run: "scripts/build.sh" },
      {
        name: "e2e-1",
        run: "AXIS_E2E_SHARD=1/3 scripts/e2e.sh",
        unaffected_by: ["docs/**", "web/**"],
      },
      {
        name: "e2e-2",
        run: "AXIS_E2E_SHARD=2/3 scripts/e2e.sh",
        unaffected_by: ["docs/**"],
      },
      { name: "integration", run: "scripts/integration.sh" },
    ],
  };

  test("lists the unaffected_by of each check that runs the suite's script", () => {
    assert.deepEqual(safeLists("playwright", config), [
      ["docs/**", "web/**"],
      ["docs/**"],
    ]);
  });

  test("skips only the paths every listing check lists", () => {
    const lists = safeLists("playwright", config);
    assert.equal(isSafe("docs/x.md", lists), true);
    assert.equal(isSafe("web/x.ts", lists), false);
  });

  test("a check without unaffected_by makes nothing safe", () => {
    const lists = safeLists("integration", config);
    assert.deepEqual(lists, [[]]);
    assert.equal(isSafe("docs/x.md", lists), false);
  });

  test("a suite that no check runs has no list and runs", () => {
    const lists = safeLists("compose", config);
    assert.deepEqual(lists, []);
    assert.equal(isSafe("docs/x.md", lists), false);
    assert.equal(
      decideSafe({ base: "abc", lists, listChanged: () => ["docs/x.md"] }).run,
      true,
    );
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
        lists: safeLists("compose", readConfig()),
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
    ["samples/apps"].flatMap((group) =>
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
    delete env.NEXKIT_SHARD;
  });

  after(() => rmSync(dir, { recursive: true, force: true }));

  test("integration.sh skips the integration tests and every scenario suite for a web/src change", () => {
    const result = run("scripts/integration.sh", "web/src/x.ts");

    assert.equal(result.status, 0, result.stdout + result.stderr);
    const suites = [
      "Axis.Integration.Tests",
      "Axis.ControlPlane.Integration.Tests",
      ...scenarioSuites(),
    ];
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

  test("integration.sh runs one shard's classes, and only shard 1 runs the control plane suite", () => {
    const shards = ["1/3", "2/3", "3/3"].map((shard) => {
      const result = run("scripts/integration.sh", "src/Axis.Engine/Foo.cs", {
        AXIS_INTEGRATION_SHARD: shard,
      });
      const calls = dotnetCalls();
      return { result, calls, call: integrationCall(calls) };
    });

    assert.ok(shards[0].call.includes("--filter-class Axis.Integration.Tests."), shards[0].call);
    assert.ok(!shards[0].call.includes("--filter-not-class"), shards[0].call);
    assert.ok(shards[1].call.includes("--filter-class Axis.Integration.Tests."), shards[1].call);
    assert.ok(shards[2].call.includes("--filter-not-class *PerformanceTests"), shards[2].call);
    assert.ok(!shards[2].call.includes("--filter-class"), shards[2].call);
    const controlPlane = shards.map(({ calls }) =>
      calls.some((call) => call.includes("Axis.ControlPlane.Integration.Tests")),
    );
    assert.deepEqual(controlPlane, [true, false, false]);
    // Each application's scenario suite is expected in exactly one shard.
    for (const suite of scenarioSuites()) {
      const expecting = shards.filter(({ result }) => result.stdout.includes(suite));
      assert.equal(expecting.length, 1, suite);
    }
  });

  test("integration.sh reads NEXKIT_SHARD as its shard, the same as AXIS_INTEGRATION_SHARD", () => {
    const shardCall = (variable) => {
      run("scripts/integration.sh", "src/Axis.Engine/Foo.cs", {
        [variable]: "2/3",
      });
      return integrationCall(dotnetCalls());
    };

    const call = shardCall("NEXKIT_SHARD");
    assert.ok(call.includes("--filter-class Axis.Integration.Tests."), call);
    assert.equal(call, shardCall("AXIS_INTEGRATION_SHARD"));
  });

  test("integration.sh refuses a malformed shard and a shard with arguments", () => {
    const malformed = run("scripts/integration.sh", "src/Axis.Engine/Foo.cs", {
      AXIS_INTEGRATION_SHARD: "4/3",
    });
    assert.equal(malformed.status, 2);
    assert.match(malformed.stderr, /AXIS_INTEGRATION_SHARD must be a shard such as 1\/3, not '4\/3'/);

    const withArguments = spawnSync(
      "bash",
      ["scripts/integration.sh", "--filter-class", "*StudioTests"],
      {
        cwd: root,
        encoding: "utf8",
        timeout: 60_000,
        env: { ...env, FAKE_CHANGED: "src/x.cs", AXIS_INTEGRATION_SHARD: "1/3" },
      },
    );
    assert.equal(withArguments.status, 2);
    assert.match(withArguments.stderr, /cannot be combined with arguments/);
    assert.deepEqual(dotnetCalls(), []);
  });

  test("integration.sh skips only the suites of its own shard for a web/src change", () => {
    const result = run("scripts/integration.sh", "web/src/x.ts", {
      AXIS_INTEGRATION_SHARD: "2/3",
    });

    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.ok(
      result.stdout.includes("Axis.Integration.Tests (skipped: not affected by this change)"),
      result.stdout,
    );
    assert.ok(!result.stdout.includes("Axis.ControlPlane.Integration.Tests"), result.stdout);
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
