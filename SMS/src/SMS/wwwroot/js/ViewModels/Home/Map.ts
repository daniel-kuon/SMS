import SHarbour = ServerModel.Harbour;

import Waypoint = ClientModel.Waypoint;
import Harbour = ClientModel.Harbour;
import Job = ClientModel.Job;
import WaypointDistance = ClientModel.WaypointDistance;

function getMiddle(pol: L.Polyline): L.LatLng {
    const start = pol.getLatLngs()[0];
    const end = pol.getLatLngs()[1];
    return new L.LatLng(start.lat + ((end.lat - start.lat) / 2), start.lng + ((end.lng - start.lng) / 2));
}

function splitPolyline(polyline: L.Polyline) {
    if (polyline.Waypoints.length === 2 && polyline.DummyHandle instanceof Waypoint) {
        const w1 = polyline.Waypoints[0];
        const w2 = polyline.DummyHandle;
        const w3 = polyline.Waypoints[1];
        w2.RemoveFromPolyline(polyline);
        polyline.DummyHandle = undefined;
        w2.AddToPolyline(polyline);
        w3.RemoveFromPolyline(polyline);
        addDummyHandle(polyline);
        addDummyHandle(mapViewModel.AddPolyline([w2, w3]));
        return;
    }
    throw new Error("Cannot split polyline. Polyline has no dummy handle or less or more than 2 waypoints");
}


function removePolyline(polyline: L.Polyline) {
    for (let waypoint of polyline.Waypoints) {
        waypoint.RemoveFromPolyline(polyline);
    }
    if (polyline.DummyHandle !== undefined) {
        polyline.DummyHandle.RemoveFromPolyline(polyline);
        polyline.DummyHandle.RemoveFromMap();
    }
    mapViewModel.Map.removeLayer(polyline);
}

function addDummyHandle(polyline: L.Polyline) {
    if (polyline.DummyHandle === undefined) {
        polyline.DummyHandle = mapViewModel.CreateWaypoint(getMiddle(polyline), MarkerType.Dummy);
        polyline.DummyHandle.AddToPolyline(polyline);
    }
}

function redrawPolyline(polyline: L.Polyline) {
    const middleLatLng = getMiddle(polyline);
    if (polyline.DummyHandle === undefined)
        addDummyHandle(polyline);
    if (polyline.DummyHandle.Longitude() !== middleLatLng.lng || polyline.DummyHandle.Latitude() !== middleLatLng.lat)
        polyline.DummyHandle.SetLatLng(middleLatLng);
    else
        polyline.redraw();
}

function removeFromPolyline(polyline: L.Polyline, latLng: L.LatLng) {
    removeFromArray(polyline.getLatLngs(), latLng);
    polyline.redraw();
}

function removeFromArray<T>(arr: T[], obj: T): boolean {
    const tmpArr = new Array<T>();
    for (let item of arr) {
        if (item !== obj)
            tmpArr.push(item);
    }
    if (tmpArr.length === arr.length)
        return false;
    while (arr.pop()) {
    }
    while (tmpArr.length > 0) {
        arr.push(tmpArr.shift());
    }
    return true;
}

enum MapMode {
    Admin,
    View,
    TripPlanning,
    RouteDrawing
}

declare namespace L {
    export interface Polyline extends Path {

        Waypoints: Array<Waypoint>;
        DummyHandle: Waypoint;
    }

    export interface LatLng {
        Polylines: Polyline[];
        Waypoint: Waypoint;
    }

    export interface Marker {
        Waypoint: Waypoint;
        Point: L.Point;
        _icon;
    }

    export interface CircleMarker {
        Waypoint: Waypoint;
    }

    export interface PathOptions {
        Draggable?: boolean;
    }

    export interface MarkerOptions {
        contextmenu?: boolean;
        contextmenuWidth?: number;
        contextmenuAnchor?: L.Point | L.Point[];
        contextmenuItems?: contextmenuItem[];
        contextmenuInheritItems: boolean;

    }

    export interface contextmenuItem {
        text?: string;
        icon?: string;
        iconCls?: string;
        callback?: Function;
        context?: Object;
        disabled?: boolean;
        separator?: boolean;
        hideOnSelect?: boolean;
        index?: number;
    }


}

