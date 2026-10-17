/** Helpers for sea-mark popups: chart position format and readable type labels (the symbols themselves come from chart-symbols.ts). */

/** "54°22.345' N 010°08.912' E" – the degrees/decimal-minutes form used on charts and GPS devices. */
export function formatPosition(lat: number, lng: number): string {
  const f = (v: number, pos: string, neg: string, degWidth: number) => {
    const a = Math.abs(v); const deg = Math.floor(a); const min = (a - deg) * 60;
    return `${String(deg).padStart(degWidth, '0')}°${min.toFixed(3).padStart(6, '0')}' ${v >= 0 ? pos : neg}`;
  };
  return `${f(lat, 'N', 'S', 2)} ${f(lng, 'E', 'W', 3)}`;
}

/** Readable labels for the OSM seamark types. */
export function seamarkTypeLabel(type: string, lang: string): string {
  const de: Record<string, string> = { buoy_lateral: 'Seitentonne', beacon_lateral: 'Seitenbake', buoy_cardinal: 'Kardinaltonne', beacon_cardinal: 'Kardinalbake', buoy_isolated_danger: 'Einzelgefahrentonne', buoy_safe_water: 'Ansteuerungstonne', buoy_special_purpose: 'Sonderzeichen', beacon_special_purpose: 'Sonderbake', light_major: 'Leuchtfeuer', light_minor: 'Leuchtfeuer (klein)', light: 'Licht', harbour: 'Hafen', anchorage: 'Ankerplatz', mooring: 'Festmachetonne', rock: 'Felsen', wreck: 'Wrack', landmark: 'Landmarke', fog_signal: 'Nebelsignal', gauge: 'Pegel', gate: 'Schleusentor', platform: 'Plattform', pilot_boarding: 'Lotsenversetzposition', rescue_station: 'Rettungsstation', restricted_area: 'Sperrgebiet', notice: 'Schifffahrtszeichen' };
  const en: Record<string, string> = { buoy_lateral: 'Lateral buoy', beacon_lateral: 'Lateral beacon', buoy_cardinal: 'Cardinal buoy', beacon_cardinal: 'Cardinal beacon', buoy_isolated_danger: 'Isolated danger buoy', buoy_safe_water: 'Safe water buoy', buoy_special_purpose: 'Special mark', beacon_special_purpose: 'Special beacon', light_major: 'Major light', light_minor: 'Minor light', light: 'Light', harbour: 'Harbour', anchorage: 'Anchorage', mooring: 'Mooring buoy', rock: 'Rock', wreck: 'Wreck', landmark: 'Landmark', fog_signal: 'Fog signal', gauge: 'Tide gauge', gate: 'Lock gate', platform: 'Platform', pilot_boarding: 'Pilot boarding', rescue_station: 'Rescue station', restricted_area: 'Restricted area', notice: 'Notice mark' };
  return (lang === 'en' ? en : de)[type] ?? type.replace(/_/g, ' ');
}
