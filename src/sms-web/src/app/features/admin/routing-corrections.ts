import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import type { FeatureCollection, Geometry, LineString, Polygon } from 'geojson';
import type { GeoJSONSource, MapMouseEvent } from 'maplibre-gl';
import { bounds, createMap, MlMap } from '../../core/map';
import { Api, errorMessage } from '../../core/api';
import { haversineM } from '../../core/track-playback';

export interface Correction { id: string; kind: 'Block' | 'Passage' | 'Bridge' | 'Lock' | 'OpeningBridge' | 'Fairway' | 'Avoid' | 'Water'; source: 'Manual' | 'Derived'; geometry: Geometry; note: string | null; active: boolean; createdAt: string; clearanceM?: number | null; }
export const KIND_ICON: Record<Correction['kind'], string> = { Block: 'block', Passage: 'timeline', Bridge: 'vertical_align_top', Lock: 'lock', OpeningBridge: 'open_in_full', Fairway: 'water', Avoid: 'warning', Water: 'waves' };

/** Length of a LineString in nautical miles (0 for other geometries). */
export function lengthNm(g: Geometry): number {
  if (g.type !== 'LineString') return 0;
  let m = 0;
  for (let i = 1; i < g.coordinates.length; i++) m += haversineM(g.coordinates[i - 1][1], g.coordinates[i - 1][0], g.coordinates[i][1], g.coordinates[i][0]);
  return m / 1852;
}

export function toFeatureCollection(list: Correction[]): FeatureCollection {
  return { type: 'FeatureCollection', features: list.map(c => ({ type: 'Feature', id: c.id, properties: { id: c.id, kind: c.kind, source: c.source, active: c.active, note: c.note ?? '' }, geometry: c.geometry })) };
}

/** Closes a ring for a Block polygon (first point repeated) – needs at least 3 distinct points. */
export function draftGeometry(kind: 'Block' | 'Passage', points: [number, number][]): LineString | Polygon | null {
  if (kind === 'Passage') return points.length >= 2 ? { type: 'LineString', coordinates: points } : null;
  return points.length >= 3 ? { type: 'Polygon', coordinates: [[...points, points[0]]] } : null;
}

