/// <reference path="../../clientmodel.ts" />
/// <reference path="../../servermodel.ts" />
/// <reference path="../../serverapi.ts" />
/// <reference path="../../../../typings/browser/definitions/moment/index.d.ts" />

import Waypoint = ClientModel.Waypoint;
import Harbour = ClientModel.Harbour;
import Job = ClientModel.Job;
import WaypointDistance = ClientModel.WaypointDistance;

var ctrlPressed = false;

if (/Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent)) {
    $("body").addClass("mobile");
}

function renderTime(duration: number);
function renderTime(startDate: Date, endDate: Date);
function renderTime(startDate: Date | number, endDate?: Date) {
    if (startDate instanceof Date)
        return renderTime(endDate.getTime() - startDate.getTime());
    const duration = <number>startDate;
    var time = Math.floor(duration / 60000);
    var mins = (time % 60).toString();
    if (mins.length === 1)
        mins = "0" + mins;
    time = Math.floor(time / 60);
    return time.toString() + ":" + mins;
}

function getMiddle(pol: L.Polyline): L.LatLng {
    const start = pol.getLatLngs()[0];
    const end = pol.getLatLngs()[1];
    //if (end === undefined)
    //    return start;
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

class EditingHelper<T extends ClientModel.Entity> {

    constructor(editingModalId: string, deletingModalId: string, Factory: () => T, Dataset: KnockoutObservableArray<T>, detailModalId: string);
    constructor(editingModalId: string, deletingModalId: string, Factory: () => T, Dataset: KnockoutObservableArray<T>, detailedSidebar: Sidebar);
    constructor(editingModalId: string, deletingModalId: string, Factory: () => T, Dataset: KnockoutObservableArray<T>);
    constructor(editingModalId: string, deletingModalId: string, protected Factory: () => T, protected Dataset: KnockoutObservableArray<T>, detailModalId?: string | Sidebar) {
        this.EditingModal = $(`#${editingModalId}`);
        this.DeletingModal = $(`#${deletingModalId}`);

        if ($("form:first").length === 1)
            this.Parsley = $("form:first", this.EditingModal).parsley(window.ParsleyConfig);
        this.EditingModal.on("show.bs.modal",
            () => {
                this.EditingModalOpen = true;
                if (this.Editing() === undefined)
                    this.Editing(this.Factory());
                mapViewModel.AlbumStack.unshift(this.Editing().Album());
            });

        this.EditingModal.on("shown.bs.modal",
            () => {
                window.setTimeout(() => $("input, select, textarea", this.EditingModal).first().focus(), 200);
            });

        this.EditingModal.on("hidden.bs.modal",
            () => {
                if (this.Editing() !== undefined)
                    this.Editing(undefined);
                this.EditingModalOpen = false;
                mapViewModel.AlbumStack.shift();
            });
        this.Editing.subscribe((entity) => {
            if (entity === undefined && this.EditingModalOpen) {
                this.EditingModal.modal("hide");
            } else if (!this.EditingModalOpen) {
                entity.SaveState();
                this.EditingModal.modal("show");
            }
        });
        this.Editing.subscribe(() => {
            if (this.Editing() !== undefined) {
                this.Editing().RevertState(true);
            }
        },
            this,
            "beforeChange");

        this.DeletingModal.on("show.bs.modal",
            () => {
                this.DeletingModalOpen = true;
                mapViewModel.AlbumStack.unshift(undefined);
            });

        this.DeletingModal.on("hidden.bs.modal",
            () => {
                if (this.Deleting() !== undefined)
                    this.Deleting(undefined);
                mapViewModel.AlbumStack.shift();
                this.DeletingModalOpen = false;
            });
        this.Deleting.subscribe((entity) => {
            if (entity === undefined && this.DeletingModalOpen) {
                this.DeletingModal.modal("hide");
            } else if (!this.DeletingModalOpen) {
                this.DeletingModal.modal("show");
            }
        });

        if (detailModalId !== undefined) {
            if (detailModalId instanceof Sidebar) {
                this.DetailSidebar = detailModalId;
                this.Detail.subscribe((entity) => {
                    if (entity === undefined && this.DetailSidebar.IsActiv()) {
                        mapViewModel.AlbumStack.shift();
                        this.DetailSidebar.Hide();
                    } else if (entity !== undefined && !this.DetailSidebar.IsActiv()) {
                        this.DetailSidebar.Show();
                        mapViewModel.AlbumStack.unshift(entity.Album());
                    }
                });
            } else {
                this.DetailModal = $(`#${detailModalId}`);
                this.Detail.subscribe((entity) => {
                    if (entity === undefined && this.DetailModalOpen) {
                        this.DetailModal.modal("hide");
                    } else if (entity !== undefined && !this.DetailModalOpen) {
                        this.DetailModal.modal("show");
                    }
                });

                this.DetailModal.on("show.bs.modal",
                    () => {
                        this.DetailModalOpen = true;
                        mapViewModel.AlbumStack.unshift(this.Detail().Album());
                    });

                this.DetailModal.on("hide.bs.modal",
                    () => {
                        this.DetailModalOpen = false;
                        mapViewModel.AlbumStack.shift();
                    });
            }
        }

    }

    protected EditingModal: JQuery;
    protected DeletingModal: JQuery;
    protected DetailModal: JQuery;
    protected DetailSidebar: Sidebar;
    protected EditingModalOpen = false;
    protected DeletingModalOpen = false;
    protected DetailModalOpen = false;
    protected Parsley: any;

    Detail = ko.observable<T>();
    Editing = ko.observable<T>();
    Deleting = ko.observable<T>();

    Delete = () => {
        this.Deleting()
            .DeleteOnServer()
            .done(() => {
                //this.Dataset.remove(this.Deleting());
                this.Deleting(undefined);
                if (this.Detail() !== undefined)
                    this.Detail(undefined);
                if (this.Editing() !== undefined)
                    this.Editing(undefined);
            });
    };

    Save = () => {
        if (this.Parsley !== undefined)
            this.Parsley.whenValidate()
                .done(() => {
                    var isNew = this.Editing().Id() === undefined;
                    this.Editing()
                        .SaveToServer()
                        .done(() => {
                            //if (isNew)
                            //    this.Dataset.push(this.Editing());
                            this.Editing(undefined);
                        });
                });
        else {
            var isNew = this.Editing().Id() === undefined;
            this.Editing()
                .SaveToServer()
                .done(() => {
                    //if (isNew)
                    //    this.Dataset.push(this.Editing());
                    this.Editing(undefined);
                });
        }
    };
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
                        mapViewModel.HarbourHelper.Editing(mapViewModel.CreateHarbour("", e.latlng));
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
        $.get("/Account/LoggedIn").done((data) => this.IsLoggedIn(data));
        this.ContentPages.subscribe((data) => {
            var nav = $("#leftNav");
            $(".contentPageLink", nav).remove();
            for (let cP of data) {
                $(`<li role="presentation" class="contentPageLink"><a href="#">${cP.Title()}</a></li>`).click(() => {
                    mapViewModel.ContentPageHelper.Detail(cP);
                    return false;
                }).appendTo(nav);
            }
        });

        this.HarbourHelper.Detail.subscribe((newHarbour) => {
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
        this.HarbourHelper.Editing.subscribe((harbour) => {
            if (harbour !== undefined && harbour.Id() === undefined)
                mapViewModel.Map.removeLayer(harbour.marker);
        },
            this,
            "beforeChange");

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
                            ServerApi.WaypointConnections
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

    IsLoggedIn = ko.observable(false);

    private routePolyline = ko.observable<L.Polyline>();

    StartRoute() {
        const trip = new ClientModel.Trip();
        const tack = new ClientModel.Tack();
        const harbour = mapViewModel.HarbourHelper.Detail();
        tack.Start(harbour);
        trip.Tacks.push(tack);
        mapViewModel.TripHelper.Editing(trip);
        mapViewModel.routePolyline(L.polyline([],
            {
                color: "#009900"
            }));
        mapViewModel.routePolyline().addTo(mapViewModel.Map);
    }

    IsLastTakInRoute = ko.computed({
        read: () => {
            var trip = mapViewModel.TripHelper.Editing();
            var h = mapViewModel.HarbourHelper.Detail();
            return trip !== undefined && h !== undefined && trip.Tacks()[trip.Tacks().length - 1].Start() === h;
        },
        deferEvaluation: true
    });

    GetRouteDistance = ko.computed({
        read: () => {
            var distance = 0;
            for (let tack of mapViewModel.TripHelper.Editing().Tacks()) {
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
        const trip = mapViewModel.TripHelper.Editing();
        const targetHarbour = mapViewModel.HarbourHelper.Editing();
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
        for (let tack of mapViewModel.TripHelper.Editing().Tacks()) {
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
        const tacks = mapViewModel.TripHelper.Editing().Tacks;
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
        const tacks = mapViewModel.TripHelper.Editing().Tacks;
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
        const tacks = mapViewModel.TripHelper.Editing().Tacks;
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
        ServerApi.Waypoints
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    if (sEntity.Type === "Waypoint")
                        this.Waypoints.push(mapViewModel
                            .CreateWaypoint(MarkerType.Waypoint)
                            .LoadFromServerEntity(sEntity));
                    else if (sEntity.Type === "Harbour") {
                        const harbour = mapViewModel
                            .CreateHarbour()
                            .LoadFromServerEntity(sEntity);
                        this.Harbours.push(harbour);
                    }
                }

                this.WaypointsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.WaypointConnections
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.WaypointConnections.push(sEntity);
                }
                this.WaypointConnectionsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Persons
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Persons.push(new ClientModel.Person().LoadFromServerEntity(sEntity));
                }
                this.PersonsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Jobs
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Jobs.push(new ClientModel.Job().LoadFromServerEntity(sEntity));
                }
                this.JobsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Trips
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Trips.push(new ClientModel.Trip().LoadFromServerEntity(sEntity));
                }
                this.TripsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Addresses
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Addresses.push(new ClientModel.Address().LoadFromServerEntity(sEntity));
                }
                this.AddressesLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Images
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Images.push(new ClientModel.Image().LoadFromServerEntity(sEntity));
                }
                this.ImagesLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Albums
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Albums.push(new ClientModel.Album().LoadFromServerEntity(sEntity));
                }
                this.AlbumsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.LogBookEntries
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.LogBookEntries.push(new ClientModel.LogBookEntry().LoadFromServerEntity(sEntity));
                }
                this.LogBookEntriesLoaded = true;
                this.InitializeModel();
            });
        ServerApi.AlbumImages.Get()
            .done(d => {
                for (let ai of d) {
                    this.AlbumImages.push(ai);
                }
                this.AlbumImagesLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Crews.Get()
            .done(d => {
                for (let c of d) {
                    this.Crews.push(c);
                }
                this.CrewsLoaded = true;
                this.InitializeModel();
            });
        ServerApi.Wifis.Get()
            .done(d => {
                for (let c of d) {
                    this.Wifis.push(new ClientModel.Wifi().LoadFromServerEntity(c));
                }
                this.WifisLoaded = true;
                this.InitializeModel();
            });
        ServerApi.ContentPages.Get()
            .done(d => {
                for (let c of d) {
                    this.ContentPages.push(new ClientModel.ContentPage().LoadFromServerEntity(c));
                }
                this.ContentPagesLoaded = true;
                this.InitializeModel();
            });
        //ServerApi.WaypointTacks.Get().done(d => {
        //    for (let sEntity of d) { this.WaypointTacks.push(new ClientModel.WaypointTack().LoadFromServerEntity(sEntity)); }
        //    this.WaypointTacksLoaded = true;
        //    this.InitialozeModel();
        //});
        ServerApi.Tacks
            .Get()
            .done(d => {
                for (let sEntity of d) {
                    this.Tacks.push(new ClientModel.Tack().LoadFromServerEntity(sEntity));
                }
                this.TacksLoaded = true;
                this.InitializeModel();
            });
        //ServerApi.Locations
        //    .Get()
        //    .done(d => {
        //        for (let sEntity of d) {
        //            if (sEntity.Type === "Location")
        //                this.Locations.push(new ClientModel.Location().LoadFromServerEntity(sEntity));
        //            else if (sEntity.Type === "Restaurant")
        //                this.Restaurants.push(new ClientModel.Restaurant().LoadFromServerEntity(sEntity));
        //            else if (sEntity.Type === "Supermarket")
        //                this.Supermarkets.push(new ClientModel.Supermarket().LoadFromServerEntity(sEntity));
        //        }
        this.LocationsLoaded = true;
        //        this.InitializeModel();
        //    });
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
            this.LocationsLoaded &&
            this.CrewsLoaded &&
            this.LogBookEntriesLoaded &&
            this.AlbumImagesLoaded &&
            this.WifisLoaded &&
            this.ContentPagesLoaded) {
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
            for (let entity of this.AlbumImages()) {
                this.GetAlbumById(entity.AlbumId).Images.push(this.GetImageById(entity.ImageId));
            }
            for (let connection of mapViewModel.WaypointConnections()) {
                const polyline = mapViewModel.AddPolyline([
                    mapViewModel.GetWayPointById(connection.Waypoint1Id), mapViewModel
                        .GetWayPointById(connection.Waypoint2Id)
                ]);
                addDummyHandle(polyline);
            }
            for (let entry of mapViewModel.LogBookEntries()) {
                entry.Start(mapViewModel.GetHarbourById(entry.StartId()));
                entry.End(mapViewModel.GetHarbourById(entry.EndId()));
                entry.Album(mapViewModel.GetAlbumById(entry.AlbumId()));
            }
            for (let crew of mapViewModel.Crews()) {
                const lBE = mapViewModel.GetLogBookEntryById(crew.TackId);
                const tack = mapViewModel.GetTackById(crew.TackId);
                const trip = mapViewModel.GetTripById(crew.TackId);
                const p = mapViewModel.GetPersonById(crew.PersonId);
                if (lBE !== undefined)
                    lBE.Persons.push(p);
                else if (tack !== undefined)
                    tack.Persons.push(p);
                else if (trip !== undefined)
                    trip.Persons.push(p);
            }
            for (let wifi of mapViewModel.Wifis()) {
                var h = mapViewModel.GetHarbourById(wifi.HarbourId());
                h.Wifis.push(wifi);
                wifi.Harbour(h);
            }
            ko.applyBindings(mapViewModel);
            $("#loadingOverlay").remove();
        }
    }

    InitializeMap() {
        mapViewModel.HarbourHelper.Detail(undefined);
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
                //p.contextmenu.enable();
            }
            mapViewModel.Map.contextmenu.enable();
        } else {
            for (let p of mapViewModel.Polylines) {
                mapViewModel.Map.removeLayer(p);
                //p.contextmenu.disable();
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
        return undefined;
    }

    GetHarbourById(id: number): ClientModel.Harbour {
        for (let entity of this.Harbours()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Harbour with id " + id + " found";
        return undefined;
    }

    GetPersonById(id: number): ClientModel.Person {
        for (let entity of this.Persons()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Person with id " + id + " found";
        return undefined;
    }

    GetJobById(id: number): ClientModel.Job {
        for (let entity of this.Jobs()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Job with id " + id + " found";
        return undefined;
    }

    GetTripById(id: number): ClientModel.Trip {
        for (let entity of this.Trips()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Trip with id " + id + " found";
        return undefined;
    }

    GetAddressById(id: number): ClientModel.Address {
        for (let entity of this.Addresses()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Address with id " + id + " found";
        return undefined;
    }

    GetImageById(id: number): ClientModel.Image {
        for (let entity of this.Images()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Image with id " + id + " found";
        return undefined;
    }

    GetTackById(id: number): ClientModel.Tack {
        for (let entity of this.Tacks()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Tack with id " + id + " found";
        return undefined;
    }

    GetLogBookEntryById(id: number): ClientModel.LogBookEntry {
        for (let entity of this.LogBookEntries()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Tack with id " + id + " found";
        return undefined;
    }

    GetAlbumById(id: number): ClientModel.Album {
        for (let entity of this.Albums()) {
            if (entity.Id() === id) return entity;
        }
        //throw "No Tack with id " + id + " found";
        return undefined;
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
        return undefined;
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
    AlbumImagesLoaded = false;
    LogBookEntriesLoaded = false;
    CrewsLoaded = false;
    WifisLoaded = false;
    ContentPagesLoaded = false;

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
    AlbumImages = ko.observableArray<ServerModel.AlbumImage>();
    LogBookEntries = ko.observableArray<ClientModel.LogBookEntry>();
    Crews = ko.observableArray<ServerModel.Crew>();
    Wifis = ko.observableArray<ClientModel.Wifi>();
    ContentPages = ko.observableArray<ClientModel.ContentPage>();

    WaypointHelper = new EditingHelper("editingWaypointModal", "deletingWaypointModal", () => this.CreateWaypoint(MarkerType.Waypoint), this.Waypoints);
    HarbourHelper = new EditingHelper("editingHarbourModal", "deletingHarbourModal", () => this.CreateHarbour(), this.Harbours, rightSidebar);
    PersonHelper = new EditingHelper("editingPersonModal", "deletingPersonModal", () => new ClientModel.Person(), this.Persons);
    JobHelper = new EditingHelper("editingJobModal", "deletingJobModal", () => new ClientModel.Job(), this.Jobs);
    TripHelper = new EditingHelper("editingTripModal", "deletingTripModal", () => new ClientModel.Trip(), this.Trips);
    AddressHelper = new EditingHelper("editingAddressModal", "deletingAddressModal", () => new ClientModel.Address(), this.Addresses);
    ImageHelper = new EditingHelper("editingImageModal", "deletingImageModal", () => new ClientModel.Image(), this.Images);
    TackHelper = new EditingHelper("editingTackModal", "deletingTackModal", () => new ClientModel.Tack(), this.Tacks);
    LocationHelper = new EditingHelper("editingLocationModal", "deletingLocationModal", () => new ClientModel.Location(), this.Locations);
    SupermarketHelper = new EditingHelper("editingSupermarketModal", "deletingSupermarketModal", () => new ClientModel.Supermarket(), this.Supermarkets);
    RestaurantHelper = new EditingHelper("editingRestaurantModal", "deletingRestaurantModal", () => new ClientModel.Restaurant(), this.Restaurants);
    LogBookEntryHelper = new EditingHelper("editingLogBookEntryModal", "deletingLogBookEntryModal", () => new ClientModel.LogBookEntry(), this.LogBookEntries, "detailedLogBookEntryModal");
    ContentPageHelper = new EditingHelper("editingContentPageModal", "deletingContentPageModal", () => new ClientModel.ContentPage(), this.ContentPages, "detailedContentPageModal");
    WifiHelper = new EditingHelper("editingWifiModal", "deletingWifiModal", () => {
        const w = new ClientModel.Wifi();
        w.HarbourId(mapViewModel.HarbourHelper.Detail().Id());
        return w;
    }, this.Wifis, "detailWifiModal");

    HarboursByName = ko.computed<Harbour[]>(() => this.Harbours.sort((h1, h2) => h1.Name() > h2.Name()?1:-1)());
    HarboursByDistance = ko.computed<Harbour[]>(() => this.Harbours.sort((h1, h2) => h1.Distance() - h2.Distance())());
    LogBookEntriesByStartDate = ko.computed<ClientModel.LogBookEntry[]>(() => this.LogBookEntries.sort((l1, l2) => Date.parse(l1.StartDate()) - Date.parse(l2.StartDate()))());



    //SortedLogBookEntries = ko.computed({
    //    read: () => this.LogBookEntries.sort((l1, l2) => {
    //        var t1 = l1.StartDate().getTime();
    //        var t2 = l2.StartDate().getTime();
    //        return t2 - t1;
    //    }),
    //    deferEvaluation: true
    //});

    InitGallery(item: ClientModel.Image, event: JQueryEventObject) {
        const items = new Array<PhotoSwipe.Item>();
        const albumElem = event.target.parentElement;
        const currImage: ClientModel.Image = this as any;
        for (let data of mapViewModel.AlbumStack()[0].Images()) {
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
                index: mapViewModel.AlbumStack()[0].Images.indexOf(currImage) as number,
                getThumbBoundsFn: (index: number): { x: number; y: number; w: number } => {
                    const elem = $("img", albumElem)[index];
                    var padding = parseFloat(window.getComputedStyle(elem, null)
                        .getPropertyValue("padding-left")
                        .replace("px", ""));
                    elem.scrollIntoView(false);
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
        mapViewModel.HarbourHelper.Detail().DeleteOnServer();
    };
    RemoveWaypoint = () => {
        mapViewModel.WaypointHelper.Detail().DeleteOnServer();
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
        //var options = {
        //    contextmenu: true,
        //    contextmenuInheritItems: false,
        //    contextmenuItems: [
        //        {
        //            text: "FFFFFFFFFFFFFFFFFF",
        //            callback: function() { console.log(this);
        //                console.log(arguments);mapViewModel.HarbourHelper.Editing(this) }
        //        },
        //        {
        //            text: "Löschen",
        //            callback: function() { mapViewModel.HarbourHelper.Deleting(this) }
        //        }
        //    ]
        //};

        const polyline = new L.Polyline([]);

        //polyline.bindContextMenu(options);

        mapViewModel.Polylines.push(polyline);
        polyline.addEventListener("click", (e: L.LeafletMouseEvent) => {
            const p1 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[0]);
            const p2 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[1]);
            polyline.DummyHandle
                .SetLatLng(mapViewModel.Map
                    .containerPointToLatLng(L.LineUtil
                        .closestPointOnSegment(e.containerPoint,
                        p1,
                        p2)),
                false);

            mapViewModel.Waypoints.push(polyline.DummyHandle);
            polyline.DummyHandle.convertFromDummyHandle();
        });
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

    CalculateDistances(start = mapViewModel.HarbourHelper.Detail(), target?: ClientModel.Waypoint) {

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
            let minimalWp: WaypointDistance;
            for (let wp of calculating) {
                for (let cWp of wp.ConnectedWayPoints) {
                    if ((calculateRoute ? cWp.RoutePrecessor() : cWp.Precessor()) !== undefined)
                        removeFromArray(wp.ConnectedWayPoints, cWp);
                }
                if (wp.ConnectedWayPoints.length === 0) {
                    removeFromArray(calculating, wp);
                    calculated.push(wp);
                } else {
                    const dist = wp.Distance + wp.ConnectedWayPoints[0].LatLng.distanceTo(wp.LatLng) / 1.852;
                    if (dist < minimalDist) {
                        minimalDist = dist;
                        minimalWp = wp;
                    }
                }
            }
            if (minimalWp !== undefined) {
                calculating.push(new WaypointDistance(minimalWp.Waypoint,
                    minimalWp.ConnectedWayPoints.shift(),
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
        if (dist === undefined || dist === null)
            dist = 0;
        while (h.Precessor() !== undefined) {
            h = h.Precessor();
            latLngs.push(h.LatLng);
        }
        mapViewModel.highlightedRoute = L.polyline(latLngs);
        mapViewModel.highlightedRoute.addTo(mapViewModel.Map);
        mapViewModel.highlightedRoute.bindLabel(dist.toString() + " sm", { noHide: true });
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

    CreateWaypoint(markerType: MarkerType): Waypoint;
    CreateWaypoint(latLng: L.LatLng, markerType: MarkerType): Waypoint;
    CreateWaypoint(latLng?: L.LatLng | MarkerType, markerType?: MarkerType): Waypoint {
        let wp: Waypoint;
        if (markerType !== undefined)
            wp = new Waypoint(<L.LatLng>latLng, markerType, mapViewModel.Map as L.mapbox.Map);
        else
            wp = new Waypoint(markerType, mapViewModel.Map as L.mapbox.Map);
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
                // ReSharper disable SuspiciousThisUsage
                if (markerType === MarkerType.Harbour) {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.HarbourHelper.Editing(this) }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.HarbourHelper.Deleting(this) }
                        }
                    ];
                } else {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.WaypointHelper.Editing(this) }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.WaypointHelper.Deleting(this) }
                        }
                    ];
                }
                // ReSharper restore SuspiciousThisUsage
            }

            const marker = new L.Marker(wp.LatLng, options);
            marker.addTo(this.Map);
            marker.Waypoint = wp;
            wp.marker = marker;
            if (mapViewModel.MapMode() === MapMode.Admin) {
                if (markerType === MarkerType.Dummy)
                    marker.addEventListener("mouseout", (e) => {
                        if (e.target.Waypoint.IsDummy())
                            mapViewModel.HoveredPolyine = undefined;

                    });
                marker.addEventListener("drag", () => { wp.SetLatLng(wp.marker.getLatLng()); });
                if (markerType === MarkerType.Waypoint || markerType === MarkerType.Dummy) {
                    this.WaypointMarkers.push(wp.marker);
                    wp.marker.Point = mapViewModel.Map.latLngToContainerPoint(wp.LatLng);
                }
                wp.marker.addEventListener("click", () => {
                    if (wp.IsDummy()) {
                        mapViewModel.HoveredPolyine = undefined;
                        wp.convertFromDummyHandle();
                        mapViewModel.Waypoints.push(wp);
                    }
                    if (mapViewModel.GetMapMode() === MapMode.RouteDrawing) {
                        if (!wp.IsInPolyline(mapViewModel.DrawingPolyline)) {
                            ServerApi.WaypointConnections
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
                    wp.marker.addOneTimeEventListener("drag", () => {
                        wp.convertFromDummyHandle();
                        mapViewModel.Waypoints.push(wp);
                    });
                //else if (markerType === MarkerType.Waypoint) {
                //    wp.Name(`Wegpunkt ${mapViewModel.Waypoints().length + 1}`);
                //}
                wp.marker.addEventListener("dragend", () => { wp.SaveToServer(); });
            } else if (markerType === MarkerType.Harbour) {
                wp.marker.addEventListener("mouseover", () => {
                    if (mapViewModel.HarbourHelper.Detail() !== undefined)
                        mapViewModel.ShowRoute(wp);
                });
                wp.marker.addEventListener("click", () => mapViewModel.HarbourHelper.Detail(wp as ClientModel.Harbour));
            }
        }
    }

    CreateHarbour();
    CreateHarbour(name: string, latLng: L.LatLng);
    CreateHarbour(name?: string, latLng?: L.LatLng) {
        let h: Harbour;
        if (latLng !== undefined)
            h = new Harbour(latLng, this.Map as L.mapbox.Map);
        else
            h = new Harbour(this.Map as L.mapbox.Map);
        h.Name(name);
        this.InitializeWaypoint(h, MarkerType.Harbour);
        return h;
    }

    DrawingPolyline: L.Polyline;
    Polylines = new Array<L.Polyline>();
    WaypointMarkers = new Array();
    HoveredPolyine: L.Polyline;

    SetOptionKey(option, item: Entity) {
        ko.applyBindingsToNode(option, { attr: { "data-id": item.Id } }, item);
        ko.applyBindingsToNode(option, { attr: { "value": item.Id } }, item);
    };

    HarboursToSelect = ko.computed(() => {
        return (<any[]>this.HarboursByName()).concat([{ Name: "Neuer Hafen...", IsDummy: true }]);
    });

    ProcessHarbourSelectOptions = (option: HTMLOptionElement, item) => {
        if (item !== undefined && item !== null && item.IsDummy === true) {
            option.value = "filled";
            const context = ko.contextFor(option);
            const select = $(option).parent();
            if (select.data("new-change-handler") === undefined)
                select.data("new-change-handler",
                    select.change(() => {
                        if ($(option).is(":selected")) {
                            const harbour = this.CreateHarbour();
                            this.HarbourHelper.Editing(harbour);
                            const subscription = this.HarbourHelper.Editing.subscribe(() => {
                                if (harbour.Id() !== undefined) {
                                    this.Harbours.push(harbour);
                                    context.$data.Harbour(harbour);
                                } else {
                                    harbour.RemoveFromMap();
                                    context.$data.Harbour(undefined);
                                }
                                subscription.dispose();
                            });
                        }
                    }));
        }
    }

    PersonsToSelect = ko.computed(() => {
        return (<any[]>this.Persons().sort((p1,p2)=>p1.FullName() > p2.FullName()?1:-1)).concat([{ FullName: "Neue Person...", IsDummy: true }]);
    });

    ProcessPersonSelectOptions = (option: HTMLOptionElement, item) => {
        if (item !== undefined && item !== null && item.IsDummy === true) {
            option.value = "filled";
            const context = ko.contextFor(option);
            const select = $(option).parent();
            if (select.data("new-change-handler") === undefined)
                select.data("new-change-handler",
                    select.change(() => {
                        if ($(option).is(":selected")) {
                            const person = new Person();
                            this.PersonHelper.Editing(person);
                            const subscription = this.PersonHelper.Editing.subscribe(() => {
                                if (person.Id() !== undefined) {
                                    this.Persons.push(person);
                                    context.$data.Person(person);
                                } else {
                                    context.$data.Person(undefined);
                                }
                                subscription.dispose();
                            });
                        }
                    }));
        }
    }

    AlbumStack = ko.observableArray<ClientModel.Album>();
}

var dropzoneModalOpenedByDrag = false;
var dropzoneModal = $("#dropzoneModal");
var jobOverviewModal = $("#jobOverviewModal");
var personOverviewModal = $("#personOverviewModal");
var dropzone: Dropzone;
var hasDrag = false;
var uploadModalVisible = false;
var pswp = $(".pswp")[0];
var personDeails = $("#personDetails");
var deletePerson = $("#deletePerson");
var leftSidebar = new Sidebar($("#leftSidebar"));
var rightSidebar = new Sidebar($("#rightSidebar"));
var bottomSidebar = new Sidebar($("#bottomSidebar"));
var harbourInfo = $("#harbourInfo");

var mapViewModel = new MapViewModel(MapMode.View);

Dropzone.options.dropzone =
    {
        acceptedFiles: "image/jpeg,image/png",
        dictInvalidFileType: "Dieser Dateityp wird nicht unterstützt",
        dictDefaultMessage: "Dateien hier ablegen",
        init() {
            dropzone = this;
            dropzone.on("success",
                (e, data: ServerModel.AlbumImage) => {
                    var image = new ClientModel.Image().LoadFromServerEntity(data.Image);
                    mapViewModel.Images.push(image);
                    mapViewModel.GetAlbumById(data.AlbumId).Images.push(image);
                });
            dropzone.on("queuecomplete", () => {
                if (dropzoneModalOpenedByDrag)
                    dropzoneModal.modal("hide");
            });
            dropzone.on("dragover", () => { hasDrag = true; });
        }
    };
document.ondragenter =
    (e: DragEvent) => {
        if (mapViewModel.IsLoggedIn &&
            !uploadModalVisible &&
            !hasDrag &&
            !dropzoneModalOpenedByDrag &&
            dropzoneModal.is(":not(.in)") &&
            e.dataTransfer.types[0] === "Files" &&
            mapViewModel.AlbumStack()[0] !== undefined) {
            dropzoneModal.modal("show");
            uploadModalVisible = true;
            dropzoneModalOpenedByDrag = true;
        }

        hasDrag = true;
        e.preventDefault();
        e.stopPropagation();
    };
document.ondragover = () => { hasDrag = true; };
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

$(".modal").on("hidden.bs.modal", function () {
    $(this).removeClass("fv-modal-stack");
    $("body").data("fv_open_modals", $("body").data("fv_open_modals") - 1);
});


$(".modal").on("shown.bs.modal", function () {

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

interface KnockoutBindingHandlers {
    daterange?: KnockoutBindingHandler;
}

ko.bindingHandlers.daterange = {
    init(element: any, valueAccessor: () => any, allBindingsAccessor?: KnockoutAllBindingsAccessor, viewModel?: any, bindingContext?: KnockoutBindingContext) {
        let value = valueAccessor()();
        if (value === undefined)
            valueAccessor()(new Date().toJSON());
        value = valueAccessor()();
        $(element)
            .daterangepicker({
                "singleDatePicker": true,
                "showDropdowns": true,
                "timePicker": true,
                "timePicker24Hour": true,
                "timePickerIncrement": 15,
                "locale": {
                    "format": "DD.MM.YYYY HH:mm",
                    "separator": " - ",
                    "applyLabel": "Speichern",
                    "cancelLabel": "Abbrechen",
                    "fromLabel": "Von",
                    "toLabel": "Bis",
                    "customRangeLabel": "Custom",
                    "weekLabel": "W",
                    "daysOfWeek": [
                        "S0",
                        "Mo",
                        "Di",
                        "Mi",
                        "Do",
                        "Fr",
                        "Sa"
                    ],
                    "monthNames": [
                        "Januar",
                        "Februar",
                        "März",
                        "April",
                        "Mai",
                        "Juni",
                        "Juli",
                        "August",
                        "September",
                        "Oktober",
                        "November",
                        "Dezember"
                    ],
                    "firstDay": 1
                },
                "alwaysShowCalendars": true,
                "startDate": value,
                "endDate": value
            }, (start) => {
                valueAccessor()(start._d.toJSON());
            });
    },
    update(element: any, valueAccessor: () => any, allBindingsAccessor?: KnockoutAllBindingsAccessor, viewModel?: any, bindingContext?: KnockoutBindingContext) {
        $(element).data("daterangepicker").setStartDate(moment(valueAccessor()()));
    }
};


window.Parsley.on("form:validate", form => {
    if (form.submitEvent === undefined)
        return false;
});

window.Parsley.on("form:submit", form => false);
