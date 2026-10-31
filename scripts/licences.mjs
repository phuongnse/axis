#!/usr/bin/env node
// Proposed: writes web/public/licences.json, the data of the Open source licences page (/licences): Axis's own licence
// and, for every npm package the SPA ships and every NuGet package the server, the worker and the admin tool ship, its
// name, version, licence, address and licence text when the package carries one. The build is to run it, so the page
// always matches what ships. Run from the repository root.
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import path from "node:path";

const root = process.cwd();
const licenceFiles = /^(licen[cs]e|copying|notice)(\.(md|txt))?$/i;

function licenceText(folder) {
  if (!existsSync(folder)) {
    return null;
  }
  const file = readdirSync(folder).find((name) => licenceFiles.test(name));
  return file ? readFileSync(path.join(folder, file), "utf8").trim() : null;
}

function npmPackages() {
  const tree = JSON.parse(
    execFileSync(
      "npm",
      ["ls", "--prefix", "web", "--omit=dev", "--all", "--json"],
      {
        encoding: "utf8",
        maxBuffer: 64 * 1024 * 1024,
      },
    ),
  );
  const found = new Map();
  const visit = (dependencies) => {
    for (const [name, info] of Object.entries(dependencies ?? {})) {
      const key = `${name}@${info.version}`;
      if (!info.version || found.has(key)) {
        continue;
      }
      const folder = path.join(root, "web", "node_modules", name);
      const manifest = existsSync(path.join(folder, "package.json"))
        ? JSON.parse(readFileSync(path.join(folder, "package.json"), "utf8"))
        : {};
      const repository =
        typeof manifest.repository === "string"
          ? manifest.repository
          : manifest.repository?.url;
      found.set(key, {
        name,
        version: info.version,
        licence:
          typeof manifest.license === "string"
            ? manifest.license
            : (manifest.license?.type ?? "Unknown"),
        url:
          manifest.homepage ??
          repository?.replace(/^git\+/, "").replace(/\.git$/, "") ??
          null,
        text: licenceText(folder),
      });
      visit(info.dependencies);
    }
  };
  visit(tree.dependencies);
  return [...found.values()];
}

function nugetPackages() {
  const store = path.join(homedir(), ".nuget", "packages");
  const found = new Map();
  for (const project of ["Axis.Server", "Axis.Worker", "Axis.Admin"]) {
    const listing = JSON.parse(
      execFileSync(
        "dotnet",
        [
          "list",
          `src/${project}/${project}.csproj`,
          "package",
          "--include-transitive",
          "--format",
          "json",
        ],
        { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 },
      ),
    );
    for (const framework of listing.projects.flatMap(
      (entry) => entry.frameworks ?? [],
    )) {
      for (const item of [
        ...(framework.topLevelPackages ?? []),
        ...(framework.transitivePackages ?? []),
      ]) {
        const key = `${item.id}@${item.resolvedVersion}`;
        if (found.has(key)) {
          continue;
        }
        const folder = path.join(
          store,
          item.id.toLowerCase(),
          item.resolvedVersion.toLowerCase(),
        );
        const nuspecPath = path.join(folder, `${item.id.toLowerCase()}.nuspec`);
        const nuspec = existsSync(nuspecPath)
          ? readFileSync(nuspecPath, "utf8")
          : "";
        const expression = /<license type="expression">([^<]+)<\/license>/.exec(
          nuspec,
        )?.[1];
        const licenceFile = /<license type="file">([^<]+)<\/license>/.exec(
          nuspec,
        )?.[1];
        const licenceUrl = /<licenseUrl>([^<]+)<\/licenseUrl>/.exec(
          nuspec,
        )?.[1];
        const projectUrl = /<projectUrl>([^<]+)<\/projectUrl>/.exec(
          nuspec,
        )?.[1];
        const text =
          licenceFile && existsSync(path.join(folder, licenceFile))
            ? readFileSync(path.join(folder, licenceFile), "utf8").trim()
            : licenceText(folder);
        found.set(key, {
          name: item.id,
          version: item.resolvedVersion,
          licence:
            expression ??
            (licenceFile
              ? "See licence text"
              : licenceUrl
                ? "See licence address"
                : "Unknown"),
          url: projectUrl ?? licenceUrl ?? null,
          text,
        });
      }
    }
  }
  return [...found.values()];
}

const byName = (left, right) => left.name.localeCompare(right.name, "en");
const data = {
  axis: {
    name: "Axis",
    licence: "AGPL-3.0-only",
    url: "https://github.com/phuongnse/axis",
    licenceUrl: "https://github.com/phuongnse/axis/blob/main/LICENSE",
  },
  npm: npmPackages().sort(byName),
  nuget: nugetPackages().sort(byName),
};
writeFileSync(
  path.join(root, "web", "public", "licences.json"),
  `${JSON.stringify(data, null, 2)}\n`,
);
console.log(
  `licences.json: ${data.npm.length} npm and ${data.nuget.length} NuGet packages`,
);
