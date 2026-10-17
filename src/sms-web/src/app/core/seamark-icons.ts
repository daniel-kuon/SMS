import type { ExpressionSpecification, Map as MlMap } from 'maplibre-gl';

/**
 * Sea-mark icons built from the OpenSeaMap renderer symbols (public/seamarks/*.svg, CC BY-SA): the body shape is
 * coloured with the mark's colour bands, a topmark is stacked on top, lights/harbours use their own symbol.
 * Icons are composed on demand when MapLibre reports a missing image id (styleimagemissing).
 */
export interface SeamarkProps { type: string; category?: string | null; shape?: string | null; colour?: string | null; }
/** raw: the symbol file is drawn as it is (OpenSeaMap's magenta glyphs for harbours, facilities, landmarks) at `size` px instead of a recoloured body path. */
export interface IconSpec { body: string; colours: string[]; vertical?: boolean; top?: string; topColour?: string; fixed?: boolean; raw?: boolean; size?: number; }

const COLOURS: Record<string, string> = { red: '#d32f2f', green: '#2e7d32', yellow: '#fbc02d', black: '#212121', white: '#ffffff', orange: '#ef6c00', blue: '#1565c0', grey: '#9e9e9e', brown: '#6d4c41', amber: '#ffb300', violet: '#8e24aa', magenta: '#d81b60' };
const BODY_BY_SHAPE: Record<string, string> = { can: 'Can', cylindrical: 'Can', conical: 'Cone', cone: 'Cone', spherical: 'Sphere', sphere: 'Sphere', pillar: 'Pillar', spar: 'Spar', barrel: 'Barrel', super: 'Super', 'super-buoy': 'Super', float: 'Float', stake: 'Stake', pole: 'Stake', post: 'Post', pile: 'Post', tower: 'Tower', lattice: 'Beacon', perch: 'Beacon', withy: 'Beacon' };
const FACILITY: Record<string, string> = { toilets: 'Toilet', fuel_station: 'Fuel', shower: 'Shower', laundrette: 'Laundrette', pump_out: 'Pump_Out', slipway: 'Slipway', boat_hoist: 'Crane', boatyard: 'Boatyard', chandler: 'Chandler', visitor_berth: 'Visitor_Berth', visitors_berth: 'Visitor_Berth', visitor_buoy: 'Visitor_Mooring', sailing_club: 'SailingClub', harbour_master: 'HarbourMaster', customs: 'Customs', hospital: 'Hospital', landing_steps: 'LandingSteps', boat_storage: 'Storage', crane: 'Crane', pump_out_station: 'Pump_Out' };
const LANDMARK: Record<string, string> = { church: 'Church', tower: 'Tower', lighthouse: 'Light_House', chimney: 'Chimney', mast: 'Mast', radio_mast: 'RadioMast', water_tower: 'WaterTower', windmill: 'Windmill', wind_motor: 'Wind_Motor', windmotor: 'Wind_Motor', flagstaff: 'Flagstaff', flagpole: 'Flagstaff', monument: 'Monument', dome: 'Dome', spire: 'Spire', minaret: 'Minaret', cairn: 'Cairn', cross: 'Cross', dish_aerial: 'DishAerial', radar_station: 'Radar_Station', statue: 'Monument' };
const CARDINAL: Record<string, { colours: string[]; top: string }> = {
  north: { colours: ['black', 'yellow'], top: 'Top_North' }, east: { colours: ['black', 'yellow', 'black'], top: 'Top_East' },
  south: { colours: ['yellow', 'black'], top: 'Top_South' }, west: { colours: ['yellow', 'black', 'yellow'], top: 'Top_West' },
};

