const targets = [
  ['web', process.env.E2E_BASE_URL],
  ['api', process.env.E2E_API_URL ? `${process.env.E2E_API_URL}/health` : undefined],
].filter((target) => target[1]);

const timeoutMs = Number(process.env.E2E_TARGET_TIMEOUT_MS ?? 120_000);
const intervalMs = 1_000;
const deadline = Date.now() + timeoutMs;

function sleep(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

async function waitForTarget(name, url) {
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { redirect: 'manual' });
      if (response.status < 500) {
        console.log(`e2e target ready: ${name} (${url})`);
        return;
      }
    } catch {
      // Keep probing until the compose dependency has finished booting.
    }

    await sleep(intervalMs);
  }

  throw new Error(`Timed out waiting for e2e target: ${name} (${url})`);
}

await Promise.all(targets.map(([name, url]) => waitForTarget(name, url)));
