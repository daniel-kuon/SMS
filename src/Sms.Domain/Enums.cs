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
public enum CorrectionKind { Block, Passage }
public enum CorrectionSource { Manual, Derived }

/// <summary>16 compass points (spec 08).</summary>
public enum WindDirection { N, NNE, NE, ENE, E, ESE, SE, SSE, S, SSW, SW, WSW, W, WNW, NW, NNW }