/** Which symbol(s) and colours a mark gets; deterministic so the same id always yields the same icon. */
export function iconSpec(p: SeamarkProps): IconSpec {
  const colours = (p.colour ?? '').split(/[;,]/).map(c => c.trim().toLowerCase()).filter(c => c in COLOURS);
  const shape = (p.shape ?? '').toLowerCase();
  const cat = (p.category ?? '').toLowerCase();
  const isBeacon = p.type.startsWith('beacon_');
  const body = (s: string, fallback: string) => BODY_BY_SHAPE[s] ?? fallback;
  switch (p.type) {
    case 'buoy_lateral': case 'beacon_lateral': {
      const port = cat === 'port' || cat === 'preferred_channel_starboard';
      if (isBeacon && shape === 'perch') return { body: port ? 'Perch_Port' : 'Perch_Starboard', colours: [port ? 'red' : 'green'] };
      if (isBeacon && shape === 'withy') return { body: port ? 'Withy_Port' : 'Withy_Starboard', colours: [port ? 'red' : 'green'] };
      const def = colours.length ? colours : cat === 'preferred_channel_starboard' ? ['red', 'green', 'red'] : cat === 'preferred_channel_port' ? ['green', 'red', 'green'] : [port ? 'red' : 'green'];
      return { body: body(shape, isBeacon ? 'Beacon' : port ? 'Can' : 'Cone'), colours: def };
    }
    case 'buoy_cardinal': case 'beacon_cardinal': {
      const c = CARDINAL[cat] ?? CARDINAL['north'];
      return { body: body(shape, isBeacon ? 'Beacon' : 'Pillar'), colours: colours.length ? colours : c.colours, top: c.top };
    }
    case 'buoy_isolated_danger': case 'beacon_isolated_danger':
      return { body: body(shape, isBeacon ? 'Beacon' : 'Pillar'), colours: colours.length ? colours : ['black', 'red', 'black'], top: 'Top_Isol' };
    case 'buoy_safe_water': case 'beacon_safe_water':
      return { body: body(shape, isBeacon ? 'Beacon' : 'Sphere'), colours: colours.length ? colours : ['red', 'white'], vertical: true, top: 'Top_Sphere', topColour: 'red' };
    case 'buoy_special_purpose': case 'beacon_special_purpose':
      return { body: body(shape, isBeacon ? 'Beacon' : 'Spar'), colours: colours.length ? colours : ['yellow'], top: 'Top_X', topColour: 'yellow' };
    case 'mooring': return cat === 'dolphin' ? { body: 'Dolphin', colours: [], raw: true, size: 20 } : cat === 'deviation_dolphin' ? { body: 'DeviationDolphin', colours: [], raw: true, size: 20 } : cat === 'bollard' ? { body: 'Bollard', colours: [], raw: true, size: 20 } : cat === 'pile' || cat === 'post' ? { body: 'Post', colours: ['grey'] } : { body: 'Visitor_Mooring', colours: [], raw: true, size: 22 };
    case 'light_major': return { body: 'Light_Major', colours: [], fixed: true };
    case 'light_minor': case 'light': case 'light_float': case 'light_vessel': return { body: 'Light_Minor', colours: [], fixed: true };
    case 'landmark': return { body: LANDMARK[cat] ?? 'LandTower', colours: [], raw: true, size: 26 };
    case 'harbour': return { body: cat === 'marina' || cat === 'yacht' ? 'Marina' : cat === 'marina_no_facilities' ? 'MarinaNF' : cat === 'fishing' ? 'Fishing_Harbour' : 'Harbour', colours: [], raw: true, size: 28 };
    case 'anchorage': return { body: 'Anchorage', colours: [], raw: true, size: 24 };
    case 'anchor_berth': return { body: 'Anchor_Berth', colours: [], raw: true, size: 24 };
    case 'berth': return { body: 'Berth', colours: [], raw: true, size: 22 };
    case 'rock': return { body: cat === 'awash' ? 'RockA' : cat === 'covers' ? 'RockC' : 'Rock', colours: [], raw: true, size: 22 };
    case 'wreck': return { body: cat === 'dangerous' ? 'WreckD' : cat === 'distributed_remains' ? 'WreckS' : 'WreckND', colours: [], raw: true, size: 24 };
    case 'fog_signal': return { body: 'Fog_Signal', colours: [], raw: true, size: 22 };
    case 'gauge': return { body: 'Tide_Gauge', colours: [], raw: true, size: 22 };
    case 'gate': return { body: 'Lock_Gate', colours: [], raw: true, size: 22 };
    case 'lock_basin': return { body: 'Lock', colours: [], raw: true, size: 24 };
    case 'platform': return { body: 'Platform', colours: [], raw: true, size: 24 };
    case 'pilot_boarding': return { body: 'Pilot', colours: [], raw: true, size: 24 };
    case 'rescue_station': return { body: 'Rescue', colours: [], raw: true, size: 24 };
    case 'signal_station_traffic': case 'signal_station_warning': case 'coastguard_station': return { body: 'Signal_Station', colours: [], raw: true, size: 24 };
    case 'small_craft_facility': return { body: FACILITY[cat.split(';')[0]] ?? 'Marina', colours: [], raw: true, size: 20 };
    case 'cable_submarine': case 'cable_overhead': return { body: 'Cable', colours: [], raw: true, size: 20 };
    case 'pipeline_submarine': case 'pipeline_overhead': return { body: 'Pipeline', colours: [], raw: true, size: 20 };
    case 'turning_basin': return { body: 'Turning_Point', colours: [], raw: true, size: 24 };
    case 'radar_station': case 'radar_transponder': case 'radar_reflector': return { body: 'Radar_Station', colours: [], raw: true, size: 22 };
    case 'restricted_area': return { body: 'Restricted', colours: [], fixed: true };
    case 'notice': return { body: 'Notice', colours: [], fixed: true };
    default: return { body: body(shape, 'Post'), colours: colours.length ? colours : ['grey'] };
  }
}

