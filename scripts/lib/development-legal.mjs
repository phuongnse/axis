// Prototype only (design review 2026-10-30-studio), not to build as is: gives the development realm's `axis` client
// the legal links that the realm sync is to write on each site's client (axis.privacyUrl, axis.termsUrl,
// axis.licencesUrl), so the sign-in pages show every link under scripts/dev.sh. Privacy and terms point at example.com,
// since the organization writes its own policy. Run it as
// `node scripts/lib/development-legal.mjs <keycloak url> <web url>` once Keycloak answers.
const [keycloak, web] = process.argv
  .slice(2)
  .map((value) => value.replace(/\/+$/, ""));

async function call(step, method, url, token, body) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  if (body && !(body instanceof URLSearchParams))
    headers["Content-Type"] = "application/json";
  const response = await fetch(url, {
    method,
    headers,
    body: body instanceof URLSearchParams ? body : body && JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok)
    throw new Error(`${step} failed with HTTP ${response.status}: ${text}`);
  return text === "" ? undefined : JSON.parse(text);
}

const form = new URLSearchParams({
  grant_type: "password",
  client_id: "admin-cli",
  username: "admin",
  password: "admin",
});
const { access_token: token } = await call(
  "get the admin token",
  "POST",
  `${keycloak}/realms/master/protocol/openid-connect/token`,
  undefined,
  form,
);
const admin = `${keycloak}/admin/realms/axis`;
const [client] = await call(
  "find the axis client",
  "GET",
  `${admin}/clients?clientId=axis`,
  token,
);
client.attributes = {
  ...client.attributes,
  "axis.privacyUrl": "https://example.com/privacy",
  "axis.termsUrl": "https://example.com/terms",
  "axis.licencesUrl": `${web}/licences`,
};
await call(
  "write the legal links",
  "PUT",
  `${admin}/clients/${client.id}`,
  token,
  client,
);
