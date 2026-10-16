import * as L from 'leaflet';
import 'leaflet.markercluster';

// Leaflet resolves its default marker images relative to the CSS file; with bundled styles that breaks,
// so point it at the copied assets (angular.json → /leaflet/).
L.Icon.Default.mergeOptions({ imagePath: '/leaflet/' });

export const markerCluster = (options?: object): L.MarkerClusterGroup =>
  (L as unknown as { markerClusterGroup: (o?: object) => L.MarkerClusterGroup }).markerClusterGroup(options);

export function baseLayers(map: L.Map): void {
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '© OpenStreetMap-Mitwirkende' }).addTo(map);
  L.tileLayer('https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png', { maxZoom: 18, attribution: 'OpenSeaMap' }).addTo(map);
}