/** Image id the symbol layer asks for; parsed back by the missing-image handler. */
export const seamarkIconExpression: ExpressionSpecification = ['concat', 'sm|', ['get', 'type'], '|', ['coalesce', ['get', 'category'], ''], '|', ['coalesce', ['get', 'shape'], ''], '|', ['coalesce', ['get', 'colour'], '']];
export function parseIconId(id: string): SeamarkProps | null {
  const p = id.split('|');
  return p.length === 5 && p[0] === 'sm' ? { type: p[1], category: p[2] || null, shape: p[3] || null, colour: p[4] || null } : null;
}

export interface Box { minX: number; minY: number; maxX: number; maxY: number; }

/** Bounding box of an SVG path (absolute and relative M/L/H/V/C/S/Q/T/A/Z; control points included). */
export function pathBox(d: string): Box {
  const tokens = d.match(/[a-zA-Z]|-?\d*\.?\d+(?:e[-+]?\d+)?/g) ?? [];
  let cmd = 'M'; let x = 0, y = 0, sx = 0, sy = 0;
  const box: Box = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity };
  const add = (px: number, py: number) => { box.minX = Math.min(box.minX, px); box.maxX = Math.max(box.maxX, px); box.minY = Math.min(box.minY, py); box.maxY = Math.max(box.maxY, py); };
  let i = 0;
  const num = () => Number(tokens[i++]);
  while (i < tokens.length) {
    const t = tokens[i];
    if (/[a-zA-Z]/.test(t)) { cmd = t; i++; if (cmd === 'Z' || cmd === 'z') { x = sx; y = sy; continue; } }
    const rel = cmd === cmd.toLowerCase();
    switch (cmd.toUpperCase()) {
      case 'M': { const nx = num(), ny = num(); x = rel ? x + nx : nx; y = rel ? y + ny : ny; sx = x; sy = y; add(x, y); cmd = rel ? 'l' : 'L'; break; }
      case 'L': case 'T': { const nx = num(), ny = num(); x = rel ? x + nx : nx; y = rel ? y + ny : ny; add(x, y); break; }
      case 'H': { const nx = num(); x = rel ? x + nx : nx; add(x, y); break; }
      case 'V': { const ny = num(); y = rel ? y + ny : ny; add(x, y); break; }
      case 'C': { for (let k = 0; k < 3; k++) { const nx = num(), ny = num(); const px = rel ? x + nx : nx, py = rel ? y + ny : ny; add(px, py); if (k === 2) { x = px; y = py; } } break; }
      case 'S': case 'Q': { for (let k = 0; k < 2; k++) { const nx = num(), ny = num(); const px = rel ? x + nx : nx, py = rel ? y + ny : ny; add(px, py); if (k === 1) { x = px; y = py; } } break; }
      case 'A': { i += 5; const nx = num(), ny = num(); x = rel ? x + nx : nx; y = rel ? y + ny : ny; add(x, y); break; }
      default: i++;
    }
  }
  return box;
}

const svgCache = new Map<string, Promise<string>>();
/** Whole symbol file, the <symbol> wrapper opened up so it renders on its own; cached per name. */
export function symbolSvg(name: string, fetcher: (url: string) => Promise<string> = url => fetch(url).then(r => r.text())): Promise<string> {
  if (!svgCache.has(name)) svgCache.set(name, fetcher(`/seamarks/${name}.svg`));
  return svgCache.get(name)!;
}
/** Draws a symbol file as it is (its own colours and frame) in a size×size icon; the file's 60×60 canvas is scaled. */
export function composeRawSvg(svg: string, size: number): string {
  const inner = svg.replace(/<\?xml[^>]*>/, '').replace(/<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '')
    .replace(/<symbol[^>]*>/g, '<g>').replace(/<\/symbol>/g, '</g>').replace(/<use[^>]*\/>/g, '');
  const canvas = /width="(\d+)"/.exec(svg)?.[1] ?? '60';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${canvas} ${canvas}">${inner}</svg>`;
}

