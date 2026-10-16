// Dev proxy: the Angular dev server forwards API and browser-telemetry calls.
// Targets are injected by the Aspire AppHost (WithReference(api), OTLP_HTTP).
const api = process.env.API_HTTPS || process.env.API_HTTP;
if (!api) {
  throw new Error('API_HTTP(S) is not set. Start the app through the Aspire AppHost (src/Sms.AppHost).');
}
const otlp = process.env.OTLP_HTTP;

module.exports = {
  '/api': { target: api, secure: false, changeOrigin: true },
  '/health': { target: api, secure: false, changeOrigin: true },
  ...(otlp ? { '/otlp': { target: otlp, changeOrigin: true, pathRewrite: { '^/otlp': '' } } } : {}),
};
