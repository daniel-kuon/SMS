// Dev proxy: the Angular dev server forwards API and browser-telemetry calls.
// Targets are injected by the Aspire AppHost (WithReference(api), OTLP_HTTP).
const api = process.env.API_HTTPS || process.env.API_HTTP;
if (!api) {
  throw new Error('API_HTTP(S) is not set. Start the app through the Aspire AppHost (src/Sms.AppHost).');
}
const otlp = process.env.OTLP_HTTP;
const tiles = process.env.TILES_HTTPS || process.env.TILES_HTTP;

module.exports = {
  '/api': { target: api, secure: false, changeOrigin: true, xfwd: true },
  // self-hosted base map (Martin, AppHost port 5161); xfwd so Martin's TileJSON points back through this proxy
  '/basemap': { target: process.env.BASEMAP_HTTP || 'http://localhost:5161', changeOrigin: true, xfwd: true },
  '/health': { target: api, secure: false, changeOrigin: true },
  ...(tiles ? { '/tiles': { target: tiles, secure: false, changeOrigin: true } } : {}),
  ...(otlp ? { '/otlp': { target: otlp, changeOrigin: true, pathRewrite: { '^/otlp': '' } } } : {}),
};
