import { beaufort, compass, fmtObservation, weatherCodeInfo } from './weather';

describe('weather helpers', () => {
  it('maps WMO codes to icons', () => {
    expect(weatherCodeInfo(0)).toEqual({ icon: 'wb_sunny', key: 'clear' });
    expect(weatherCodeInfo(2).key).toBe('partlyCloudy');
    expect(weatherCodeInfo(45).key).toBe('fog');
    expect(weatherCodeInfo(63).key).toBe('rain');
    expect(weatherCodeInfo(80).key).toBe('showers');
    expect(weatherCodeInfo(95).key).toBe('thunderstorm');
    expect(weatherCodeInfo(null).key).toBe('unknown');
  });

  it('converts knots to Beaufort', () => {
    expect(beaufort(0)).toBe(0); expect(beaufort(2)).toBe(1); expect(beaufort(12)).toBe(4); expect(beaufort(33)).toBe(8); expect(beaufort(70)).toBe(12); expect(beaufort(null)).toBeNull();
  });

  it('names compass points the German way', () => {
    expect(compass(0)).toBe('N'); expect(compass(90)).toBe('O'); expect(compass(225)).toBe('SW'); expect(compass(359)).toBe('N'); expect(compass(null)).toBe('–');
  });

  it('formats a recorded observation in one line', () => {
    const s = fmtObservation({ id: 1, time: '2026-10-12T08:00:00Z', lat: 54, lng: 10, temperatureC: 14.2, windKn: 12.5, windDirDeg: 250, gustKn: null, precipitationMm: null, weatherCode: 3, pressureHpa: null, cloudCover: null, waveHeightM: 0.6, waveDirDeg: null, wavePeriodS: null });
    expect(s).toContain('14 °C'); expect(s).toContain('WSW 13 kn'); expect(s).toContain('0.6 m');
  });
});
