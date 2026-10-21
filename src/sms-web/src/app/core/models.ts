import { Visibility } from './api';

export interface Paged<T> { items: T[]; page: number; pageSize: number; total: number; }

export interface HarbourMarker { id: string; name: string; lat: number; lng: number; category: string | null; country: string | null; }
export interface Wifi { id: string; name: string; password: string | null; speed: number; free: boolean; }
export interface Review { id: string; authorId: string; author: string; rating: number; text: string | null; visitedOn: string | null; createdAt: string; }
export interface Harbour extends HarbourMarker {
  website: string | null; description: string | null; phone: string | null; vhfChannel: string | null; facilities: Record<string, boolean>;
  source: string; osmType: string | null; osmId: number | null; wifis: Wifi[]; reviews: Review[]; averageRating: number | null; photoUrls: string[]; services: HarbourService[];
}
/** Nearest service of a kind (0 m = on site, from the harbour's facility flags). */
/** distanceM straight; walkM over the walking network when known (null/absent: no way nearby, the straight distance stands). */
export interface HarbourService { kind: string; distanceM: number; name: string | null; poiId: number | null; walkM?: number | null; }
export interface NearbyPort { harbourId: string; name: string; lat: number; lng: number; distanceNm: number; straightLineNm: number; etaHours: number; method: string; }

export type ShipRole = 'Owner' | 'Skipper';
export interface ShipMember { userId: string; displayName: string; role: ShipRole; since: string; }
export interface Ship {
  id: string; name: string; type: string | null; lengthM: number | null; beamM: number | null; draftM: number | null; airDraftM: number | null;
  sailNumber: string | null; callSign: string | null; mmsi: string | null; homeHarbourId: string | null; homeHarbourName: string | null;
  cruisingSpeedKn: number; description: string | null; visibility: Visibility; photoUrl: string | null; members: ShipMember[]; myRole: ShipRole | null;
  /** Routes avoid water whose depth band lies below this (null: draft + 0.5 m); warnings where a band's floor lies below warnDepthM (null: none). */
  minDepthM?: number | null; warnDepthM?: number | null;
}
export interface ShipStats { nauticalMiles: number; engineHours: number; entries: number; harbours: number; trips: number; }

export type CrewRole = 'Skipper' | 'Crew';
export type CrewStatus = 'Invited' | 'Accepted' | 'Declined' | 'Left';
export type TripStatus = 'Planned' | 'Active' | 'Completed' | 'Cancelled';
export interface Crew { id: string; userId: string | null; name: string; role: CrewRole; canWriteLogbook: boolean; status: CrewStatus; fromDate: string | null; toDate: string | null; isGuest: boolean; }
export interface TripSummary { id: string; shipId: string; shipName: string; name: string; startDate: string; endDate: string; status: TripStatus; visibility: Visibility; crewCount: number; }
export interface Trip extends TripSummary {
  description: string | null; timeZone: string; plannedStartHarbourId: string | null; plannedEndHarbourId: string | null; currency: string; costsLockedAt: string | null;
  crew: Crew[]; me: Crew | null; canEdit: boolean; canWriteLogbook: boolean; isCrew: boolean; costsVisible: boolean; trackVisibility: Visibility; publicPositionDelayMinutes: number;
}
export interface Leg { id: string; order: number; alternative: number; fromHarbourId: string; fromName: string; toHarbourId: string; toName: string; plannedDate: string | null; distanceNm: number | null; etaHours: number | null; warnings?: RouteWarning[]; }
/** A route assumption the chart data does not show (kind "assumedPassage": a strait the coastline closes, passed along a ferry line or buoy gap).
 *  Kind "startOnLand"/"endOnLand": the clicked point (lat/lng) lies on land, the route starts/ends at the nearest navigable water (snapped*, distanceM away).
 *  Kind "checkDepth": the route crosses water charted below the ship's warning depth at lat/lng (distanceM: length of that stretch).
 *  Kind "bridge": asked without an air draft, the route passes under a fixed bridge at lat/lng (distanceM: its charted clearance).
 *  Kind "bridgeUnknown": the route passes under a fixed bridge without a charted clearance at lat/lng.
 *  Kind "uncertainDepth": no route leads through definitely deep water and buoyed channels only; this one crosses water whose band floor lies
 *  below the minimum depth at lat/lng (distanceM: length of that stretch).
 *  Kind "cardinal": the route runs through the danger quadrant of a cardinal mark at lat/lng (one per mark). */
