namespace Sms.Domain;

/// <summary>Spec 08 §8.3: Private &lt; Crew &lt; Public.</summary>
public enum Visibility { Private = 0, Crew = 1, Public = 2 }

public enum ShipRole { Owner, Skipper }
public enum CrewRole { Skipper, Crew }
public enum CrewStatus { Invited, Accepted, Declined, Left }
public enum InvitationStatus { Pending, Accepted, Declined, Expired }
public enum TripStatus { Planned, Active, Completed, Cancelled }
public enum SplitMode { Equal, Weights, Exact }
public enum RateSource { Ecb, Manual, Same }
public enum SettlementStatus { Reported, Confirmed }
public enum MediaKind { Photo, Gpx, Receipt, Avatar }
public enum ProcessingStatus { Pending, Done, Failed }
public enum LocationSource { Manual, Exif, Device, Track, Harbour }
public enum HarbourSource { Osm, User, Legacy }
public enum TaskScope { Trip, Ship }
public enum ProfileVisibility { Public, Registered }
/// <summary>OpeningBridge: a movable bridge – no clearance limit, but a waiting-time penalty like a lock (spec 10 canals).</summary>
/// <summary>Fairway: a marked channel area (polygon, OSM seamark:type=fairway) – open water next to it is penalised, water inside it is not.</summary>
/// <summary>Avoid: a warning/restricted-entry area (firing range outside firing hours) – crossed only when the detour is long.</summary>
/// <summary>Water: an inland water body (lake, IJsselmeer) that the coastline-based land polygons count as land.</summary>
public enum CorrectionKind { Block, Passage, Bridge, Lock, OpeningBridge, Fairway, Avoid, Water, Pier }
public enum CorrectionSource { Manual, Derived }
/// <summary>How routes treat fixed bridges without a charted clearance: passed with a warning, or avoided unless no other route exists.</summary>
public enum UnknownBridgePolicy { Pass, Avoid }

/// <summary>16 compass points (spec 08).</summary>
public enum WindDirection { N, NNE, NE, ENE, E, ESE, SE, SSE, S, SSW, SW, WSW, W, WNW, NW, NNW }
