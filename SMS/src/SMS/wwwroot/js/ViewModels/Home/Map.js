/// <reference path="../../clientmodel.ts" />
/// <reference path="../../servermodel.ts" />
/// <reference path="../../serverapi.ts" />
/// <reference path="../../../../typings/browser/definitions/moment/index.d.ts" />
var Waypoint = ClientModel.Waypoint;
var Harbour = ClientModel.Harbour;
var Job = ClientModel.Job;
var WaypointDistance = ClientModel.WaypointDistance;
if (/Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent)) {
    $("body").addClass("mobile");
}
function renderTime(startDate, endDate) {
    if (startDate instanceof Date)
        return renderTime(endDate.getTime() - startDate.getTime());
    var duration = startDate;
    var time = Math.ceil(duration / 60000);
    var mins = (time % 60).toString();
    if (mins.length === 1)
        mins = "0" + mins;
    time = Math.ceil(time / 60);
    return time.toString() + ":" + mins;
}
function getMiddle(pol) {
    var start = pol.getLatLngs()[0];
    var end = pol.getLatLngs()[1];
    return new L.LatLng(start.lat + ((end.lat - start.lat) / 2), start.lng + ((end.lng - start.lng) / 2));
}
function splitPolyline(polyline) {
    if (polyline.Waypoints.length === 2 && polyline.DummyHandle instanceof Waypoint) {
        var w1 = polyline.Waypoints[0];
        var w2 = polyline.DummyHandle;
        var w3 = polyline.Waypoints[1];
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
function removePolyline(polyline) {
    for (var _i = 0, _a = polyline.Waypoints; _i < _a.length; _i++) {
        var waypoint = _a[_i];
        waypoint.RemoveFromPolyline(polyline);
    }
    if (polyline.DummyHandle !== undefined) {
        polyline.DummyHandle.RemoveFromPolyline(polyline);
        polyline.DummyHandle.RemoveFromMap();
    }
    mapViewModel.Map.removeLayer(polyline);
}
function addDummyHandle(polyline) {
    if (polyline.DummyHandle === undefined) {
        polyline.DummyHandle = mapViewModel.CreateWaypoint(getMiddle(polyline), MarkerType.Dummy);
        polyline.DummyHandle.AddToPolyline(polyline);
    }
}
function redrawPolyline(polyline) {
    var middleLatLng = getMiddle(polyline);
    if (polyline.DummyHandle === undefined)
        addDummyHandle(polyline);
    if (polyline.DummyHandle.Longitude() !== middleLatLng.lng || polyline.DummyHandle.Latitude() !== middleLatLng.lat)
        polyline.DummyHandle.SetLatLng(middleLatLng);
    else
        polyline.redraw();
}
function removeFromPolyline(polyline, latLng) {
    removeFromArray(polyline.getLatLngs(), latLng);
    polyline.redraw();
}
function removeFromArray(arr, obj) {
    var tmpArr = new Array();
    for (var _i = 0, arr_1 = arr; _i < arr_1.length; _i++) {
        var item = arr_1[_i];
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
var MapMode;
(function (MapMode) {
    MapMode[MapMode["Admin"] = 0] = "Admin";
    MapMode[MapMode["View"] = 1] = "View";
    MapMode[MapMode["TripPlanning"] = 2] = "TripPlanning";
    MapMode[MapMode["RouteDrawing"] = 3] = "RouteDrawing";
})(MapMode || (MapMode = {}));
var EditingHelper = (function () {
    function EditingHelper(editingModalId, deletingModalId, Factory, Dataset, detailModalId) {
        var _this = this;
        this.Factory = Factory;
        this.Dataset = Dataset;
        this.EditingModalOpen = false;
        this.DeletingModalOpen = false;
        this.DetailModalOpen = false;
        this.Detail = ko.observable();
        this.Editing = ko.observable();
        this.Deleting = ko.observable();
        this.Delete = function () {
            _this.Deleting()
                .DeleteOnServer()
                .done(function () {
                _this.Dataset.remove(_this.Deleting());
                _this.Deleting(undefined);
            });
        };
        this.Save = function () {
            var isNew = _this.Editing().Id() === undefined;
            _this.Editing()
                .SaveToServer()
                .done(function () {
                if (isNew)
                    _this.Dataset.push(_this.Editing());
                _this.Deleting(undefined);
            });
        };
        this.EditingModal = $("#" + editingModalId);
        this.DeletingModal = $("#" + deletingModalId);
        this.EditingModal.on("show.bs.modal", function () {
            _this.EditingModalOpen = true;
            if (_this.Editing() === undefined)
                _this.Editing(_this.Factory());
        });
        this.EditingModal.on("hidden.bs.modal", function () {
            _this.EditingModalOpen = false;
            if (_this.Editing() !== undefined)
                _this.Editing(undefined);
        });
        this.Editing.subscribe(function (entity) {
            if (entity === undefined && _this.EditingModalOpen) {
                _this.EditingModal.modal("hide");
            }
            else if (!_this.EditingModalOpen) {
                entity.SaveState();
                _this.EditingModal.modal("show");
            }
        });
        this.Editing.subscribe(function () {
            if (_this.Editing() !== undefined)
                _this.Editing().RevertState(true);
        }, this, "beforeChange");
        this.DeletingModal.on("show.bs.modal", function () {
            _this.DeletingModalOpen = true;
        });
        this.DeletingModal.on("hidden.bs.modal", function () {
            _this.DeletingModalOpen = false;
            if (_this.Deleting() !== undefined)
                _this.Deleting(undefined);
        });
        this.Deleting.subscribe(function (entity) {
            if (entity === undefined && _this.DeletingModalOpen) {
                _this.DeletingModal.modal("hide");
            }
            else if (!_this.DeletingModalOpen) {
                _this.DeletingModal.modal("show");
            }
        });
        if (detailModalId !== undefined) {
            if (detailModalId instanceof Sidebar) {
                this.DetailSidebar = detailModalId;
                this.Detail.subscribe(function (entity) {
                    if (entity === undefined && _this.DetailSidebar.IsActiv()) {
                        _this.DetailSidebar.Hide();
                    }
                    else if (!_this.DetailSidebar.IsActiv()) {
                        _this.DetailSidebar.Hide();
                    }
                });
            }
            else {
                this.DetailModal = $("#" + detailModalId);
                this.Detail.subscribe(function (entity) {
                    if (entity === undefined && _this.DetailModalOpen) {
                        _this.DetailModal.modal("hide");
                    }
                    else if (!_this.DetailModalOpen) {
                        entity.SaveState();
                        _this.DetailModal.modal("show");
                    }
                });
            }
        }
        this.Deleting.subscribe(function (entity) {
            if (entity === undefined && _this.DeletingModalOpen) {
                _this.DeletingModal.modal("hide");
            }
            else if (!_this.DeletingModalOpen) {
                entity.SaveState();
                _this.DeletingModal.modal("show");
            }
        });
    }
    return EditingHelper;
}());
var MapViewModel = (function () {
    function MapViewModel(mapMode) {
        var _this = this;
        this.routePolyline = ko.observable();
        this.IsLastTakInRoute = ko.computed({
            read: function () {
                var trip = mapViewModel.SelectedTrip();
                var h = mapViewModel.SelectedHarbour();
                return trip !== undefined && h !== undefined && trip.Tacks()[trip.Tacks().length - 1].Start() === h;
            },
            deferEvaluation: true
        });
        this.GetRouteDistance = ko.computed({
            read: function () {
                var distance = 0;
                for (var _i = 0, _a = mapViewModel.SelectedTrip().Tacks(); _i < _a.length; _i++) {
                    var tack = _a[_i];
                    if (!isNaN(tack.Distance()))
                        distance += tack.Distance();
                }
                return distance;
            },
            deferEvaluation: true
        });
        this.TopJobs = ko.computed({
            read: function () {
                return mapViewModel.Jobs().filter(function (j) { return j.SuperJobId() === undefined; });
            },
            deferEvaluation: true
        });
        this.IsInViewMode = ko.computed({
            read: function () {
                return mapViewModel.MapMode() === MapMode.View;
            },
            deferEvaluation: true
        });
        this.IsInAdminMode = ko.computed({
            read: function () {
                return mapViewModel.MapMode() === MapMode.Admin;
            },
            deferEvaluation: true
        });
        this.WaypointsLoaded = false;
        this.WaypointConnectionsLoaded = false;
        this.PersonsLoaded = false;
        this.JobsLoaded = false;
        this.TripsLoaded = false;
        this.AddressesLoaded = false;
        this.ImagesLoaded = false;
        this.AlbumsLoaded = false;
        this.WaypointTacksLoaded = false;
        this.TacksLoaded = false;
        this.LocationsLoaded = false;
        this.AlbumImagesLoaded = false;
        this.LogBookEntriesLoaded = false;
        this.CrewsLoaded = false;
        this.Waypoints = ko.observableArray();
        this.WaypointConnections = ko.observableArray();
        this.Harbours = ko.observableArray();
        this.Persons = ko.observableArray();
        this.Jobs = ko.observableArray();
        this.Trips = ko.observableArray();
        this.Addresses = ko.observableArray();
        this.Images = ko.observableArray();
        this.Tacks = ko.observableArray();
        this.Locations = ko.observableArray();
        this.Supermarkets = ko.observableArray();
        this.Restaurants = ko.observableArray();
        this.Albums = ko.observableArray();
        this.AlbumImages = ko.observableArray();
        this.LogBookEntries = ko.observableArray();
        this.Crews = ko.observableArray();
        this.SelectedWaypoint = ko.observable();
        this.SelectedHarbour = ko.observable();
        this.SelectedPerson = ko.observable();
        this.SelectedJob = ko.observable();
        this.SelectedTrip = ko.observable();
        this.SelectedAddress = ko.observable();
        this.SelectedImage = ko.observable();
        this.SelectedTack = ko.observable();
        this.SelectedLocation = ko.observable();
        this.SelectedSupermarket = ko.observable();
        this.SelectedRestaurant = ko.observable();
        this.SelectedLogBookEntry = ko.observable();
        this.RemoveHarbour = function () {
            mapViewModel.SelectedWaypoint().RemoveFromMap();
            mapViewModel.Waypoints.remove(_this.SelectedWaypoint());
        };
        this.RemoveWaypoint = function () {
            mapViewModel.SelectedHarbour().RemoveFromMap();
            mapViewModel.Harbours.remove(_this.SelectedHarbour());
            mapViewModel.Harbours.remove(_this.SelectedHarbour());
        };
        this.MapMode = ko.observable();
        this.RemovePolyline = function (polyline) {
            _this.Map.removeLayer(polyline);
            _this.DrawingPolyline = undefined;
        };
        this.routeFixed = false;
        this.noRevertToPreviousBounds = false;
        this.Polylines = new Array();
        this.DeletingPerson = ko.observable();
        this.EditingPerson = ko.observable();
        this.EditingHarbour = ko.observable();
        this.DeletingHarbour = ko.observable();
        this.EditingWaypoint = ko.observable();
        this.DeletingWaypoint = ko.observable();
        this.DeletingJob = ko.observable();
        this.EditingJob = ko.observable();
        this.EditingLogBookEntry = ko.observable();
        this.DeletingLogBookEntry = ko.observable();
        this.DetailedLogBookEntry = ko.observable();
        this.WaypointMarkers = new Array();
        L.mapbox
            .accessToken =
            "pk.eyJ1IjoiZGFuaWVsLWt1b24iLCJhIjoiY2lldnVtY29iMDBiOHQxbTBvZzBqZWl6cCJ9.UEc2YqH59pB1YTpv22vg8A";
        this.MapMode(mapMode);
        this.MapMode.subscribe(function () {
            _this.InitializeMap();
        });
        var mapOptions = {
            contextmenu: mapMode === MapMode.Admin,
            contextmenuItems: [
                {
                    text: "Neuer Hafen",
                    callback: function (e) {
                        console.log(e);
                        mapViewModel.EditingHarbour(mapViewModel.CreateHarbour("", e.latlng));
                    }
                }
            ]
        };
        this.Map = L.mapbox.map("map", "mapbox.streets", mapOptions);
        this.Map.setView([54.40774166820069, 10.523529052734373], 9);
        L.tileLayer("http://t1.openseamap.org/seamark/{z}/{x}/{y}.png").addTo(this.Map);
        this.LoadData();
        this.SelectedHarbour.subscribe(function (newHarbour) {
            if (newHarbour !== undefined) {
                mapViewModel.CalculateDistances(newHarbour);
                mapViewModel.Harbours.sort(function (h1, h2) { return h1.Distance() - h2.Distance(); });
            }
            else {
                for (var _i = 0, _a = mapViewModel.Harbours(); _i < _a.length; _i++) {
                    var h = _a[_i];
                    h.Distance(0);
                }
            }
            mapViewModel.routeFixed = false;
            mapViewModel.HideRoute();
        });
        this.EditingHarbour.subscribe(function (harbour) {
            if (harbour === undefined) {
                editingHarbourModal.modal("hide");
            }
            else {
                harbour.SaveState();
                editingHarbourModal.modal("show");
            }
        });
        this.EditingHarbour.subscribe(function (harbour) {
            if (harbour !== undefined) {
                harbour.RevertState(true);
                if (harbour.Id() === undefined)
                    mapViewModel.Map.removeLayer(harbour.marker);
            }
        }, this, "beforeChange");
        this.DeletingHarbour.subscribe(function (h) {
            if (h === undefined) {
                deletingHarbourModal.modal("hide");
            }
            else {
                deletingHarbourModal.modal("show");
            }
        });
        this.EditingWaypoint.subscribe(function (waypoint) {
            if (waypoint === undefined) {
                editingWaypointModal.modal("hide");
            }
            else {
                waypoint.SaveState();
                editingWaypointModal.modal("show");
            }
        });
        this.EditingWaypoint.subscribe(function (waypoint) {
            if (mapViewModel.EditingWaypoint() !== undefined)
                mapViewModel.EditingWaypoint().RevertState(true);
        }, this, "beforeChange");
        this.DeletingWaypoint.subscribe(function (h) {
            if (h === undefined) {
                deletingWaypointModal.modal("hide");
            }
            else {
                deletingWaypointModal.modal("show");
            }
        });
        this.EditingJob.subscribe(function (job) {
            if (job === undefined) {
                editingJobModal.modal("hide");
            }
            else {
                job.SaveState();
                editingJobModal.modal("show");
            }
        });
        this.EditingJob.subscribe(function (job) {
            if (mapViewModel.EditingJob() !== undefined)
                mapViewModel.EditingJob().RevertState(true);
        }, this, "beforeChange");
        this.DeletingJob.subscribe(function (h) {
            if (h === undefined) {
                deletingJobModal.modal("hide");
            }
            else {
                deletingJobModal.modal("show");
            }
        });
        this.EditingPerson.subscribe(function (Person) {
            if (Person === undefined) {
                editingPersonModal.modal("hide");
            }
            else {
                Person.SaveState();
                editingPersonModal.modal("show");
            }
        });
        this.EditingPerson.subscribe(function (Person) {
            if (mapViewModel.EditingPerson() !== undefined)
                mapViewModel.EditingPerson().RevertState(true);
        }, this, "beforeChange");
        this.DeletingPerson.subscribe(function (h) {
            if (h === undefined) {
                deletingPersonModal.modal("hide");
            }
            else {
                deletingPersonModal.modal("show");
            }
        });
        this.DetailedLogBookEntry.subscribe(function (logBookEntry) {
            detailedLogBookEntryModal.modal("show");
        });
        this.EditingLogBookEntry.subscribe(function (logBookEntry) {
            if (logBookEntry === undefined) {
                editingLogBookEntryModal.modal("hide");
            }
            else {
                logBookEntry.SaveState();
                editingLogBookEntryModal.modal("show");
            }
        });
        this.EditingLogBookEntry.subscribe(function (job) {
            if (mapViewModel.EditingLogBookEntry() !== undefined)
                mapViewModel.EditingLogBookEntry().RevertState(true);
        }, this, "beforeChange");
        this.SelectedHarbour.subscribe(function (h) {
            if (h === undefined)
                rightSidebar.Hide();
            else
                rightSidebar.Show();
        });
        this.SelectedTrip.subscribe(function (t) {
            if (t === undefined)
                bottomSidebar.Hide();
            else
                bottomSidebar.Show();
        });
        this.Map.addEventListener("mousemove", function (e) {
            if (_this.GetMapMode() === MapMode.RouteDrawing) {
                _this.DrawingLatLng.lat = e.latlng.lat;
                _this.DrawingLatLng.lng = e.latlng.lng;
                _this.DrawingPolyline.redraw();
            }
            if (_this.MapMode() === MapMode.Admin)
                for (var _i = 0, _a = _this.WaypointMarkers; _i < _a.length; _i++) {
                    var marker = _a[_i];
                    if (marker.Point.distanceTo(e.containerPoint) < 150)
                        marker.setOpacity(marker.Waypoint.IsDummy() ? 0.0 : 1);
                    else
                        marker.setOpacity(marker.Waypoint.IsDummy() ? 0.0 : 0.8);
                }
            if (mapViewModel.HoveredPolyine !== undefined && mapViewModel.HoveredPolyine.DummyHandle !== undefined) {
                var polyline = mapViewModel.HoveredPolyine;
                var p1 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[0]);
                var p2 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[1]);
                if (p1.distanceTo(e.containerPoint) < 20 || p2.distanceTo(e.containerPoint) < 20) {
                    mapViewModel.HoveredPolyine = undefined;
                }
                else {
                    mapViewModel.HoveredPolyine.DummyHandle.marker.setOpacity(0.8);
                    mapViewModel.HoveredPolyine.DummyHandle
                        .SetLatLng(mapViewModel.Map
                        .containerPointToLatLng(L.LineUtil
                        .closestPointOnSegment(e.containerPoint, p1, p2)), false);
                }
            }
        });
        this.Map.addEventListener("click", function (e) {
            if (_this.GetMapMode() === MapMode.RouteDrawing) {
                var waypoint = mapViewModel.CreateWaypoint(e.latlng, MarkerType.Waypoint);
                var startId_1 = _this.DrawingPolyline.Waypoints[0].Id();
                waypoint.SaveToServer()
                    .done(function (w) {
                    ServerApi.WaypointConnections
                        .Connect(w.Id, startId_1);
                });
                waypoint.AddToPolyline(_this.DrawingPolyline);
                addDummyHandle(_this.DrawingPolyline);
                removeFromPolyline(_this.DrawingPolyline, _this.DrawingLatLng);
                _this.DrawingPolyline = _this.AddPolyline(waypoint);
                _this.DrawingLatLng = new L.LatLng(e.latlng.lat, e.latlng.lng);
                _this.DrawingPolyline.addLatLng(_this.DrawingLatLng);
            }
        });
        this.Map.addEventListener("dblclick", function (e) {
            if (_this.GetMapMode() === MapMode.RouteDrawing) {
                e.originalEvent.cancelBubble = true;
                e.originalEvent.preventDefault();
                e.originalEvent.stopPropagation();
                _this.DrawingPolyline.addLatLng(e.latlng);
                _this.DrawingLatLng = e.latlng;
            }
        });
        $(document)
            .keyup(function (e) {
            if (_this.GetMapMode() === MapMode.RouteDrawing) {
                if (e.keyCode === 27) {
                    _this.RemovePolyline(_this.DrawingPolyline);
                }
            }
        });
        this.Map.addEventListener("move", function (e) {
            for (var _i = 0, _a = _this.WaypointMarkers; _i < _a.length; _i++) {
                var marker = _a[_i];
                marker.Point = _this.Map.latLngToContainerPoint(marker.getLatLng());
            }
        });
        this.Map.addEventListener("zoom", function (e) {
            for (var _i = 0, _a = _this.WaypointMarkers; _i < _a.length; _i++) {
                var marker = _a[_i];
                marker.Point = _this.Map.latLngToContainerPoint(marker.getLatLng());
            }
        });
    }
    MapViewModel.prototype.StartRoute = function () {
        var trip = new ClientModel.Trip();
        var tack = new ClientModel.Tack();
        var harbour = mapViewModel.SelectedHarbour();
        tack.Start(harbour);
        trip.Tacks.push(tack);
        mapViewModel.SelectedTrip(trip);
        mapViewModel.routePolyline(L.polyline([], {
            color: "#009900"
        }));
        mapViewModel.routePolyline().addTo(mapViewModel.Map);
    };
    MapViewModel.prototype.AddToRoute = function () {
        var trip = mapViewModel.SelectedTrip();
        var targetHarbour = mapViewModel.SelectedHarbour();
        var tack = new ClientModel.Tack();
        var lastTack = trip.Tacks()[trip.Tacks().length - 1];
        var startHarbour = lastTack.Start();
        mapViewModel.CalculateDistances(targetHarbour, startHarbour);
        lastTack.Distance(startHarbour.RouteDistance());
        var wp = startHarbour;
        mapViewModel.routePolyline().addLatLng(wp.LatLng);
        while (wp.RoutePrecessor() !== undefined /*&& wp.RoutePrecessor() !== startHarbour*/) {
            wp = wp.RoutePrecessor();
            mapViewModel.routePolyline().addLatLng(wp.LatLng);
        }
        lastTack.End(targetHarbour);
        tack.Start(targetHarbour);
        trip.Tacks.push(tack);
    };
    MapViewModel.prototype.RedrawTrip = function () {
        mapViewModel.Map.removeLayer(mapViewModel.routePolyline());
        mapViewModel.routePolyline(L.polyline([], {
            color: "#009900"
        }));
        mapViewModel.routePolyline().addTo(mapViewModel.Map);
        for (var _i = 0, _a = mapViewModel.SelectedTrip().Tacks(); _i < _a.length; _i++) {
            var tack = _a[_i];
            var targetHarbour = tack.End();
            var startHarbour = tack.Start();
            if (targetHarbour === undefined)
                continue;
            mapViewModel.CalculateDistances(targetHarbour, startHarbour);
            tack.Distance(startHarbour.RouteDistance());
            var wp = startHarbour;
            mapViewModel.routePolyline().addLatLng(wp.LatLng);
            while (wp.RoutePrecessor() !== undefined /*&& wp.RoutePrecessor() !== startHarbour*/) {
                wp = wp.RoutePrecessor();
                mapViewModel.routePolyline().addLatLng(wp.LatLng);
            }
        }
    };
    MapViewModel.prototype.PullTack = function () {
        var tack = this;
        var tacks = mapViewModel.SelectedTrip().Tacks;
        var index = tacks.indexOf(tack);
        var prevTack = tacks()[index - 1];
        var tmpEnd = tack.End();
        tack.End(prevTack.Start());
        prevTack.End(tmpEnd);
        if (index > 1) {
            tacks()[index - 2].End(tack.Start());
        }
        tacks.splice(index - 1, 2, tack, prevTack);
        mapViewModel.RedrawTrip();
    };
    MapViewModel.prototype.PushTack = function () {
        var tack = this;
        var tacks = mapViewModel.SelectedTrip().Tacks;
        var index = tacks.indexOf(tack);
        var nextTack = tacks()[index + 1];
        tack.End(nextTack.End());
        nextTack.End(tack.Start());
        if (index > 0) {
            tacks()[index - 1].End(nextTack.Start());
        }
        tacks.splice(index, 2, nextTack, tack);
        mapViewModel.RedrawTrip();
    };
    MapViewModel.prototype.RemoveTack = function () {
        var tack = this;
        var tacks = mapViewModel.SelectedTrip().Tacks;
        var index = tacks.indexOf(tack);
        var prevTack = tacks()[index - 1];
        if (prevTack !== undefined)
            prevTack.End(tack.End());
        tacks.remove(tack);
        mapViewModel.RedrawTrip();
    };
    MapViewModel.prototype.LoadData = function () {
        var _this = this;
        ServerApi.Waypoints
            .Get()
            .done(function (d) {
            for (var _i = 0, d_1 = d; _i < d_1.length; _i++) {
                var sEntity = d_1[_i];
                if (sEntity.Type === "Waypoint")
                    _this.Waypoints.push(mapViewModel
                        .CreateWaypoint(MarkerType.Waypoint)
                        .LoadFromServerEntity(sEntity));
                else if (sEntity.Type === "Harbour") {
                    var harbour = mapViewModel
                        .CreateHarbour()
                        .LoadFromServerEntity(sEntity);
                    _this.Harbours.push(harbour);
                }
            }
            _this.WaypointsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.WaypointConnections
            .Get()
            .done(function (d) {
            for (var _i = 0, d_2 = d; _i < d_2.length; _i++) {
                var sEntity = d_2[_i];
                _this.WaypointConnections.push(sEntity);
            }
            _this.WaypointConnectionsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Persons
            .Get()
            .done(function (d) {
            for (var _i = 0, d_3 = d; _i < d_3.length; _i++) {
                var sEntity = d_3[_i];
                _this.Persons.push(new ClientModel.Person().LoadFromServerEntity(sEntity));
            }
            _this.PersonsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Jobs
            .Get()
            .done(function (d) {
            for (var _i = 0, d_4 = d; _i < d_4.length; _i++) {
                var sEntity = d_4[_i];
                _this.Jobs.push(new ClientModel.Job().LoadFromServerEntity(sEntity));
            }
            _this.JobsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Trips
            .Get()
            .done(function (d) {
            for (var _i = 0, d_5 = d; _i < d_5.length; _i++) {
                var sEntity = d_5[_i];
                _this.Trips.push(new ClientModel.Trip().LoadFromServerEntity(sEntity));
            }
            _this.TripsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Addresses
            .Get()
            .done(function (d) {
            for (var _i = 0, d_6 = d; _i < d_6.length; _i++) {
                var sEntity = d_6[_i];
                _this.Addresses.push(new ClientModel.Address().LoadFromServerEntity(sEntity));
            }
            _this.AddressesLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Images
            .Get()
            .done(function (d) {
            for (var _i = 0, d_7 = d; _i < d_7.length; _i++) {
                var sEntity = d_7[_i];
                _this.Images.push(new ClientModel.Image().LoadFromServerEntity(sEntity));
            }
            _this.ImagesLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Albums
            .Get()
            .done(function (d) {
            for (var _i = 0, d_8 = d; _i < d_8.length; _i++) {
                var sEntity = d_8[_i];
                _this.Albums.push(new ClientModel.Album().LoadFromServerEntity(sEntity));
            }
            _this.AlbumsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.LogBookEntries
            .Get()
            .done(function (d) {
            for (var _i = 0, d_9 = d; _i < d_9.length; _i++) {
                var sEntity = d_9[_i];
                _this.LogBookEntries.push(new ClientModel.LogBookEntry().LoadFromServerEntity(sEntity));
            }
            _this.LogBookEntriesLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.AlbumImages.Get()
            .done(function (d) {
            for (var _i = 0, d_10 = d; _i < d_10.length; _i++) {
                var ai = d_10[_i];
                _this.AlbumImages.push(ai);
            }
            _this.AlbumImagesLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Crews.Get()
            .done(function (d) {
            for (var _i = 0, d_11 = d; _i < d_11.length; _i++) {
                var ai = d_11[_i];
                _this.Crews.push(ai);
            }
            _this.CrewsLoaded = true;
            _this.InitializeModel();
        });
        //ServerApi.WaypointTacks.Get().done(d => {
        //    for (let sEntity of d) { this.WaypointTacks.push(new ClientModel.WaypointTack().LoadFromServerEntity(sEntity)); }
        //    this.WaypointTacksLoaded = true;
        //    this.InitialozeModel();
        //});
        ServerApi.Tacks
            .Get()
            .done(function (d) {
            for (var _i = 0, d_12 = d; _i < d_12.length; _i++) {
                var sEntity = d_12[_i];
                _this.Tacks.push(new ClientModel.Tack().LoadFromServerEntity(sEntity));
            }
            _this.TacksLoaded = true;
            _this.InitializeModel();
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
    };
    MapViewModel.prototype.InitializeModel = function () {
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
            this.AlbumImagesLoaded) {
            for (var _i = 0, _a = this.Jobs(); _i < _a.length; _i++) {
                var entity = _a[_i];
                if (entity.AssignedToId() !== undefined)
                    entity.AssignedTo(this.GetPersonById(entity.AssignedToId()));
                if (entity.TripId() !== undefined)
                    entity.Trip(this.GetTripById(entity.TripId()));
                if (entity.SuperJobId() !== undefined) {
                    entity.SuperJob(this.GetJobById(entity.SuperJobId()));
                    entity.SuperJob().SubJobs.push(entity);
                }
            }
            for (var _b = 0, _c = this.Harbours(); _b < _c.length; _b++) {
                var entity = _c[_b];
                entity.Album(this.GetAlbumById(entity.AlbumId()));
            }
            for (var _d = 0, _e = this.Locations(); _d < _e.length; _d++) {
                var entity = _e[_d];
                entity.Address(this.GetAddressById(entity.AddressId()));
                this.GetHarbourById(entity.HarbourId()).Locations.push(entity);
            }
            for (var _f = 0, _g = this.AlbumImages(); _f < _g.length; _f++) {
                var entity = _g[_f];
                this.GetAlbumById(entity.AlbumId).Images.push(this.GetImageById(entity.ImageId));
            }
            for (var _h = 0, _j = mapViewModel.WaypointConnections(); _h < _j.length; _h++) {
                var connection = _j[_h];
                var polyline = mapViewModel.AddPolyline([
                    mapViewModel.GetWayPointById(connection.Waypoint1Id), mapViewModel
                        .GetWayPointById(connection.Waypoint2Id)
                ]);
                addDummyHandle(polyline);
            }
            for (var _k = 0, _l = mapViewModel.LogBookEntries(); _k < _l.length; _k++) {
                var entry = _l[_k];
                entry.Start(mapViewModel.GetHarbourById(entry.StartId()));
                entry.End(mapViewModel.GetHarbourById(entry.EndId()));
                entry.Album(mapViewModel.GetAlbumById(entry.AlbumId()));
            }
            for (var _m = 0, _o = mapViewModel.Crews(); _m < _o.length; _m++) {
                var crew = _o[_m];
                var lBE = mapViewModel.GetLogBookEntryById(crew.TackId);
                var tack = mapViewModel.GetTackById(crew.TackId);
                var trip = mapViewModel.GetTripById(crew.TackId);
                var p = mapViewModel.GetPersonById(crew.PersonId);
                if (lBE !== undefined)
                    lBE.Persons.push(p);
                else if (tack !== undefined)
                    tack.Persons.push(p);
                else if (trip !== undefined)
                    trip.Persons.push(p);
            }
            ko.applyBindings(mapViewModel);
            $("#loadingOverlay").remove();
        }
    };
    MapViewModel.prototype.InitializeMap = function () {
        mapViewModel.SelectedHarbour(undefined);
        for (var _i = 0, _a = mapViewModel.Waypoints(); _i < _a.length; _i++) {
            var wp = _a[_i];
            if (wp.marker !== undefined)
                mapViewModel.Map.removeLayer(wp.marker);
            mapViewModel.CreateMarker(MarkerType.Waypoint, wp);
        }
        for (var _b = 0, _c = mapViewModel.Harbours(); _b < _c.length; _b++) {
            var h = _c[_b];
            if (h.marker !== undefined)
                mapViewModel.Map.removeLayer(h.marker);
            mapViewModel.CreateMarker(MarkerType.Harbour, h);
        }
        for (var _d = 0, _e = mapViewModel.Polylines; _d < _e.length; _d++) {
            var p = _e[_d];
            if (p.DummyHandle.marker !== undefined)
                mapViewModel.Map.removeLayer(p.DummyHandle.marker);
            mapViewModel.CreateMarker(MarkerType.Dummy, p.DummyHandle);
        }
        if (mapViewModel.MapMode() === MapMode.Admin) {
            for (var _f = 0, _g = mapViewModel.Polylines; _f < _g.length; _f++) {
                var p = _g[_f];
                p.addTo(mapViewModel.Map);
            }
            mapViewModel.Map.contextmenu.enable();
        }
        else {
            for (var _h = 0, _j = mapViewModel.Polylines; _h < _j.length; _h++) {
                var p = _j[_h];
                mapViewModel.Map.removeLayer(p);
            }
            mapViewModel.Map.contextmenu.disable();
        }
    };
    MapViewModel.prototype.GetWaypointById = function (id) {
        for (var _i = 0, _a = this.Waypoints(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        for (var _b = 0, _c = this.Harbours(); _b < _c.length; _b++) {
            var entity = _c[_b];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Waypoint with id " + id + " found";
    };
    MapViewModel.prototype.GetHarbourById = function (id) {
        for (var _i = 0, _a = this.Harbours(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Harbour with id " + id + " found";
    };
    MapViewModel.prototype.GetPersonById = function (id) {
        for (var _i = 0, _a = this.Persons(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Person with id " + id + " found";
    };
    MapViewModel.prototype.GetJobById = function (id) {
        for (var _i = 0, _a = this.Jobs(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Job with id " + id + " found";
    };
    MapViewModel.prototype.GetTripById = function (id) {
        for (var _i = 0, _a = this.Trips(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Trip with id " + id + " found";
    };
    MapViewModel.prototype.GetAddressById = function (id) {
        for (var _i = 0, _a = this.Addresses(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Address with id " + id + " found";
    };
    MapViewModel.prototype.GetImageById = function (id) {
        for (var _i = 0, _a = this.Images(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Image with id " + id + " found";
    };
    MapViewModel.prototype.GetTackById = function (id) {
        for (var _i = 0, _a = this.Tacks(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Tack with id " + id + " found";
    };
    MapViewModel.prototype.GetLogBookEntryById = function (id) {
        for (var _i = 0, _a = this.LogBookEntries(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Tack with id " + id + " found";
    };
    MapViewModel.prototype.GetAlbumById = function (id) {
        for (var _i = 0, _a = this.Albums(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Tack with id " + id + " found";
    };
    MapViewModel.prototype.GetLocationById = function (id) {
        for (var _i = 0, _a = this.Locations(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        for (var _b = 0, _c = this.Restaurants(); _b < _c.length; _b++) {
            var entity = _c[_b];
            if (entity.Id() === id)
                return entity;
        }
        for (var _d = 0, _e = this.Restaurants(); _d < _e.length; _d++) {
            var entity = _e[_d];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Location with id " + id + " found";
    };
    //SortedLogBookEntries = ko.computed({
    //    read: () => this.LogBookEntries.sort((l1, l2) => {
    //        var t1 = l1.StartDate().getTime();
    //        var t2 = l2.StartDate().getTime();
    //        return t2 - t1;
    //    }),
    //    deferEvaluation: true
    //});
    MapViewModel.prototype.InitGallery = function () {
        var items = new Array();
        var currImage = this;
        for (var _i = 0, _a = mapViewModel.SelectedHarbour().Album().Images(); _i < _a.length; _i++) {
            var data = _a[_i];
            items.push({
                h: data.Height(),
                w: data.Width(),
                src: data.Path()
            });
        }
        gallery = new PhotoSwipe(pswp, PhotoSwipeUI_Default, items, {
            index: mapViewModel.SelectedHarbour().Album().Images.indexOf(currImage),
            getThumbBoundsFn: function (index) {
                var elem = $(".images:first img")[index];
                var padding = parseFloat(window.getComputedStyle(elem, null)
                    .getPropertyValue("padding-left")
                    .replace("px", ""));
                elem.scrollIntoView(false);
                var bounds = elem.getBoundingClientRect();
                return {
                    x: bounds.left + padding,
                    y: bounds.top + window.screenY + padding,
                    w: bounds.width - (2 * padding)
                };
            }
        });
        gallery.init();
    };
    MapViewModel.prototype.AddHarbour = function () {
        var harbour = mapViewModel.CreateHarbour("Hafen " + this.Harbours.length, this.Map.getCenter());
        mapViewModel.Harbours.push(harbour);
        harbour.SaveToServer();
    };
    MapViewModel.prototype.AddPolyline = function (arg) {
        var polyline = new L.Polyline([]);
        mapViewModel.Polylines.push(polyline);
        polyline.addEventListener("click", function (e) {
            var p1 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[0]);
            var p2 = mapViewModel.Map.latLngToContainerPoint(polyline.getLatLngs()[1]);
            polyline.DummyHandle
                .SetLatLng(mapViewModel.Map
                .containerPointToLatLng(L.LineUtil
                .closestPointOnSegment(e.containerPoint, p1, p2)), false);
            mapViewModel.Waypoints.push(polyline.DummyHandle);
            polyline.DummyHandle.convertFromDummyHandle();
        });
        if (mapViewModel.MapMode() === MapMode.Admin)
            polyline.addTo(this.Map);
        polyline.Waypoints = new Array();
        if (arg !== undefined)
            if (arg instanceof Waypoint)
                arg.AddToPolyline(polyline);
            else
                for (var _i = 0, _a = arg; _i < _a.length; _i++) {
                    var waypoint = _a[_i];
                    waypoint.AddToPolyline(polyline);
                }
        polyline.addEventListener("mouseover", function () {
            //mapViewModel.HoveredPolyine = polyline;
        });
        return polyline;
    };
    MapViewModel.prototype.GetMapMode = function () {
        if (this.DrawingPolyline !== undefined && this.DrawingLatLng !== undefined)
            return MapMode.RouteDrawing;
        return this.MapMode();
    };
    MapViewModel.prototype.GetWayPointById = function (id) {
        for (var _i = 0, _a = this.Waypoints(); _i < _a.length; _i++) {
            var waypoint = _a[_i];
            if (waypoint.Id() === id)
                return waypoint;
        }
        for (var _b = 0, _c = this.Harbours(); _b < _c.length; _b++) {
            var waypoint = _c[_b];
            if (waypoint.Id() === id)
                return waypoint;
        }
        throw "No Waypoint with id " + id + " in model";
    };
    MapViewModel.prototype.CalculateDistances = function (start, target) {
        if (start === void 0) { start = mapViewModel.SelectedHarbour(); }
        var waypoints = [start];
        var calculating = new Array();
        var calculated = new Array();
        var calculateRoute = target !== undefined;
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
            for (var _i = 0, _a = mapViewModel.Waypoints(); _i < _a.length; _i++) {
                var wp = _a[_i];
                wp.RoutePrecessor(undefined);
            }
            for (var _b = 0, _c = mapViewModel.Harbours(); _b < _c.length; _b++) {
                var h = _c[_b];
                h.RoutePrecessor(undefined);
            }
        }
        else {
            for (var _d = 0, _e = mapViewModel.Waypoints(); _d < _e.length; _d++) {
                var wp = _e[_d];
                wp.Precessor(undefined);
            }
            for (var _f = 0, _g = mapViewModel.Harbours(); _f < _g.length; _f++) {
                var h = _g[_f];
                h.Precessor(undefined);
            }
        }
        while (calculating.length > 0) {
            var minimalDist = Number.POSITIVE_INFINITY;
            var minimalWP = void 0;
            for (var _h = 0, calculating_1 = calculating; _h < calculating_1.length; _h++) {
                var wp = calculating_1[_h];
                for (var _j = 0, _k = wp.ConnectedWayPoints; _j < _k.length; _j++) {
                    var cWP = _k[_j];
                    if ((calculateRoute ? cWP.RoutePrecessor() : cWP.Precessor()) !== undefined)
                        removeFromArray(wp.ConnectedWayPoints, cWP);
                }
                if (wp.ConnectedWayPoints.length === 0) {
                    removeFromArray(calculating, wp);
                    calculated.push(wp);
                }
                else {
                    var dist = wp.Distance + wp.ConnectedWayPoints[0].LatLng.distanceTo(wp.LatLng) / 1.852;
                    if (dist < minimalDist) {
                        minimalDist = dist;
                        minimalWP = wp;
                    }
                }
            }
            if (minimalWP !== undefined) {
                calculating.push(new WaypointDistance(minimalWP.Waypoint, minimalWP.ConnectedWayPoints.shift(), minimalDist, waypoints, calculateRoute));
            }
        }
        if (calculateRoute)
            for (var _l = 0, calculated_1 = calculated; _l < calculated_1.length; _l++) {
                var wp = calculated_1[_l];
                wp.Waypoint.RouteDistance(Math.round(wp.Distance / 100) / 10);
            }
        else
            for (var _m = 0, calculated_2 = calculated; _m < calculated_2.length; _m++) {
                var wp = calculated_2[_m];
                wp.Waypoint.Distance(Math.round(wp.Distance / 100) / 10);
            }
    };
    MapViewModel.prototype.ShowRoute = function (h) {
        if (mapViewModel.highlightedRoute !== undefined) {
            mapViewModel.routeFixed = false;
            mapViewModel.HideRoute();
        }
        if (h === undefined)
            h = this;
        if (!(h instanceof ClientModel.Harbour))
            return;
        var latLngs = [h.LatLng];
        var dist = h.Distance();
        if (dist === undefined)
            dist = 0;
        while (h.Precessor() !== undefined) {
            h = h.Precessor();
            latLngs.push(h.LatLng);
        }
        mapViewModel.highlightedRoute = L.polyline(latLngs);
        mapViewModel.highlightedRoute.addTo(mapViewModel.Map);
        mapViewModel.highlightedRoute.bindLabel(dist.toString() + " sm", { noHide: true });
        mapViewModel.FitBounds(mapViewModel.highlightedRoute.getBounds());
    };
    MapViewModel.prototype.FitBounds = function (bounds) {
        var map = mapViewModel.Map;
        var currentBounds = map.getBounds();
        if (!currentBounds.contains(bounds)) {
            if (mapViewModel.previousBounds === undefined)
                mapViewModel.previousBounds = currentBounds;
            map.fitBounds(bounds);
        }
    };
    MapViewModel.prototype.HideRoute = function (force) {
        if (force === void 0) { force = false; }
        if ((!mapViewModel.routeFixed || force) && mapViewModel.highlightedRoute !== undefined) {
            mapViewModel.routeFixed = false;
            mapViewModel.Map.removeLayer(mapViewModel.highlightedRoute);
            mapViewModel.highlightedRoute = undefined;
            if (!mapViewModel.noRevertToPreviousBounds && mapViewModel.previousBounds !== undefined) {
                var tmpBounds_1 = mapViewModel.previousBounds;
                mapViewModel.previousBounds = undefined;
                window.setTimeout(function () {
                    if (mapViewModel.previousBounds === undefined)
                        mapViewModel.Map.fitBounds(tmpBounds_1);
                    else
                        mapViewModel.previousBounds = tmpBounds_1;
                }, 100);
            }
        }
    };
    MapViewModel.prototype.FixRoute = function () {
        mapViewModel.routeFixed = true;
        mapViewModel.previousBounds = undefined;
    };
    MapViewModel.prototype.CreateWaypoint = function (latLng, markerType) {
        var wp;
        if (markerType !== undefined)
            wp = new Waypoint(latLng, markerType, mapViewModel.Map);
        else
            wp = new Waypoint(markerType, mapViewModel.Map);
        this.InitializeWaypoint(wp, markerType);
        return wp;
    };
    MapViewModel.prototype.InitializeWaypoint = function (wp, markerType) {
        this.CreateMarker(markerType, wp);
    };
    MapViewModel.prototype.CreateMarker = function (markerType, wp) {
        if (mapViewModel.MapMode() === MapMode.Admin || markerType === MarkerType.Harbour) {
            var options = {
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
                            callback: function () { mapViewModel.EditingHarbour(this); }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.DeletingHarbour(this); }
                        }
                    ];
                }
                else {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.EditingWaypoint(this); }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.DeletingWaypoint(this); }
                        }
                    ];
                }
            }
            var marker = new L.Marker(wp.LatLng, options);
            marker.addTo(this.Map);
            marker.Waypoint = wp;
            wp.marker = marker;
            if (mapViewModel.MapMode() === MapMode.Admin) {
                if (markerType === MarkerType.Dummy)
                    marker.addEventListener("mouseout", function (e) {
                        if (e.target.Waypoint.IsDummy()) {
                            mapViewModel.HoveredPolyine = undefined;
                        }
                    });
                marker.addEventListener("drag", function (e) {
                    wp.SetLatLng(wp.marker.getLatLng());
                });
                if (markerType === MarkerType.Waypoint || markerType === MarkerType.Dummy) {
                    this.WaypointMarkers.push(wp.marker);
                    wp.marker.Point = mapViewModel.Map.latLngToContainerPoint(wp.LatLng);
                }
                wp.marker.addEventListener("click", function (e) {
                    if (wp.IsDummy()) {
                        mapViewModel.Waypoints.push(wp);
                        wp.convertFromDummyHandle();
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
                        }
                        else {
                            removePolyline(mapViewModel.DrawingPolyline);
                            mapViewModel.DrawingPolyline = undefined;
                            mapViewModel.DrawingLatLng = undefined;
                        }
                    }
                });
                wp.marker.addEventListener("dblclick", function (e) {
                    mapViewModel.DrawingPolyline = mapViewModel.AddPolyline(wp);
                    mapViewModel.DrawingLatLng = new L.LatLng(e.latlng.lat, e.latlng.lng);
                    mapViewModel.DrawingPolyline.addLatLng(mapViewModel.DrawingLatLng);
                });
                if (markerType === MarkerType.Dummy)
                    wp.marker.addOneTimeEventListener("drag", function (e) {
                        wp.convertFromDummyHandle();
                        mapViewModel.Waypoints.push(wp);
                    });
                //else if (markerType === MarkerType.Waypoint) {
                //    wp.Name(`Wegpunkt ${mapViewModel.Waypoints().length + 1}`);
                //}
                wp.marker.addEventListener("dragend", function (e) {
                    wp.SaveToServer();
                });
            }
            else if (markerType === MarkerType.Harbour) {
                wp.marker.addEventListener("mouseover", function () {
                    if (mapViewModel.SelectedHarbour() !== undefined)
                        mapViewModel.ShowRoute(wp);
                });
                wp.marker.addEventListener("click", function () { return mapViewModel.SelectedHarbour(wp); });
            }
        }
    };
    MapViewModel.prototype.CreateHarbour = function (name, latLng) {
        var h;
        if (latLng !== undefined)
            h = new Harbour(latLng, this.Map);
        else
            h = new Harbour(this.Map);
        h.Name(name);
        this.InitializeWaypoint(h, MarkerType.Harbour);
        return h;
    };
    MapViewModel.prototype.SaveHarbour = function () {
        var harbour = this;
        if (harbour.Id() === undefined) {
            mapViewModel.Harbours.push(harbour);
        }
        harbour.SaveToServer()
            .done(function () {
            mapViewModel.EditingHarbour(undefined);
        });
    };
    MapViewModel.prototype.DeleteHarbour = function () {
        var h = mapViewModel.DeletingHarbour();
        ServerApi.WaypointConnections
            .Disconnect(h.Id())
            .done(function () {
            h.DeleteOnServer()
                .done(function () {
                h.RemoveFromMap();
                mapViewModel.Harbours.remove(h);
                mapViewModel.DeletingHarbour(undefined);
            });
        });
    };
    MapViewModel.prototype.SaveWaypoint = function () {
        var waypoint = this;
        waypoint.SaveToServer()
            .done(function () {
            mapViewModel.EditingWaypoint(undefined);
        });
    };
    MapViewModel.prototype.DeleteWaypoint = function () {
        var wp = mapViewModel.DeletingWaypoint();
        ServerApi.WaypointConnections
            .Disconnect(wp.Id())
            .done(function () {
            wp.DeleteOnServer()
                .done(function () {
                wp.RemoveFromMap();
                mapViewModel.Waypoints.remove(wp);
                mapViewModel.DeletingWaypoint(undefined);
            });
        });
    };
    ;
    MapViewModel.prototype.SaveJob = function () {
        var job = this;
        var newJob = job.Id() === undefined;
        job.SaveToServer()
            .done(function () {
            if (newJob) {
                mapViewModel.Jobs.push(mapViewModel.EditingJob());
                if (mapViewModel.EditingJob().SuperJobId() !== undefined)
                    mapViewModel.GetJobById(mapViewModel.EditingJob().SuperJobId()).SubJobs.push(mapViewModel.EditingJob());
            }
            mapViewModel.EditingJob(undefined);
        });
    };
    MapViewModel.prototype.DeleteJob = function () {
        var job = mapViewModel.DeletingJob();
        job.DeleteOnServer()
            .done(function () {
            mapViewModel.Jobs.remove(job);
            if (job.SuperJobId() !== undefined)
                mapViewModel.GetJobById(job.SuperJobId()).SubJobs.remove(job);
            mapViewModel.DeletingJob(undefined);
        });
    };
    MapViewModel.prototype.SaveLogBookEntry = function () {
        var job = this;
        var newLogBookEntry = job.Id() === undefined;
        job.SaveToServer()
            .done(function () {
            if (newLogBookEntry) {
                mapViewModel.LogBookEntries.push(mapViewModel.EditingLogBookEntry());
            }
            mapViewModel.EditingLogBookEntry(undefined);
        });
    };
    MapViewModel.prototype.DeleteLogBookEntry = function () {
        var job = mapViewModel.DeletingLogBookEntry();
        job.DeleteOnServer()
            .done(function () {
            mapViewModel.LogBookEntries.remove(job);
            mapViewModel.DeletingLogBookEntry(undefined);
        });
    };
    MapViewModel.prototype.SetOptionKey = function (option, item) {
        ko.applyBindingsToNode(option, { attr: { "data-id": item.Id } }, item);
        ko.applyBindingsToNode(option, { attr: { "value": item.Id } }, item);
    };
    ;
    MapViewModel.prototype.SavePerson = function () {
        mapViewModel.EditingPerson().SaveToServer().done(function () {
            mapViewModel.Persons.push(mapViewModel.EditingPerson());
            mapViewModel.EditingPerson(undefined);
        });
    };
    MapViewModel.prototype.DeletePerson = function () {
        mapViewModel.DeletingPerson()
            .DeleteOnServer()
            .done(function () {
            mapViewModel.Persons.remove(mapViewModel.DeletingPerson());
            mapViewModel.DeletingPerson(undefined);
        });
    };
    return MapViewModel;
}());
var mapViewModel = new MapViewModel(MapMode.View);
var dropzoneModalOpenedByDrag = false;
var dropzoneModal = $("#dropzoneModal");
var editingLogBookEntryModal = $("#editingLogBookEntryModal");
var detailedLogBookEntryModal = $("#detailedLogBookEntryModal");
var editingHarbourModal = $("#editingHarbourModal");
var deletingHarbourModal = $("#deletingHarbourModal");
var editingWaypointModal = $("#editingWaypointModal");
var deletingWaypointModal = $("#deletingWaypointModal");
var deletingJobModal = $("#deletingJobModal");
var editingJobModal = $("#editingJobModal");
var jobOverviewModal = $("#jobOverviewModal");
var editingPersonModal = $("#editingPersonModal");
var deletingPersonModal = $("#deletingPersonModal");
var personOverviewModal = $("#personOverviewModal");
var dropzone;
var hasDrag = false;
var uploadModalVisible = false;
var pswp = $(".pswp")[0];
var personDeails = $("#personDetails");
var Person = ClientModel.Person;
var deletePerson = $("#deletePerson");
var leftSidebar = new Sidebar($("#leftSidebar"));
var rightSidebar = new Sidebar($("#rightSidebar"));
var bottomSidebar = new Sidebar($("#bottomSidebar"));
var harbourInfo = $("#harbourInfo");
Dropzone.options.dropzone =
    {
        acceptedFiles: "image/jpeg,image/png",
        dictInvalidFileType: "Dieser Dateityp wird nicht unterstützt",
        dictDefaultMessage: "Dateien hier ablegen",
        init: function () {
            dropzone = this;
            dropzone.on("success", function (e, data) {
                var image = new ClientModel.Image().LoadFromServerEntity(data.Image);
                mapViewModel.Images.push(image);
                mapViewModel.GetAlbumById(data.AlbumId).Images.push(image);
            });
            dropzone.on("queuecomplete", function () {
                if (dropzoneModalOpenedByDrag)
                    dropzoneModal.modal("hide");
            });
            dropzone.on("dragover", function () {
                hasDrag = true;
            });
        }
    };
document.ondragenter =
    function (e) {
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
    function (e) {
        hasDrag = true;
    };
document.ondragleave =
    function (e) {
        if (uploadModalVisible && hasDrag && dropzoneModalOpenedByDrag && dropzone.getQueuedFiles().length === 0 ||
            dropzone.getUploadingFiles().length === 0) {
            hasDrag = false;
            window.setTimeout(function () {
                if (!hasDrag) {
                    dropzoneModal.modal("hide");
                    uploadModalVisible = false;
                }
            }, 1000);
        }
        e.preventDefault();
        e.stopPropagation();
    };
dropzoneModal.on("hide.bs.modal", function (e) {
    if (dropzone.getQueuedFiles().length > 0 || dropzone.getUploadingFiles().length > 0) {
        e.preventDefault();
        e.stopImmediatePropagation();
        alert("Das Fenster kann nicht geschlossen werden, während Dateien hochgeladen werden.");
        return false;
    }
    else {
        dropzone.removeAllFiles();
        dropzoneModalOpenedByDrag = false;
    }
});
var gallery;
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
ko.bindingHandlers.daterange = {
    init: function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
        var value = valueAccessor()();
        if (value === undefined)
            valueAccessor()(new Date().toJSON());
        value = valueAccessor()();
        $(element)
            .daterangepicker({
            "singleDatePicker": true,
            "timePicker": true,
            "timePicker24Hour": true,
            "autoApply": true,
            "startDate": value,
            "endDate": value
        }, function (start, end, label) {
            valueAccessor()(start._d.toJSON());
        });
    },
    update: function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
        $(element).data("daterangepicker").setStartDate(moment(valueAccessor()()));
    }
};