export interface RouteWarning { kind: string; lat: number; lng: number; note: string; snappedLat?: number | null; snappedLng?: number | null; distanceM?: number | null; }
/** " · ⚠ <notes>" for a route's warnings (the same note once, with a count), "" without any. */
export function routeWarningText(warnings?: RouteWarning[] | null): string {
  if (!warnings?.length) return '';
  const counts = new Map<string, number>(); for (const w of warnings) counts.set(w.note, (counts.get(w.note) ?? 0) + 1);
  return ` · ⚠ ${[...counts].map(([note, n]) => n > 1 ? `${note} (${n}×)` : note).join('; ')}`;
}

export interface EntryCrew { crewMemberId: string | null; userId: string | null; name: string; }
export interface LogbookEntry {
  id: string; shipId: string; tripId: string | null; startHarbourId: string; startHarbour: string; endHarbourId: string; endHarbour: string;
  departedAt: string; arrivedAt: string; duration: string; engineHoursStart: number | null; engineHoursEnd: number | null; logStart: number | null; logEnd: number | null;
  windSpeed: string | null; windDirection: string | null; weather: string | null; seaState: string | null; sailedDistanceNm: number | null; sailsNotes: string | null; notes: string | null;
  visibility: Visibility; authorId: string; crew: EntryCrew[]; crewList: string; canEdit: boolean;
  routeGeometry?: { type: 'LineString'; coordinates: number[][] } | null;
}
export interface LogbookDefaults { startHarbourId: string | null; startHarbourName: string | null; engineHoursStart: number | null; logStart: number | null; crew: EntryCrew[]; }

export interface Media {
  id: string; ownerId: string; owner: string; tripId: string | null; postId: string | null; day: string | null; takenAt: string | null; lat: number | null; lng: number | null;
  locationSource: string | null; caption: string | null; visibility: Visibility; width: number | null; height: number | null; thumbUrl: string; previewUrl: string; originalUrl: string;
}
export interface Post {
  id: string; tripId: string; authorId: string; author: string; day: string; title: string | null; content: string; harbourId: string | null; lat: number | null; lng: number | null;
  locationSource: string | null; visibility: Visibility; publishedAt: string; media: Media[]; canEdit: boolean;
}
export interface Day { date: string; logbook: LogbookEntry[]; posts: Post[]; photos: Media[]; }

export type SplitMode = 'Equal' | 'Weights' | 'Exact';
export interface Share { crewMemberId: string; name: string; weight: number | null; exactAmount: number | null; computedShare: number; }
export interface Expense {
  id: string; tripId: string; paidByCrewMemberId: string; paidBy: string; amount: number; currency: string; paidOn: string; exchangeRate: number; rateDate: string;
  rateSource: string; amountInTripCurrency: number; category: string; description: string | null; receiptUrl: string | null; splitMode: SplitMode; shares: Share[]; createdById: string | null; deleted: boolean;
}
export interface Balance { crewMemberId: string; name: string; paid: number; share: number; sent: number; received: number; balance: number; }
export interface Transfer { fromCrewMemberId: string; from: string; toCrewMemberId: string; to: string; amount: number; }
export interface Settlement { id: string; fromCrewMemberId: string; from: string; toCrewMemberId: string; to: string; amount: number; paidAt: string; status: 'Reported' | 'Confirmed'; note: string | null; }
export interface Balances { currency: string; total: number; myCrewMemberId: string | null; balances: Balance[]; suggestedTransfers: Transfer[]; settlements: Settlement[]; lockedAt: string | null; }

export interface TrackingDevice { id: string; name: string; protocol: 'OsmAnd' | 'OwnTracks' | 'Json'; tripId: string | null; active: boolean; lastSeenAt: string | null; owner: string; }
export interface DeviceCreated { id: string; token: string; osmAndUrl: string; ownTracksUrl: string; jsonUrl: string; hint: string; }
export interface PrivacyZone { id: string; name: string; lat: number; lng: number; radiusM: number; }
export interface TaskItem { id: string; scope: 'Trip' | 'Ship'; tripId: string | null; shipId: string | null; parentId: string | null; title: string; content: string | null; dueDate: string | null; assigneeCrewMemberId: string | null; assigneeUserId: string | null; assignee: string | null; done: boolean; }
export interface ContentPage { slug: string; title: string; content: string; language: string; showInFooter: boolean; }
