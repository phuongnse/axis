# Keycloak files for tenant realms

`password-blacklists/axis-breached-passwords.txt` is the breached-password list that every realm made by
`tenant create` checks new passwords against. Mount the `password-blacklists` folder at
`/opt/keycloak/data/password-blacklists/` in every Keycloak that `tenant create` targets. Without the file, `tenant
create` fails at its realm step.

Realms made by `tenant create` name the login theme `axis`, which `npm run build:keycloak-theme --prefix web` builds into
`artifacts/keycloak-theme`. Mount it at `/opt/keycloak/themes/`. Without it, Keycloak shows its own sign-in page.

The on-premises Compose example in `deploy/compose/` mounts both: the password list from this folder and the theme
from `AXIS_IDENTITY_THEME_DIR`, see [on premises](../../docs/reference/installation.md#on-premises). The Helm chart
mounts both from the volumes `identity.passwordBlacklists.volume` and `identity.theme.volume`, see
[in the cloud](../../docs/reference/installation.md#in-the-cloud).

The list comes from the top 1,000,000 passwords of the Xato 10 million password list in
[SecLists](https://github.com/danielmiessler/SecLists/blob/master/Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt)
(MIT licence). It keeps only the entries of 12 to 128 characters, because the realm's length rule refuses shorter
passwords anyway. The entries are lowercase, sorted, one per line, with no duplicates, because Keycloak lowercases a
password before it checks the list.
