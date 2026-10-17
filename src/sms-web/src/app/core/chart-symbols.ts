import type { ExpressionSpecification } from 'maplibre-gl';

/**
 * Sea-mark symbols from the "freenauticalchart" sprite (quantenschaum/mapping, served by Open Waters Seamap, CC-BY 4.0;
 * public/seamarks/fnc.*). Names follow <shape>/<pattern>/<colours> with colours joined by "_" ("pillar/horizontal/red_white").
 * The expressions below pick the sprite name from our tile properties (type, category, shape, colour, colour_pattern …).
 */
const S = 'fnc:';
const colours: ExpressionSpecification = ['coalesce', ['get', 'colour_key'], 'generic']; // "red;white" → "red_white", joined by the tile server (expressions cannot replace text)
const buoyShape: ExpressionSpecification = ['case',
  ['in', ['get', 'shape'], ['literal', ['pole', 'perch', 'post']]], 'stake',
  ['in', ['get', 'shape'], ['literal', ['buoyant', 'lattice']]], 'pile',
  ['in', ['get', 'shape'], ['literal', ['ice-buoy', 'ice_buoy']]], 'super-buoy',
  ['==', ['get', 'shape'], 'cone'], 'conical',
  ['==', ['get', 'shape'], 'sphere'], 'spherical',
  ['==', ['get', 'shape'], 'cylindrical'], 'can',
  ['!', ['has', 'shape']], ['case', ['in', 'beacon', ['get', 'type']], 'stake', ['in', ['get', 'type'], ['literal', ['light_float', 'light_vessel']]], 'super-buoy', 'pillar'],
  ['get', 'shape']];
const buoyIcon: ExpressionSpecification = ['coalesce',
  ['image', ['case', ['all', ['==', ['get', 'shape'], 'withy'], ['==', ['get', 'category'], 'port']], S + 'withy-port', ['==', ['get', 'shape'], 'withy'], S + 'withy-starboard', '']],
  ['image', ['case', ['all', ['has', 'colour_pattern'], ['!=', ['get', 'colour_pattern'], 'no']], ['concat', S, buoyShape, '/', ['get', 'colour_pattern'], '/', colours], ['concat', S, buoyShape, '/', colours]]],
  ['image', ['concat', S, buoyShape, '/', ['coalesce', ['get', 'colour'], 'generic']]],
  ['image', ['concat', S, buoyShape, '/generic']],
  ['image', ['case', ['in', 'beacon', ['get', 'type']], S + 'stake/generic', S + 'pillar/generic']]];

const landmark: ExpressionSpecification = ['concat', S, ['case', ['==', ['get', 'conspicuous'], true], 'convis/', ''],
  ['case', ['in', ['get', 'function'], ['literal', ['church', 'chapel']]], 'church',
    ['match', ['coalesce', ['get', 'category'], ''], 'cairn', 'cairn-lmk', 'chimney', 'chimney', 'column', 'column', 'cross', 'cross-lmk', 'dish_aerial', 'dish_aerial', 'dome', 'dome', 'flagstaff', 'flagstaff', 'flare_stack', 'flare-stack',
      'mast', 'mast', 'monument', 'monument', 'obelisk', 'obelisk', 'radar_scanner', 'radar_scanner', 'spire', 'spire', 'statue', 'statue', 'tower', 'tower-lmk', 'windmill', 'windmill', 'windmotor', 'windmotor', 'windsock', 'windsock', 'lighthouse', 'tower-lmk', 'monument']]];

const facility: ExpressionSpecification = ['concat', S, ['match', ['coalesce', ['get', 'category'], ''],
  'slipway', 'poi-slipway', 'fuel_station', 'poi-fuel', ['water_tap', 'drinking_water'], 'poi-water', ['pump-out', 'pump_out'], 'poi-pumpout', ['visitor_berth', 'visitors_berth', 'visitor_buoy'], 'poi-berth', 'boat_hoist', 'poi-hoist',
  'boatyard', 'poi-boatyard', 'chandler', 'poi-chandler', 'electricity', 'poi-electricity', 'laundrette', 'poi-laundry', 'showers', 'poi-showers', 'toilets', 'poi-toilets', ['toilets;showers', 'toilets;showers;laundrette'], 'poi-toilets',
  'sailing_club', 'poi-club', 'boat_storage', 'poi-boat-storage', 'fish_cleaning', 'poi-fish-cleaning', 'kayak', 'poi-kayak', 'provisions', 'poi-provisions', 'restaurant', 'poi-restaurant', 'beach', 'poi-beach', 'poi-generic']];

