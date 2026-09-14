/**
 * Production settings. An empty base URL means the API is reached on the same origin
 * (Azure Static Web Apps with a linked backend), so no rebuild is needed to change host.
 */
export const environment = {
  production: true,
  apiBaseUrl: '',
};