const symbolCache = new Map<string, Promise<string>>();
/** Path data of a symbol file (first <path d="…">); cached per name. */
export function symbolPath(name: string, fetcher: (url: string) => Promise<string> = url => fetch(url).then(r => r.text())): Promise<string> {
  if (!symbolCache.has(name)) symbolCache.set(name, fetcher(`/seamarks/${name}.svg`).then(svg => /\bd="([^"]+)"/.exec(svg)?.[1] ?? ''));
  return symbolCache.get(name)!;
}

export const ICON_W = 36, ICON_H = 48;

/** Composes the icon SVG: body cropped to its path box and scaled to fit, colour bands via clip path, topmark above. */
export function composeSvg(spec: IconSpec, bodyPath: string, topPath: string | null): string {
  const b = pathBox(bodyPath);
  const bw = Math.max(1, b.maxX - b.minX), bh = Math.max(1, b.maxY - b.minY);
  const topH = topPath ? 16 : 0;
  const scale = Math.min((ICON_W - 4) / bw, (ICON_H - 4 - topH) / bh);
  const w = bw * scale, h = bh * scale;
  const ox = (ICON_W - w) / 2, oy = ICON_H - 2 - h;
  const colours = spec.colours.map(c => COLOURS[c] ?? c);
  let bodySvg: string;
  if (spec.fixed || colours.length === 0) bodySvg = `<path d="${bodyPath}" fill="${spec.fixed ? '#212121' : '#9e9e9e'}"/>`;
  else if (colours.length === 1) bodySvg = `<path d="${bodyPath}" fill="${colours[0]}" stroke="#212121" stroke-width="${1 / scale}"/>`;
  else {
    const n = colours.length;
    const bands = colours.map((c, k) => spec.vertical
      ? `<rect x="${b.minX + bw * k / n}" y="${b.minY}" width="${bw / n + 0.5}" height="${bh}" fill="${c}"/>`
      : `<rect x="${b.minX}" y="${b.minY + bh * k / n}" width="${bw}" height="${bh / n + 0.5}" fill="${c}"/>`).join('');
    bodySvg = `<clipPath id="c"><path d="${bodyPath}"/></clipPath><g clip-path="url(#c)">${bands}</g><path d="${bodyPath}" fill="none" stroke="#212121" stroke-width="${1 / scale}"/>`;
  }
  let topSvg = '';
  if (topPath) {
    const t = pathBox(topPath); const tw = Math.max(1, t.maxX - t.minX), th = Math.max(1, t.maxY - t.minY);
    const ts = Math.min(14 / tw, topH / th);
    topSvg = `<g transform="translate(${ICON_W / 2 - tw * ts / 2} ${oy - th * ts}) scale(${ts}) translate(${-t.minX} ${-t.minY})"><path d="${topPath}" fill="${COLOURS[spec.topColour ?? 'black']}"/></g>`;
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${ICON_W}" height="${ICON_H}" viewBox="0 0 ${ICON_W} ${ICON_H}"><g transform="translate(${ox} ${oy}) scale(${scale}) translate(${-b.minX} ${-b.minY})">${bodySvg}</g>${topSvg}</svg>`;
}

/** Hooks the on-demand icon generation into a map (call once per map). */
export function registerSeamarkIcons(map: MlMap): void {
  const pending = new Set<string>();
  map.on('styleimagemissing', async e => {
    const props = parseIconId(e.id);
    if (!props || pending.has(e.id)) return;
    pending.add(e.id);
    if (!map.hasImage(e.id)) map.addImage(e.id, { width: 1, height: 1, data: new Uint8Array(4) }); // placeholder now, real icon when composed (keeps MapLibre quiet)
    try {
      const spec = iconSpec(props);
      const size = spec.size ?? 24;
      const svg = spec.raw ? composeRawSvg(await symbolSvg(spec.body), size) : composeSvg(spec, (await symbolPath(spec.body)) || 'M0 0 h10 v10 h-10 z', spec.top ? await symbolPath(spec.top) : null);
      const img = spec.raw ? new Image(size * 2, size * 2) : new Image(ICON_W * 2, ICON_H * 2);
      await new Promise<void>((res, rej) => { img.onload = () => res(); img.onerror = () => rej(new Error('icon')); img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg); });
      if (map.hasImage(e.id)) map.removeImage(e.id);
      map.addImage(e.id, img, { pixelRatio: 2 });
    } catch { /* leave the mark without icon */ } finally { pending.delete(e.id); }
  });
}

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
