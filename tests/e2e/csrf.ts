/**
 * The header every API write must carry (D23). Specs pass it on their own API calls only, so the
 * browser's requests prove that the SPA sends it itself.
 */
export const csrfHeaders = { 'X-Axis-Csrf': '1' }
