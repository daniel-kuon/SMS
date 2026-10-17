import { composeSvg, formatPosition, iconSpec, parseIconId, pathBox, seamarkTypeLabel, symbolPath } from './seamark-icons';

describe('seamark icons', () => {
  it('maps OSM sea-mark types to OpenSeaMap bodies, colours and topmarks', () => {
    expect(iconSpec({ type: 'buoy_lateral', category: 'port' })).toMatchObject({ body: 'Can', colours: ['red'] });
    expect(iconSpec({ type: 'buoy_lateral', category: 'starboard', shape: 'spar' })).toMatchObject({ body: 'Spar', colours: ['green'] });
    expect(iconSpec({ type: 'buoy_lateral', category: 'preferred_channel_starboard', colour: 'red;green;red' })).toMatchObject({ body: 'Can', colours: ['red', 'green', 'red'] });
    expect(iconSpec({ type: 'buoy_cardinal', category: 'west', shape: 'pillar' })).toMatchObject({ body: 'Pillar', colours: ['yellow', 'black', 'yellow'], top: 'Top_West' });
    expect(iconSpec({ type: 'beacon_cardinal', category: 'north' })).toMatchObject({ body: 'Beacon', top: 'Top_North' });
    expect(iconSpec({ type: 'buoy_safe_water' })).toMatchObject({ body: 'Sphere', colours: ['red', 'white'], vertical: true, top: 'Top_Sphere' });
    expect(iconSpec({ type: 'buoy_isolated_danger' })).toMatchObject({ colours: ['black', 'red', 'black'], top: 'Top_Isol' });
    expect(iconSpec({ type: 'light_major' })).toMatchObject({ body: 'Light_Major', fixed: true });
    expect(iconSpec({ type: 'harbour', category: 'marina' })).toMatchObject({ body: 'Marina' });
    expect(iconSpec({ type: 'beacon_lateral', category: 'port', shape: 'perch' })).toMatchObject({ body: 'Perch_Port' });
    expect(iconSpec({ type: 'something_new' })).toMatchObject({ body: 'Post', colours: ['grey'] });
    expect(iconSpec({ type: 'buoy_lateral', category: 'port', colour: 'purple' }).colours).toEqual(['red']); // unknown colour ignored
  });

  it('round-trips the image id used by the symbol layer', () => {
    expect(parseIconId('sm|buoy_lateral|port|can|red')).toEqual({ type: 'buoy_lateral', category: 'port', shape: 'can', colour: 'red' });
    expect(parseIconId('sm|light_major|||')).toEqual({ type: 'light_major', category: null, shape: null, colour: null });
    expect(parseIconId('something-else')).toBeNull();
  });

  it('computes path bounding boxes for absolute and relative commands', () => {
    expect(pathBox('M 10 20 L 30 40 Z')).toEqual({ minX: 10, minY: 20, maxX: 30, maxY: 40 });
    expect(pathBox('m 10,20 20,0 0,10 h -5 v 5 z')).toEqual({ minX: 10, minY: 20, maxX: 30, maxY: 35 });
    const can = pathBox('m 159.48,160.26 -28.062,-8.8438 -15.219,45.75 h -7.9688 v 5.6562 h 29.688 c 1.2873,5.533 6.2333,9.6562 12.156,9.6562 5.925,0 10.871,-4.1205 12.156,-9.6562 h 29.844 v -0.0625 -5.5938 h -7.6875 l 9,-26.188 -29.938,-9.4375 z');
    expect(can.minX).toBeCloseTo(108.2, 0); expect(can.maxX).toBeCloseTo(193.4, 0); // control points count expect(can.minY).toBeCloseTo(151.4, 0); expect(can.maxY).toBeCloseTo(212.5, 0);
  });

  it('composes a coloured, banded SVG with a topmark', () => {
    const body = 'M 0 0 L 100 0 L 100 200 L 0 200 Z';
    const svg = composeSvg({ body: 'Pillar', colours: ['black', 'yellow'], top: 'Top_North' }, body, 'M 0 0 L 40 0 L 20 60 Z');
    expect(svg).toContain('<clipPath id="c">');
    expect(svg).toContain('fill="#212121"'); // black band
    expect(svg).toContain('fill="#fbc02d"'); // yellow band
    expect((svg.match(/<rect/g) ?? []).length).toBe(2);
    expect(svg).toMatch(/<g transform="translate\([\d.]+ [\d.]+\) scale\([\d.]+\)/);
    const single = composeSvg({ body: 'Can', colours: ['red'] }, body, null);
    expect(single).toContain('fill="#d32f2f"'); expect(single).not.toContain('clipPath');
    const vertical = composeSvg({ body: 'Sphere', colours: ['red', 'white'], vertical: true }, body, null);
    expect(vertical).toContain('width="50.5"'); // bands side by side
  });

  it('caches symbol paths and extracts the first path', async () => {
    let calls = 0;
    const fetcher = async (url: string) => { calls++; return `<svg><path d="M 1 2 L 3 4"/></svg><!-- ${url} -->`; };
    expect(await symbolPath('Test_A', fetcher)).toBe('M 1 2 L 3 4');
    expect(await symbolPath('Test_A', fetcher)).toBe('M 1 2 L 3 4');
    expect(calls).toBe(1);
  });

  it('formats chart positions in degrees and decimal minutes', () => {
    expect(formatPosition(54.372, 10.1456)).toBe("54°22.320' N 010°08.736' E");
    expect(formatPosition(-33.86, -151.2)).toBe("33°51.600' S 151°12.000' W");
    expect(seamarkTypeLabel('buoy_lateral', 'de')).toBe('Seitentonne');
    expect(seamarkTypeLabel('buoy_lateral', 'en')).toBe('Lateral buoy');
    expect(seamarkTypeLabel('odd_type', 'en')).toBe('odd type');
  });
});