class MapViewModel {
    constructor(mapMode: MapMode) {
        L.mapbox
            .accessToken =
            "pk.eyJ1IjoiZGFuaWVsLWt1b24iLCJhIjoiY2lldnVtY29iMDBiOHQxbTBvZzBqZWl6cCJ9.UEc2YqH59pB1YTpv22vg8A";
        this.MapMode(mapMode);
        this.MapMode.subscribe(() => {
            this.InitializeMap();
        });
        const mapOptions: L.mapbox.MapOptions = {
            contextmenu: mapMode === MapMode.Admin,
            contextmenuItems: [
                {
                    text: "Neuer Hafen",
                    callback: function (e) {
                        console.log(e);
                        mapViewModel.EditingHarbour(mapViewModel.CreateHarbour("", e.latlng))
                    }
                }
            ]
        };
        this.Map = L.mapbox.map("map",
            "mapbox.streets",
            mapOptions);
        this.Map.setView([54.40774166820069, 10.523529052734373], 9);
        L.tileLayer("http://t1.openseamap.org/seamark/{z}/{x}/{y}.png").addTo(this.Map);
        this.LoadData();
        this.SelectedHarbour.subscribe((newHarbour) => {
            if (newHarbour !== undefined) {
                mapViewModel.CalculateDistances(newHarbour);
                mapViewModel.Harbours.sort((h1, h2) => h1.Distance() - h2.Distance());
            } else {
                for (let h of mapViewModel.Harbours()) {
                    h.Distance(0);
                }
            }
            mapViewModel.routeFixed = false;
            mapViewModel.HideRoute();
        });
        this.EditingHarbour.subscribe((harbour) => {
            if (harbour === undefined) {
                editingHarbourModal.modal("hide");
            } else {
                harbour.SaveState();
                editingHarbourModal.modal("show");
            }
        });
        this.EditingHarbour.subscribe((harbour) => {
            if (harbour !== undefined) {
                harbour.RevertState(true);
                if (harbour.Id() === undefined)
                    mapViewModel.Map.removeLayer(harbour.marker);
            }
        },
            this,
            "beforeChange");
        this.DeletingHarbour.subscribe((h) => {
            if (h === undefined) {
                deletingHarbourModal.modal("hide");
            } else {
                deletingHarbourModal.modal("show");
            }
        });
        this.EditingWaypoint.subscribe((waypoint) => {
            if (waypoint === undefined) {
                editingWaypointModal.modal("hide");
            } else {
                waypoint.SaveState();
                editingWaypointModal.modal("show");
            }
        });
        this.EditingWaypoint.subscribe((waypoint) => {
            if (mapViewModel.EditingWaypoint() !== undefined)
                mapViewModel.EditingWaypoint().RevertState(true);
        },
            this,
            "beforeChange");
        this.DeletingWaypoint.subscribe((h) => {
            if (h === undefined) {
                deletingWaypointModal.modal("hide");
            } else {
                deletingWaypointModal.modal("show");
            }
        });
        this.EditingJob.subscribe((job) => {
            if (job === undefined) {
                editingJobModal.modal("hide");
            } else {
                job.SaveState();
                editingJobModal.modal("show");
            }
        });
        this.EditingJob.subscribe((job) => {
            if (mapViewModel.EditingJob() !== undefined)
                mapViewModel.EditingJob().RevertState(true);
        },
            this,
            "beforeChange");
        this.DeletingJob.subscribe((h) => {
            if (h === undefined) {
                deletingJobModal.modal("hide");
            } else {
                deletingJobModal.modal("show");
            }
        });
        this.SelectedHarbour.subscribe((h) => {
            if (h === undefined)
                rightSidebar.Hide();
            else
                rightSidebar.Show();

        });
        this.SelectedTrip.subscribe((t) => {
            if (t === undefined)
                bottomSidebar.Hide();
            else
                bottomSidebar.Show();
        });
        this.Map.addEventListener("mousemove",
            (e: L.LeafletMouseEvent) => {
                if (this.GetMapMode() === MapMode.RouteDrawing) {
                    this.DrawingLatLng.lat = e.latlng.lat;
                    this.DrawingLatLng.lng = e.latlng.lng;
                    this.DrawingPolyline.redraw();
                }
                if (this.MapMode() === MapMode.Admin)
                    for (let marker of this.WaypointMarkers) {
                        if (marker.Point.distanceTo(e.containerPoint) < 150)
                            marker.setOpacity(marker.Waypoint.IsDummy() ? 0.0 : 1);
                        else
                            marker.setOpacity(marker.Waypoint.IsDummy() ? 0.0 : 0.8);
                    }
                if (mapViewModel.HoveredPolyine !== undefined && mapViewModel.HoveredPolyine.DummyHandle !== undefined) {
                    const polyline = mapViewModel.HoveredPolyine;
                    const p1 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[0]);
                    const p2 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[1]);
                    if (p1.distanceTo(e.containerPoint) < 20 || p2.distanceTo(e.containerPoint) < 20) {
                        mapViewModel.HoveredPolyine = undefined;
                    } else {
                        mapViewModel.HoveredPolyine.DummyHandle.marker.setOpacity(0.8);
                        mapViewModel.HoveredPolyine.DummyHandle
                            .SetLatLng(mapViewModel.Map
                                .containerPointToLatLng(L.LineUtil
                                    .closestPointOnSegment(e.containerPoint,
                                    p1,
                                    p2)),
                            false);
                    }
                }

            });
        this.Map.addEventListener("click",
            (e: L.LeafletMouseEvent) => {
                if (this.GetMapMode() === MapMode.RouteDrawing) {
                    const waypoint = mapViewModel.CreateWaypoint(e.latlng, MarkerType.Waypoint);
                    const startId = this.DrawingPolyline.Waypoints[0].Id();
                    waypoint.SaveToServer()
                        .done(w => {
                            ServerApi.WaypointConnectionApi.GetDefault()
                                .Connect(w.Id, startId);
                        });
                    waypoint.AddToPolyline(this.DrawingPolyline);
                    addDummyHandle(this.DrawingPolyline);
                    removeFromPolyline(this.DrawingPolyline, this.DrawingLatLng);
                    this.DrawingPolyline = this.AddPolyline(waypoint);
                    this.DrawingLatLng = new L.LatLng(e.latlng.lat, e.latlng.lng);
                    this.DrawingPolyline.addLatLng(this.DrawingLatLng);
                }
            });

        this.Map.addEventListener("dblclick",
            (e: L.LeafletMouseEvent) => {
                if (this.GetMapMode() === MapMode.RouteDrawing) {
                    e.originalEvent.cancelBubble = true;
                    e.originalEvent.preventDefault();
                    e.originalEvent.stopPropagation();
                    this.DrawingPolyline.addLatLng(e.latlng);
                    this.DrawingLatLng = e.latlng;
                }
            });
        $(document)
            .keyup((e: JQueryKeyEventObject) => {
                if (this.GetMapMode() === MapMode.RouteDrawing) {
                    if (e.keyCode === 27) {
                        this.RemovePolyline(this.DrawingPolyline);
                    }
                }
            });
        this.Map.addEventListener("move",
            (e: L.LeafletMouseEvent) => {
                for (let marker of this.WaypointMarkers) {
                    marker.Point = this.Map.latLngToContainerPoint(marker.getLatLng());
                }
            });
        this.Map.addEventListener("zoom",
            (e: L.LeafletMouseEvent) => {
                for (let marker of this.WaypointMarkers) {
                    marker.Point = this.Map.latLngToContainerPoint(marker.getLatLng());
                }
            });
    }

    private routePolyline = ko.observable<L.Polyline>();

    StartRoute() {
        const trip = new ClientModel.Trip();
        const tack = new ClientModel.Tack();
        const harbour = mapViewModel.SelectedHarbour();
        tack.Start(harbour);
        trip.Tacks.push(tack);
        mapViewModel.SelectedTrip(trip);
        mapViewModel.routePolyline(L.polyline([],
            {
                color: "#009900"
            }));
        mapViewModel.routePolyline().addTo(mapViewModel.Map);
    }

    IsLastTakInRoute = ko.computed({
        read: () => {
            var trip = mapViewModel.SelectedTrip();
            var h = mapViewModel.SelectedHarbour();
            return trip !== undefined && h !== undefined && trip.Tacks()[trip.Tacks().length - 1].Start() === h;
        },
        deferEvaluation: true
    });

    GetRouteDistance = ko.computed({
        read: () => {
            var distance = 0;
            for (let tack of mapViewModel.SelectedTrip().Tacks()) {
                if (!isNaN(tack.Distance()))
                    distance += tack.Distance();
            }
            return distance;
        },
        deferEvaluation: true
    });

    TopJobs = ko.computed({
        read: () => {
            return mapViewModel.Jobs().filter((j) => j.SuperJobId() === undefined);
        },
        deferEvaluation: true
    });

    AddToRoute() {
        const trip = mapViewModel.SelectedTrip();
        const targetHarbour = mapViewModel.SelectedHarbour();
        const tack = new ClientModel.Tack();
        const lastTack = trip.Tacks()[trip.Tacks().length - 1];
        const startHarbour = lastTack.Start();
        mapViewModel.CalculateDistances(targetHarbour, startHarbour);
        lastTack.Distance(startHarbour.RouteDistance());
        let wp: ClientModel.Waypoint = startHarbour;
        mapViewModel.routePolyline().addLatLng(wp.LatLng);
        while (wp.RoutePrecessor() !== undefined /*&& wp.RoutePrecessor() !== startHarbour*/) {
            wp = wp.RoutePrecessor();
            mapViewModel.routePolyline().addLatLng(wp.LatLng);
        }

        lastTack.End(targetHarbour);
        tack.Start(targetHarbour);
        trip.Tacks.push(tack);
    }

    RedrawTrip() {
        mapViewModel.Map.removeLayer(mapViewModel.routePolyline());
        mapViewModel.routePolyline(L.polyline([],
            {
                color: "#009900"
            }));
        mapViewModel.routePolyline().addTo(mapViewModel.Map);
        for (let tack of mapViewModel.SelectedTrip().Tacks()) {
            const targetHarbour = tack.End();
            const startHarbour = tack.Start();
            if (targetHarbour === undefined)
                continue;
            mapViewModel.CalculateDistances(targetHarbour, startHarbour);
            tack.Distance(startHarbour.RouteDistance());
            let wp: ClientModel.Waypoint = startHarbour;
            mapViewModel.routePolyline().addLatLng(wp.LatLng);
            while (wp.RoutePrecessor() !== undefined /*&& wp.RoutePrecessor() !== startHarbour*/) {
                wp = wp.RoutePrecessor();
                mapViewModel.routePolyline().addLatLng(wp.LatLng);
            }
        }
    }

    PullTack() {
        const tack: ClientModel.Tack = <any>this;
        const tacks = mapViewModel.SelectedTrip().Tacks;
        const index = tacks.indexOf(tack);
        const prevTack = tacks()[index - 1];
        var tmpEnd = tack.End();
        tack.End(prevTack.Start());
        prevTack.End(tmpEnd);
        if (index > 1) {
            tacks()[index - 2].End(tack.Start());
        }
        tacks.splice(index - 1, 2, tack, prevTack);
        mapViewModel.RedrawTrip();
    }

    PushTack() {
        const tack: ClientModel.Tack = <any>this;
        const tacks = mapViewModel.SelectedTrip().Tacks;
        const index = tacks.indexOf(tack);
        const nextTack = tacks()[index + 1];
        tack.End(nextTack.End());
        nextTack.End(tack.Start());
        if (index > 0) {
            tacks()[index - 1].End(nextTack.Start());
        }
        tacks.splice(index, 2, nextTack, tack);
        mapViewModel.RedrawTrip();
    }

    RemoveTack() {
        const tack: ClientModel.Tack = <any>this;
        const tacks = mapViewModel.SelectedTrip().Tacks;
        const index = tacks.indexOf(tack);
        const prevTack = tacks()[index - 1];
        if (prevTack !== undefined)
            prevTack.End(tack.End());
        tacks.remove(tack);
        mapViewModel.RedrawTrip();
    }

    IsInViewMode = ko.computed<boolean>({
        read: () => {
            return mapViewModel.MapMode() === MapMode.View;
        },
        deferEvaluation: true
    });

    IsInAdminMode = ko.computed<boolean>({
        read: () => {
            return mapViewModel.MapMode() === MapMode.Admin;
        },
        deferEvaluation: true
    });


    LoadData() {
        ServerApi.WaypointApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    if (sEntity.Type === ServerModel.Waypoint.GetType())
                        this.Waypoints.push(mapViewModel
                            .CreateWaypoint(L.latLng(sEntity.Latitude, sEntity.Longitude), MarkerType.Waypoint)
                            .LoadFromServerEntity(sEntity));
                    else if (sEntity.Type === ServerModel.Harbour.GetType()) {
                        const harbour = mapViewModel
                            .CreateHarbour(sEntity.Name, L.latLng(sEntity.Latitude, sEntity.Longitude))
                            .LoadFromServerEntity(sEntity as SHarbour);
                        this.Harbours.push(harbour);
                    }
                }

                this.WaypointsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.WaypointConnectionApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.WaypointConnections.push(sEntity);
                }
                this.WaypointConnectionsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.PersonApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Persons.push(new ClientModel.Person().LoadFromServerEntity(sEntity));
                }
                this.PersonsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.JobApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Jobs.push(new ClientModel.Job().LoadFromServerEntity(sEntity));
                }
                this.JobsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.TripApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Trips.push(new ClientModel.Trip().LoadFromServerEntity(sEntity));
                }
                this.TripsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.AddressApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Addresses.push(new ClientModel.Address().LoadFromServerEntity(sEntity));
                }
                this.AddressesLoaded = true;
                this.InitializeModel();
            });
        ServerApi.ImageApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Images.push(new ClientModel.Image().LoadFromServerEntity(sEntity));
                }
                this.ImagesLoaded = true;
                this.InitializeModel();
            });
        ServerApi.AlbumApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Albums.push(new ClientModel.Album().LoadFromServerEntity(sEntity));
                }
                this.AlbumsLoaded = true;
                this.InitializeModel();
            });
        //ServerApi.WaypointTackApi.GetDefault().Get().done(d => {
        //    for (let sEntity of d) { this.WaypointTacks.push(new ClientModel.WaypointTack().LoadFromServerEntity(sEntity)); }
        //    this.WaypointTacksLoaded = true;
        //    this.InitialozeModel();
        //});
        ServerApi.TackApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Tacks.push(new ClientModel.Tack().LoadFromServerEntity(sEntity));
                }
                this.TacksLoaded = true;
                this.InitializeModel();
            });
        ServerApi.LocationApi.GetDefault()
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    if (sEntity.Type === ServerModel.Location.GetType())
                        this.Locations.push(new ClientModel.Location().LoadFromServerEntity(sEntity));
                    else if (sEntity.Type === ServerModel.Restaurant.GetType())
                        this.Restaurants.push(new ClientModel.Restaurant().LoadFromServerEntity(sEntity));
                    else if (sEntity.Type === ServerModel.Supermarket.GetType())
                        this.Supermarkets.push(new ClientModel.Supermarket().LoadFromServerEntity(sEntity));
                }
                this.LocationsLoaded = true;
                this.InitializeModel();
            });
    }

    InitializeModel() {
        if (this.WaypointsLoaded &&
            this.WaypointConnectionsLoaded &&
            this.PersonsLoaded &&
            this.JobsLoaded &&
            this.TripsLoaded &&
            this.AddressesLoaded &&
            this.ImagesLoaded &&
            this.AlbumsLoaded &&
            //this.WaypointTacksLoaded &&
            this.TacksLoaded &&
            this.LocationsLoaded) {
            for (let entity of this.Jobs()) {
                if (entity.AssignedToId() !== undefined)
                    entity.AssignedTo(this.GetPersonById(entity.AssignedToId()));
                if (entity.TripId() !== undefined)
                    entity.Trip(this.GetTripById(entity.TripId()));
                if (entity.SuperJobId() !== undefined) {
                    entity.SuperJob(this.GetJobById(entity.SuperJobId()));
                    entity.SuperJob().SubJobs.push(entity);
                }
            }
            for (let entity of this.Harbours()) {
                entity.Album(this.GetAlbumById(entity.AlbumId()));
            }
            for (let entity of this.Locations()) {
                entity.Address(this.GetAddressById(entity.AddressId()));
                this.GetHarbourById(entity.HarbourId()).Locations.push(entity);
            }
            for (let entity of this.Images()) {
                this.GetAlbumById(entity.ParentAlbumId()).Images.push(entity);
            }
            for (let connection of mapViewModel.WaypointConnections()) {
                const polyline = mapViewModel.AddPolyline([
                    mapViewModel.GetWayPointById(connection.Waypoint1Id), mapViewModel
                        .GetWayPointById(connection.Waypoint2Id)
                ]);
                addDummyHandle(polyline);
            }
            $("#loadingOverlay").remove();
        }
    }

    InitializeMap() {
        mapViewModel.SelectedHarbour(undefined);
        for (let wp of mapViewModel.Waypoints()) {
            if (wp.marker !== undefined)
                mapViewModel.Map.removeLayer(wp.marker);
            mapViewModel.CreateMarker(MarkerType.Waypoint, wp);
        }
        for (let h of mapViewModel.Harbours()) {
            if (h.marker !== undefined)
                mapViewModel.Map.removeLayer(h.marker);
            mapViewModel.CreateMarker(MarkerType.Harbour, h);
        }
        for (let p of mapViewModel.Polylines) {
            if (p.DummyHandle.marker !== undefined)
                mapViewModel.Map.removeLayer(p.DummyHandle.marker);
            mapViewModel.CreateMarker(MarkerType.Dummy, p.DummyHandle);
        }
        if (mapViewModel.MapMode() === MapMode.Admin) {
            for (let p of mapViewModel.Polylines) {
                p.addTo(mapViewModel.Map);
            }
            mapViewModel.Map.contextmenu.enable();
        } else {
            for (let p of mapViewModel.Polylines) {
                mapViewModel.Map.removeLayer(p);
            }
            mapViewModel.Map.contextmenu.disable();
        }
    }

    Map: L.mapbox.Map;

    GetWaypointById(id: number): ClientModel.Waypoint {
        for (let entity of this.Waypoints()) {
            if (entity.Id() === id) return entity;
        }
        for (let entity of this.Harbours()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Waypoint with id " + id + " found";
    }

    GetHarbourById(id: number): ClientModel.Harbour {
        for (let entity of this.Harbours()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Harbour with id " + id + " found";
    }

    GetPersonById(id: number): ClientModel.Person {
        for (let entity of this.Persons()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Person with id " + id + " found";
    }

    GetJobById(id: number): ClientModel.Job {
        for (let entity of this.Jobs()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Job with id " + id + " found";
    }

    GetTripById(id: number): ClientModel.Trip {
        for (let entity of this.Trips()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Trip with id " + id + " found";
    }

    GetAddressById(id: number): ClientModel.Address {
        for (let entity of this.Addresses()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Address with id " + id + " found";
    }

    GetImageById(id: number): ClientModel.Image {
        for (let entity of this.Images()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Image with id " + id + " found";
    }

    GetTackById(id: number): ClientModel.Tack {
        for (let entity of this.Tacks()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Tack with id " + id + " found";
    }

    GetAlbumById(id: number): ClientModel.Album {
        for (let entity of this.Albums()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Tack with id " + id + " found";
    }

    GetLocationById(id: number): ClientModel.Location {
        for (let entity of this.Locations()) {
            if (entity.Id() === id) return entity;
        }
        for (let entity of this.Restaurants()) {
            if (entity.Id() === id) return entity;
        }
        for (let entity of this.Restaurants()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Location with id " + id + " found";
    }

    WaypointsLoaded = false;
    WaypointConnectionsLoaded = false;
    PersonsLoaded = false;
    JobsLoaded = false;
    TripsLoaded = false;
    AddressesLoaded = false;
    ImagesLoaded = false;
    AlbumsLoaded = false;
    WaypointTacksLoaded = false;
    TacksLoaded = false;
    LocationsLoaded = false;

    Waypoints = ko.observableArray<ClientModel.Waypoint>();
    WaypointConnections = ko.observableArray<ServerModel.WaypointConnection>();
    Harbours = ko.observableArray<ClientModel.Harbour>();
    Persons = ko.observableArray<ClientModel.Person>();
    Jobs = ko.observableArray<ClientModel.Job>();
    Trips = ko.observableArray<ClientModel.Trip>();
    Addresses = ko.observableArray<ClientModel.Address>();
    Images = ko.observableArray<ClientModel.Image>();
    Tacks = ko.observableArray<ClientModel.Tack>();
    Locations = ko.observableArray<ClientModel.Location>();
    Supermarkets = ko.observableArray<ClientModel.Supermarket>();
    Restaurants = ko.observableArray<ClientModel.Restaurant>();
    Albums = ko.observableArray<ClientModel.Album>();


    SelectedWaypoint = ko.observable<ClientModel.Waypoint>();
    SelectedHarbour = ko.observable<ClientModel.Harbour>();
    SelectedPerson = ko.observable<ClientModel.Person>();
    SelectedJob = ko.observable<ClientModel.Job>();
    SelectedTrip = ko.observable<ClientModel.Trip>();
    SelectedAddress = ko.observable<ClientModel.Address>();
    SelectedImage = ko.observable<ClientModel.Image>();
    SelectedTack = ko.observable<ClientModel.Tack>();
    SelectedLocation = ko.observable<ClientModel.Location>();
    SelectedSupermarket = ko.observable<ClientModel.Supermarket>();
    SelectedRestaurant = ko.observable<ClientModel.Restaurant>();

    InitGallery() {
        const items = new Array<PhotoSwipe.Item>();
        const currImage: ClientModel.Image = this as any;
        for (let data of mapViewModel.SelectedHarbour().Album().Images()) {
            items.push(({
                h: data.Height(),
                w: data.Width(),
                src: data.Path()
            } as any));
        }
        gallery = new PhotoSwipe(pswp,
            PhotoSwipeUI_Default,
            items,
            {
                index: mapViewModel.SelectedHarbour().Album().Images.indexOf(currImage) as number,
                getThumbBoundsFn: (index: number): { x: number; y: number; w: number } => {
                    const elem = $(".images:first img")[index];
                    var padding = parseFloat(window.getComputedStyle(elem, null)
                        .getPropertyValue("padding-left")
                        .replace("px", ""));
                    elem.scrollIntoView();
                    const bounds = elem.getBoundingClientRect();
                    return {
                        x: bounds.left + padding,
                        y: bounds.top + window.screenY + padding,
                        w: bounds.width - (2 * padding)
                    };
                }
            });
        gallery.init();
    }

    AddHarbour(): void {
        const harbour = mapViewModel.CreateHarbour(`Hafen ${this.Harbours.length}`, this.Map.getCenter());
        mapViewModel.Harbours.push(harbour);
        harbour.SaveToServer();
    }

    RemoveHarbour = () => {
        mapViewModel.SelectedWaypoint().RemoveFromMap();
        mapViewModel.Waypoints.remove(this.SelectedWaypoint());
    };
    RemoveWaypoint = () => {
        mapViewModel.SelectedHarbour().RemoveFromMap();
        mapViewModel.Harbours.remove(this.SelectedHarbour());
        mapViewModel.Harbours.remove(this.SelectedHarbour());
    };

    //CopyHarbour(h1: Harbour, h2: Harbour): void {
    //    this.CopyWaypoint(h1, h2);
    //}

    //CopyWaypoint(w1: Waypoint, w2: Waypoint) {
    //    w2.WaypointNumber(w1.WaypointNumber());
    //    w2.Latitude(w1.Latitude());
    //    w2.Longitude(w1.Longitude());
    //    w2.Name(w1.Name());
    //    w2.Description(w1.Description());
    //}

    AddPolyline(waypoint?: Waypoint): L.Polyline;
    AddPolyline(waypoints?: Waypoint[]): L.Polyline;
    AddPolyline(arg?): L.Polyline {
        const polyline = new L.Polyline([]);
        mapViewModel.Polylines.push(polyline);
        if (mapViewModel.MapMode() === MapMode.Admin)
            polyline.addTo(this.Map);
        polyline.Waypoints = new Array();
        if (arg !== undefined)
            if (arg instanceof Waypoint)
                (arg as Waypoint).AddToPolyline(polyline);
            else
                for (let waypoint of arg as Waypoint[]) {
                    waypoint.AddToPolyline(polyline);
                }
        polyline.addEventListener("mouseover",
            () => {
                mapViewModel.HoveredPolyine = polyline;
            });
        return polyline;
    }


    MapMode = ko.observable<MapMode>();
    DrawingLatLng: L.LatLng;
    DrawingSourceWaypoint: Waypoint;
    DrawingTargetWaypoint: Waypoint;
    RemovePolyline = (polyline: L.Polyline) => {
        this.Map.removeLayer(polyline);
        this.DrawingPolyline = undefined;
    };

    GetMapMode(): MapMode {
        if (this.DrawingPolyline !== undefined && this.DrawingLatLng !== undefined)
            return MapMode.RouteDrawing;
        return this.MapMode();
    }

    GetWayPointById(id: number): Waypoint {
        for (let waypoint of this.Waypoints()) {
            if (waypoint.Id() === id)
                return waypoint;
        }
        for (let waypoint of this.Harbours()) {
            if (waypoint.Id() === id)
                return waypoint;
        }
        throw `No Waypoint with id ${id} in model`;
    }

    CalculateDistances(start = mapViewModel.SelectedHarbour(), target?: ClientModel.Waypoint) {

        const waypoints: Array<Waypoint> = [start];
        const calculating = new Array<WaypointDistance>();
        const calculated = new Array<WaypointDistance>();
        const calculateRoute = target !== undefined;
        calculating.push(new WaypointDistance(undefined, start, 0, waypoints, calculateRoute));
        //for (let waypoint of mapViewModel.Waypoints()) {
        //    waypoints.push(new WaypointDistance(null, waypoint, Number.POSITIVE_INFINITY));
        //}
        //for (let harbour of mapViewModel.Harbours()) {
        //    if (harbour !== start) {
        //        waypoints.push(new WaypointDistance(null, harbour, Number.POSITIVE_INFINITY));
        //    }
        //}
        if (calculateRoute) {
            for (let wp of mapViewModel.Waypoints()) {
                wp.RoutePrecessor(undefined);
            }
            for (let h of mapViewModel.Harbours()) {
                h.RoutePrecessor(undefined);
            }
        } else {
            for (let wp of mapViewModel.Waypoints()) {
                wp.Precessor(undefined);
            }
            for (let h of mapViewModel.Harbours()) {
                h.Precessor(undefined);
            }
        }
        while (calculating.length > 0) {
            let minimalDist = Number.POSITIVE_INFINITY;
            let minimalWP: WaypointDistance;
            for (let wp of calculating) {
                for (let cWP of wp.ConnectedWayPoints) {
                    if ((calculateRoute ? cWP.RoutePrecessor() : cWP.Precessor()) !== undefined)
                        removeFromArray(wp.ConnectedWayPoints, cWP);
                }
                if (wp.ConnectedWayPoints.length === 0) {
                    removeFromArray(calculating, wp);
                    calculated.push(wp);
                } else {
                    const dist = wp.Distance + wp.ConnectedWayPoints[0].LatLng.distanceTo(wp.LatLng);
                    if (dist < minimalDist) {
                        minimalDist = dist;
                        minimalWP = wp;
                    }
                }
            }
            if (minimalWP !== undefined) {
                calculating.push(new WaypointDistance(minimalWP.Waypoint,
                    minimalWP.ConnectedWayPoints.shift(),
                    minimalDist,
                    waypoints,
                    calculateRoute));
                //if (minimalWP.Waypoint === target)
                //    break;
            }
        }
        if (calculateRoute)
            for (let wp of calculated) {
                wp.Waypoint.RouteDistance(Math.round(wp.Distance / 100) / 10);
                //wp.Waypoint.Precessor(wp.Precessor);
            }
        else
            for (let wp of calculated) {
                wp.Waypoint.Distance(Math.round(wp.Distance / 100) / 10);
                //wp.Waypoint.Precessor(wp.Precessor);
            }
    }

    private highlightedRoute: L.Polyline;
    private routeFixed = false;
    private previousBounds: L.LatLngBounds;
    private noRevertToPreviousBounds = false;

    ShowRoute(h: ClientModel.Waypoint) {
        if (mapViewModel.highlightedRoute !== undefined) {
            mapViewModel.routeFixed = false;
            mapViewModel.HideRoute();
        }
        if (h === undefined)
            h = (this as any);
        if (!(h instanceof ClientModel.Harbour))
            return;
        const latLngs = [h.LatLng];
        let dist = h.Distance();
        if (dist === undefined)
            dist = 0;
        while (h.Precessor() !== undefined) {
            h = h.Precessor();
            latLngs.push(h.LatLng);
        }
        mapViewModel.highlightedRoute = L.polyline(latLngs);
        mapViewModel.highlightedRoute.addTo(mapViewModel.Map);
        mapViewModel.highlightedRoute.bindLabel(dist.toString() + " km", { noHide: true });
        mapViewModel.FitBounds(mapViewModel.highlightedRoute.getBounds());
    }

    FitBounds(bounds: L.LatLngBounds) {
        const map = mapViewModel.Map;
        const currentBounds = map.getBounds();
        if (!currentBounds.contains(bounds)) {
            if (mapViewModel.previousBounds === undefined)
                mapViewModel.previousBounds = currentBounds;
            map.fitBounds(bounds);
        }
    }

    HideRoute(force = false) {
        if ((!mapViewModel.routeFixed || force) && mapViewModel.highlightedRoute !== undefined) {
            mapViewModel.routeFixed = false;
            mapViewModel.Map.removeLayer(mapViewModel.highlightedRoute);
            mapViewModel.highlightedRoute = undefined;
            if (!mapViewModel.noRevertToPreviousBounds && mapViewModel.previousBounds !== undefined) {
                const tmpBounds = mapViewModel.previousBounds;
                mapViewModel.previousBounds = undefined;
                window.setTimeout(() => {
                    if (mapViewModel.previousBounds === undefined)
                        mapViewModel.Map.fitBounds(tmpBounds);
                    else
                        mapViewModel.previousBounds = tmpBounds;
                }, 100);
            }
        }
    }

    FixRoute() {
        mapViewModel.routeFixed = true;
        mapViewModel.previousBounds = undefined;
    }

    CreateWaypoint(latLng: L.LatLng, markerType: MarkerType): Waypoint {
        const wp = new Waypoint(latLng, markerType, mapViewModel.Map as L.mapbox.Map);
        this.InitializeWaypoint(wp, markerType);
        return wp;
    }

    InitializeWaypoint(wp: Waypoint, markerType: MarkerType) {
        this.CreateMarker(markerType, wp);
    }

    CreateMarker(markerType: MarkerType, wp: ClientModel.Waypoint) {
        if (mapViewModel.MapMode() === MapMode.Admin || markerType === MarkerType.Harbour) {
            const options: L.MarkerOptions = {
                draggable: (mapViewModel.MapMode() === MapMode.Admin)
            };
            if (markerType === MarkerType.Dummy) {
                options.opacity = 0;
            }
            if (mapViewModel.MapMode() === MapMode.Admin &&
                (markerType === MarkerType.Waypoint || markerType === MarkerType.Dummy)) {
                options.icon = new L.Icon({
                    iconUrl: "/images/waypointhandle.png",
                    iconSize: new L.Point(10, 10, true),
                    className: "waypoint"
                });

            }
            if (mapViewModel.MapMode() === MapMode.Admin) {
                options.contextmenu = true;
                options.contextmenuInheritItems = false;
                if (markerType === MarkerType.Harbour) {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.EditingHarbour(this) }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.DeletingHarbour(this) }
                        }
                    ];
                } else {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.EditingWaypoint(this) }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.DeletingWaypoint(this) }
                        }
                    ];
                }
            }

            const marker = new L.Marker(wp.LatLng, options);
            marker.addTo(this.Map);
            marker.Waypoint = wp;
            wp.marker = marker;
            if (mapViewModel.MapMode() === MapMode.Admin) {
                if (markerType === MarkerType.Dummy)
                    marker.addEventListener("mouseout", (e) => {
                        if (e.target.Waypoint.IsDummy()) {
                            mapViewModel.HoveredPolyine = undefined;
                        }
                    })
                marker.addEventListener("drag",
                    (e: L.LeafletMouseEvent) => {
                        wp.SetLatLng(wp.marker.getLatLng());
                    });
                if (markerType === MarkerType.Waypoint || markerType === MarkerType.Dummy) {
                    this.WaypointMarkers.push(wp.marker);
                    wp.marker.Point = mapViewModel.Map.latLngToContainerPoint(wp.LatLng);
                }
                wp.marker.addEventListener("click",
                    (e: L.LeafletMouseEvent) => {
                        if (wp.IsDummy()) {
                            mapViewModel.Waypoints.push(wp);
                            wp.convertFromDummyHandle();
                        }
                        if (mapViewModel.GetMapMode() === MapMode.RouteDrawing) {
                            if (!wp.IsInPolyline(mapViewModel.DrawingPolyline)) {
                                ServerApi.WaypointConnectionApi.GetDefault()
                                    .Connect(wp.Id(), mapViewModel.DrawingPolyline.Waypoints[0].Id());
                                wp.AddToPolyline(mapViewModel.DrawingPolyline);
                                removeFromPolyline(mapViewModel.DrawingPolyline, mapViewModel.DrawingLatLng);
                                addDummyHandle(mapViewModel.DrawingPolyline);
                                mapViewModel.DrawingPolyline = undefined;
                                mapViewModel.DrawingLatLng = undefined;
                            } else {
                                removePolyline(mapViewModel.DrawingPolyline);
                                mapViewModel.DrawingPolyline = undefined;
                                mapViewModel.DrawingLatLng = undefined;
                            }
                        }
                    });
                wp.marker.addEventListener("dblclick",
                    (e: L.LeafletMouseEvent) => {
                        mapViewModel.DrawingPolyline = mapViewModel.AddPolyline(wp);
                        mapViewModel.DrawingLatLng = new L.LatLng(e.latlng.lat, e.latlng.lng);
                        mapViewModel.DrawingPolyline.addLatLng(mapViewModel.DrawingLatLng);
                    });
                if (markerType === MarkerType.Dummy)
                    wp.marker.addOneTimeEventListener("drag",
                        (e: L.LeafletMouseEvent) => {
                            wp.convertFromDummyHandle();
                            mapViewModel.Waypoints.push(wp);
                        });
                //else if (markerType === MarkerType.Waypoint) {
                //    wp.Name(`Wegpunkt ${mapViewModel.Waypoints().length + 1}`);
                //}
                wp.marker.addEventListener("dragend",
                    (e: L.LeafletMouseEvent) => {
                        wp.SaveToServer();
                    });
            } else if (markerType === MarkerType.Harbour) {
                wp.marker.addEventListener("mouseover",
                    () => {
                        if (mapViewModel.SelectedHarbour() !== undefined)
                            mapViewModel.ShowRoute(wp);
                    });
                wp.marker.addEventListener("click", () => mapViewModel.SelectedHarbour(wp as ClientModel.Harbour));
            }
        }
    }

    CreateHarbour(name: string, latLng: L.LatLng) {
        const h = new Harbour(name, latLng, this.Map as L.mapbox.Map);
        this.InitializeWaypoint(h, MarkerType.Harbour);
        return h;
    }

    SaveHarbour() {
        const harbour: ClientModel.Harbour = this as any;
        if (harbour.Id() === undefined) {
            mapViewModel.Harbours.push(harbour);
        }
        harbour.SaveToServer()
            .done(() => {
                mapViewModel.EditingHarbour(undefined);
            });
    }

    DeleteHarbour() {
        var h = mapViewModel.DeletingHarbour();
        ServerApi.WaypointConnectionApi.GetDefault()
            .Disconnect(h.Id())
            .done(() => {
                h.DeleteOnServer()
                    .done(() => {
                        h.RemoveFromMap();
                        mapViewModel.Harbours.remove(h);
                        mapViewModel.DeletingHarbour(undefined);
                    });
            });
    }

    SaveWaypoint() {
        const waypoint: ClientModel.Waypoint = this as any;
        waypoint.SaveToServer()
            .done(() => {
                mapViewModel.EditingWaypoint(undefined);
            });
    }

    DeleteWaypoint() {
        var wp = mapViewModel.DeletingWaypoint();
        ServerApi.WaypointConnectionApi.GetDefault()
            .Disconnect(wp.Id())
            .done(() => {
                wp.DeleteOnServer()
                    .done(() => {
                        wp.RemoveFromMap();
                        mapViewModel.Waypoints.remove(wp);
                        mapViewModel.DeletingWaypoint(undefined);
                    });
            });
    };


    SaveJob() {
        const job: ClientModel.Job = this as any;
        const newJob = job.Id() === undefined;
        job.SaveToServer()
            .done(() => {
                if (newJob) {
                    mapViewModel.Jobs.push(mapViewModel.EditingJob());
                    if (mapViewModel.EditingJob().SuperJobId() !== undefined)
                        mapViewModel.GetJobById(mapViewModel.EditingJob().SuperJobId()).SubJobs.push(mapViewModel.EditingJob());
                }
                mapViewModel.EditingJob(undefined);
            });
    }

    DeleteJob() {
        const job = mapViewModel.DeletingJob();
        job.DeleteOnServer()
            .done(() => {
                mapViewModel.Jobs.remove(job);
                if (job.SuperJobId() !== undefined)
                    mapViewModel.GetJobById(job.SuperJobId()).SubJobs.remove(job);
                mapViewModel.DeletingJob(undefined);
            });
    }


    DrawingPolyline: L.Polyline;
    Polylines = new Array<L.Polyline>();
    EditingHarbour = ko.observable<Harbour>();
    DeletingHarbour = ko.observable<Harbour>();
    EditingWaypoint = ko.observable<Waypoint>();
    DeletingWaypoint = ko.observable<Waypoint>();
    DeletingJob = ko.observable<Job>();
    EditingJob = ko.observable<Job>();
    WaypointMarkers = new Array();
    HoveredPolyine: L.Polyline;

}

var mapViewModel = new MapViewModel(MapMode.View);
ko.applyBindings(mapViewModel);
var dropzoneModalOpenedByDrag = false;

var dropzoneModal = $("#dropzoneModal");
var editingHarbourModal = $("#editingHarbourModal");
var deletingHarbourModal = $("#deletingHarbourModal");
var editingWaypointModal = $("#editingWaypointModal");
var deletingWaypointModal = $("#deletingWaypointModal");
var deletingJobModal = $("#deletingJobModal");
var editingJobModal = $("#editingJobModal");
var jobOverviewModal = $("#jobOverviewModal");
var dropzone: Dropzone;
var hasDrag = false;
var uploadModalVisible = false;
var pswp = $(".pswp")[0];

var leftSidebar = new Sidebar($("#leftSidebar"));
var rightSidebar = new Sidebar($("#rightSidebar"));
var bottomSidebar = new Sidebar($("#bottomSidebar"));
var harbourInfo = $("#harbourInfo");
Dropzone.options.dropzone =
    {
        acceptedFiles: "image/jpeg,image/png",
        dictInvalidFileType: "Dieser Dateityp wird nicht unterstützt",
        dictDefaultMessage: "Dateien hier ablegen",
        init() {
            dropzone = this;
            dropzone.on("success",
                (e, data: ServerModel.Image) => {
                    var image = new ClientModel.Image().LoadFromServerEntity(data);
                    mapViewModel.Images.push(image);
                    mapViewModel.GetAlbumById(image.ParentAlbumId()).Images.push(image);
                });
            dropzone.on("queuecomplete",
                () => {
                    if (dropzoneModalOpenedByDrag)
                        dropzoneModal.modal("hide");
                });
            dropzone.on("dragover",
                () => {
                    hasDrag = true;
                });
        }
    };

document.ondragenter =
    (e: DragEvent) => {
        if (!uploadModalVisible &&
            !hasDrag &&
            !dropzoneModalOpenedByDrag &&
            dropzoneModal.is(":not(.in)") &&
            e.dataTransfer.types[0] === "Files" &&
            mapViewModel.SelectedHarbour() !== undefined) {
            dropzoneModal.modal("show");
            uploadModalVisible = true;
            dropzoneModalOpenedByDrag = true;
        }

        hasDrag = true;
        e.preventDefault();
        e.stopPropagation();
    };
document.ondragover =
    (e: DragEvent) => {
        hasDrag = true;
    };
document.ondragleave =
    (e: DragEvent) => {
        if (uploadModalVisible && hasDrag && dropzoneModalOpenedByDrag && dropzone.getQueuedFiles().length === 0 ||
            dropzone.getUploadingFiles().length === 0) {
            hasDrag = false;
            window.setTimeout(() => {
                if (!hasDrag) {
                    dropzoneModal.modal("hide");
                    uploadModalVisible = false;
                }
            },
                1000);
        }

        e.preventDefault();
        e.stopPropagation();
    };
dropzoneModal.on("hide.bs.modal",
    e => {
        if (dropzone.getQueuedFiles().length > 0 || dropzone.getUploadingFiles().length > 0) {
            e.preventDefault();
            e.stopImmediatePropagation();
            alert("Das Fenster kann nicht geschlossen werden, während Dateien hochgeladen werden.");
            return false;
        } else {
            dropzone.removeAllFiles();
            dropzoneModalOpenedByDrag = false;
        }
    });
var gallery: PhotoSwipe<PhotoSwipe.Options>;

$(".modal").on("hidden.bs.modal", function (event) {
    $(this).removeClass("fv-modal-stack");
    $("body").data("fv_open_modals", $("body").data("fv_open_modals") - 1);
});


$(".modal").on("shown.bs.modal", function (event) {

    // keep track of the number of open modals

    if (typeof ($("body").data("fv_open_modals")) == "undefined") {
        $("body").data("fv_open_modals", 0);
    }


    // if the z-index of this modal has been set, ignore.

    if ($(this).hasClass("fv-modal-stack")) {
        return;
    }

    $(this).addClass("fv-modal-stack");

    $("body").data("fv_open_modals", $("body").data("fv_open_modals") + 1);

    $(this).css("z-index", 1040 + (10 * $("body").data("fv_open_modals")));

    $(".modal-backdrop").not(".fv-modal-stack")
        .css("z-index", 1039 + (10 * $("body").data("fv_open_modals")));


    $(".modal-backdrop").not("fv-modal-stack")
        .addClass("fv-modal-stack");

});


