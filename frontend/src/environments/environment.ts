/**
 * Production settings.
 *
 * The API is a separate App Service, not a Static Web Apps linked backend, so this must be its
 * absolute origin. An empty value would make the browser request /api/* from the Static Web App,
 * where navigationFallback answers with index.html — a 200 carrying HTML, which surfaces as
 * "Could not load stories" rather than as an obvious 404.
 *
 * Changing this origin also means updating `connect-src` in staticwebapp.config.json, or the
 * CSP blocks the call before it is made.
 */
export const environment = {
  production: true,
  apiBaseUrl: 'https://hackernews-api-tm101-ehf9fshdg3cah9hu.westus3-01.azurewebsites.net',
};