export const chartIconExpression: ExpressionSpecification = ['case',
  ['any', ['in', 'buoy', ['get', 'type']], ['in', 'beacon', ['get', 'type']], ['in', ['get', 'type'], ['literal', ['light_float', 'light_vessel']]]], buoyIcon,
  ['==', ['get', 'type'], 'light_major'], S + 'light-major',
  ['==', ['get', 'type'], 'light_minor'], S + 'light-minor',
  ['==', ['get', 'type'], 'landmark'], landmark,
  ['==', ['get', 'type'], 'small_craft_facility'], facility,
  ['==', ['get', 'type'], 'harbour'], ['case', ['==', ['get', 'category'], 'fishing'], S + 'poi-fishing-harbour', S + 'poi-marina'],
  ['==', ['get', 'type'], 'rock'], ['match', ['coalesce', ['get', 'water_level'], ''], 'covers', S + 'rock-covers', 'awash', S + 'rock-awash', ['dry', 'part-submerged', 'part_submerged'], S + 'rock-dry', S + 'rock-submerged'],
  ['==', ['get', 'type'], 'wreck'], ['match', ['coalesce', ['get', 'category'], ''], 'non-dangerous', S + 'wreck-non-dangerous', ['hull_showing', 'mast_showing'], S + 'wreck-hull_showing', 'distributed_remains', S + 'foul', S + 'wreck-dangerous'],
  ['==', ['get', 'type'], 'obstruction'], ['case', ['==', ['get', 'category'], 'foul_ground'], S + 'foul', S + 'obstruction'],
  ['in', ['get', 'type'], ['literal', ['anchorage', 'anchor_berth']]], S + 'anchor',
  ['==', ['get', 'type'], 'mooring'], S + 'pilpnt',
  ['==', ['get', 'type'], 'fog_signal'], S + 'fogsignal',
  ['==', ['get', 'type'], 'rescue_station'], S + 'rescue',
  ['==', ['get', 'type'], 'pilot_boarding'], S + 'pilot-boarding',
  ['==', ['get', 'type'], 'platform'], S + 'platform',
  ['==', ['get', 'type'], 'crane'], S + 'crane',
  ['in', ['get', 'type'], ['literal', ['radar_station', 'radar_transponder']]], S + 'radar_scanner',
  ['in', ['get', 'type'], ['literal', ['signal_station_traffic', 'signal_station_warning', 'coastguard_station']]], S + 'signal',
  ['==', ['get', 'type'], 'notice'], S + 'notice-board',
  ['in', ['get', 'type'], ['literal', ['cable_submarine', 'cable_overhead']]], S + 'cable',
  ['in', ['get', 'type'], ['literal', ['pipeline_submarine', 'pipeline_overhead']]], S + 'pipeline',
  ['==', ['get', 'type'], 'marine_farm'], S + 'marine-farm',
  ['==', ['get', 'type'], 'restricted_area'], S + 'caution',
  ['==', ['get', 'type'], 'gate'], S + 'notice-board',
  S + 'pilpnt'];

/** Topmark: "<shape>/<colour>" (shape names already normalised by the tile server: 2_cones_up, cone_point_up, cylinder, cross, sphere …). */
export const chartTopmarkExpression: ExpressionSpecification = ['coalesce',
  ['image', ['concat', S, ['get', 'topmark_shape'], '/', ['coalesce', ['get', 'topmark_colour_key'], 'generic']]],
  ['image', ['concat', S, ['get', 'topmark_shape'], '/generic']],
  ['image', ['concat', S, ['get', 'topmark_shape']]]];
export const chartTopmarkOffset: ExpressionSpecification = ['case',
  ['in', ['get', 'shape'], ['literal', ['pillar', 'spar', 'stake', 'pole', 'perch', 'post']]], ['literal', [0, -22]],
  ['in', ['get', 'shape'], ['literal', ['buoyant', 'lattice', 'pile', 'tower']]], ['literal', [0, -20]],
  ['in', ['get', 'shape'], ['literal', ['conical', 'cone']]], ['literal', [0, -12]],
  ['==', ['get', 'shape'], 'can'], ['literal', [0, -8]],
  ['in', ['get', 'shape'], ['literal', ['spherical', 'sphere', 'barrel', 'super-buoy']]], ['literal', [0, -9]],
  ['literal', [0, -26]]];

/** Light flare in the light's colour (sprite "light/R" …); white lights draw as yellow on charts. */
export const lightIconExpression: ExpressionSpecification = ['concat', S, 'light/', ['match', ['coalesce', ['get', 'light_colour'], ''], 'white', 'W', 'red', 'R', 'green', 'G', 'yellow', 'Y', 'blue', 'Bu', 'orange', 'Or', 'amber', 'Am', 'generic']];

/** Rocks, wrecks and obstructions with a charted depth deeper than draft + margin are hidden; everything else stays. */
export function hazardFilter(draftM: number | null, marginM = 0.5): ExpressionSpecification {
  const base: ExpressionSpecification = ['==', ['geometry-type'], 'Point'];
  if (draftM === null) return base;
  return ['all', base, ['any', ['!', ['in', ['get', 'type'], ['literal', ['rock', 'wreck', 'obstruction']]]], ['!', ['has', 'depth']], ['<', ['get', 'depth'], draftM + marginM]]];
}
