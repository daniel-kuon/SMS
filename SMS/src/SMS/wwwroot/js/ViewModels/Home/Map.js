var SHarbour = ServerModel.Harbour;
var Waypoint = ClientModel.Waypoint;
var Harbour = ClientModel.Harbour;
var Job = ClientModel.Job;
var WaypointDistance = ClientModel.WaypointDistance;
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
        this.EditingHarbour = ko.observable();
        this.DeletingHarbour = ko.observable();
        this.EditingWaypoint = ko.observable();
        this.DeletingWaypoint = ko.observable();
        this.DeletingJob = ko.observable();
        this.EditingJob = ko.observable();
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
                    ServerApi.WaypointConnectionApi.GetDefault()
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
        ServerApi.WaypointApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_1 = d; _i < d_1.length; _i++) {
                var sEntity = d_1[_i];
                if (sEntity.Type === ServerModel.Waypoint.GetType())
                    _this.Waypoints.push(mapViewModel
                        .CreateWaypoint(L.latLng(sEntity.Latitude, sEntity.Longitude), MarkerType.Waypoint)
                        .LoadFromServerEntity(sEntity));
                else if (sEntity.Type === ServerModel.Harbour.GetType()) {
                    var harbour = mapViewModel
                        .CreateHarbour(sEntity.Name, L.latLng(sEntity.Latitude, sEntity.Longitude))
                        .LoadFromServerEntity(sEntity);
                    _this.Harbours.push(harbour);
                }
            }
            _this.WaypointsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.WaypointConnectionApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_2 = d; _i < d_2.length; _i++) {
                var sEntity = d_2[_i];
                _this.WaypointConnections.push(sEntity);
            }
            _this.WaypointConnectionsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.PersonApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_3 = d; _i < d_3.length; _i++) {
                var sEntity = d_3[_i];
                _this.Persons.push(new ClientModel.Person().LoadFromServerEntity(sEntity));
            }
            _this.PersonsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.JobApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_4 = d; _i < d_4.length; _i++) {
                var sEntity = d_4[_i];
                _this.Jobs.push(new ClientModel.Job().LoadFromServerEntity(sEntity));
            }
            _this.JobsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.TripApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_5 = d; _i < d_5.length; _i++) {
                var sEntity = d_5[_i];
                _this.Trips.push(new ClientModel.Trip().LoadFromServerEntity(sEntity));
            }
            _this.TripsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.AddressApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_6 = d; _i < d_6.length; _i++) {
                var sEntity = d_6[_i];
                _this.Addresses.push(new ClientModel.Address().LoadFromServerEntity(sEntity));
            }
            _this.AddressesLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.ImageApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_7 = d; _i < d_7.length; _i++) {
                var sEntity = d_7[_i];
                _this.Images.push(new ClientModel.Image().LoadFromServerEntity(sEntity));
            }
            _this.ImagesLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.AlbumApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_8 = d; _i < d_8.length; _i++) {
                var sEntity = d_8[_i];
                _this.Albums.push(new ClientModel.Album().LoadFromServerEntity(sEntity));
            }
            _this.AlbumsLoaded = true;
            _this.InitializeModel();
        });
        //ServerApi.WaypointTackApi.GetDefault().Get().done(d => {
        //    for (let sEntity of d) { this.WaypointTacks.push(new ClientModel.WaypointTack().LoadFromServerEntity(sEntity)); }
        //    this.WaypointTacksLoaded = true;
        //    this.InitialozeModel();
        //});
        ServerApi.TackApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_9 = d; _i < d_9.length; _i++) {
                var sEntity = d_9[_i];
                _this.Tacks.push(new ClientModel.Tack().LoadFromServerEntity(sEntity));
            }
            _this.TacksLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.LocationApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_10 = d; _i < d_10.length; _i++) {
                var sEntity = d_10[_i];
                if (sEntity.Type === ServerModel.Location.GetType())
                    _this.Locations.push(new ClientModel.Location().LoadFromServerEntity(sEntity));
                else if (sEntity.Type === ServerModel.Restaurant.GetType())
                    _this.Restaurants.push(new ClientModel.Restaurant().LoadFromServerEntity(sEntity));
                else if (sEntity.Type === ServerModel.Supermarket.GetType())
                    _this.Supermarkets.push(new ClientModel.Supermarket().LoadFromServerEntity(sEntity));
            }
            _this.LocationsLoaded = true;
            _this.InitializeModel();
        });
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
            this.LocationsLoaded) {
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
            for (var _f = 0, _g = this.Images(); _f < _g.length; _f++) {
                var entity = _g[_f];
                this.GetAlbumById(entity.ParentAlbumId()).Images.push(entity);
            }
            for (var _h = 0, _j = mapViewModel.WaypointConnections(); _h < _j.length; _h++) {
                var connection = _j[_h];
                var polyline = mapViewModel.AddPolyline([
                    mapViewModel.GetWayPointById(connection.Waypoint1Id), mapViewModel
                        .GetWayPointById(connection.Waypoint2Id)
                ]);
                addDummyHandle(polyline);
            }
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
                elem.scrollIntoView();
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
            mapViewModel.HoveredPolyine = polyline;
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
                    var dist = wp.Distance + wp.ConnectedWayPoints[0].LatLng.distanceTo(wp.LatLng);
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
        mapViewModel.highlightedRoute.bindLabel(dist.toString() + " km", { noHide: true });
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
        var wp = new Waypoint(latLng, markerType, mapViewModel.Map);
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
                            ServerApi.WaypointConnectionApi.GetDefault()
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
        var h = new Harbour(name, latLng, this.Map);
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
        ServerApi.WaypointConnectionApi.GetDefault()
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
        ServerApi.WaypointConnectionApi.GetDefault()
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
    return MapViewModel;
}());
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
var dropzone;
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
        init: function () {
            dropzone = this;
            dropzone.on("success", function (e, data) {
                var image = new ClientModel.Image().LoadFromServerEntity(data);
                mapViewModel.Images.push(image);
                mapViewModel.GetAlbumById(image.ParentAlbumId()).Images.push(image);
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

//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIlZpZXdNb2RlbHMvSG9tZS9NYXAudHMiXSwibmFtZXMiOltdLCJtYXBwaW5ncyI6IkFBQUEsSUFBTyxRQUFRLEdBQUcsV0FBVyxDQUFDLE9BQU8sQ0FBQztBQUV0QyxJQUFPLFFBQVEsR0FBRyxXQUFXLENBQUMsUUFBUSxDQUFDO0FBQ3ZDLElBQU8sT0FBTyxHQUFHLFdBQVcsQ0FBQyxPQUFPLENBQUM7QUFDckMsSUFBTyxHQUFHLEdBQUcsV0FBVyxDQUFDLEdBQUcsQ0FBQztBQUM3QixJQUFPLGdCQUFnQixHQUFHLFdBQVcsQ0FBQyxnQkFBZ0IsQ0FBQztBQUV2RCxtQkFBbUIsR0FBZTtJQUM5QixJQUFNLEtBQUssR0FBRyxHQUFHLENBQUMsVUFBVSxFQUFFLENBQUMsQ0FBQyxDQUFDLENBQUM7SUFDbEMsSUFBTSxHQUFHLEdBQUcsR0FBRyxDQUFDLFVBQVUsRUFBRSxDQUFDLENBQUMsQ0FBQyxDQUFDO0lBQ2hDLE1BQU0sQ0FBQyxJQUFJLENBQUMsQ0FBQyxNQUFNLENBQUMsS0FBSyxDQUFDLEdBQUcsR0FBRyxDQUFDLENBQUMsR0FBRyxDQUFDLEdBQUcsR0FBRyxLQUFLLENBQUMsR0FBRyxDQUFDLEdBQUcsQ0FBQyxDQUFDLEVBQUUsS0FBSyxDQUFDLEdBQUcsR0FBRyxDQUFDLENBQUMsR0FBRyxDQUFDLEdBQUcsR0FBRyxLQUFLLENBQUMsR0FBRyxDQUFDLEdBQUcsQ0FBQyxDQUFDLENBQUMsQ0FBQztBQUMxRyxDQUFDO0FBRUQsdUJBQXVCLFFBQW9CO0lBQ3ZDLEVBQUUsQ0FBQyxDQUFDLFFBQVEsQ0FBQyxTQUFTLENBQUMsTUFBTSxLQUFLLENBQUMsSUFBSSxRQUFRLENBQUMsV0FBVyxZQUFZLFFBQVEsQ0FBQyxDQUFDLENBQUM7UUFDOUUsSUFBTSxFQUFFLEdBQUcsUUFBUSxDQUFDLFNBQVMsQ0FBQyxDQUFDLENBQUMsQ0FBQztRQUNqQyxJQUFNLEVBQUUsR0FBRyxRQUFRLENBQUMsV0FBVyxDQUFDO1FBQ2hDLElBQU0sRUFBRSxHQUFHLFFBQVEs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QyxTQUFTLEVBQUUsSUFBSSxHQUFHLENBQUMsRUFBRSxHQUFHLENBQUMsQ0FBQyxNQUFNLENBQUMsQ0FBQyxJQUFJLENBQUMsZ0JBQWdCLENBQUMsQ0FBQyxDQUFDLENBQUM7SUFFdkUsQ0FBQyxDQUFDLGlCQUFpQixDQUFDLENBQUMsR0FBRyxDQUFDLGlCQUFpQixDQUFDO1NBQ3RDLEdBQUcsQ0FBQyxTQUFTLEVBQUUsSUFBSSxHQUFHLENBQUMsRUFBRSxHQUFHLENBQUMsQ0FBQyxNQUFNLENBQUMsQ0FBQyxJQUFJLENBQUMsZ0JBQWdCLENBQUMsQ0FBQyxDQUFDLENBQUM7SUFHcEUsQ0FBQyxDQUFDLGlCQUFpQixDQUFDLENBQUMsR0FBRyxDQUFDLGdCQUFnQixDQUFDO1NBQ3JDLFFBQVEsQ0FBQyxnQkFBZ0IsQ0FBQyxDQUFDO0FBRXBDLENBQUMsQ0FBQyxDQUFDIiwiZmlsZSI6IlZpZXdNb2RlbHMvSG9tZS9NYXAuanMiLCJzb3VyY2VzQ29udGVudCI6WyJpbXBvcnQgU0hhcmJvdXIgPSBTZXJ2ZXJNb2RlbC5IYXJib3VyO1xyXG5cclxuaW1wb3J0IFdheXBvaW50ID0gQ2xpZW50TW9kZWwuV2F5cG9pbnQ7XHJcbmltcG9ydCBIYXJib3VyID0gQ2xpZW50TW9kZWwuSGFyYm91cjtcclxuaW1wb3J0IEpvYiA9IENsaWVudE1vZGVsLkpvYjtcclxuaW1wb3J0IFdheXBvaW50RGlzdGFuY2UgPSBDbGllbnRNb2RlbC5XYXlwb2ludERpc3RhbmNlO1xyXG5cclxuZnVuY3Rpb24gZ2V0TWlkZGxlKHBvbDogTC5Qb2x5bGluZSk6IEwuTGF0TG5nIHtcclxuICAgIGNvbnN0IHN0YXJ0ID0gcG9sLmdldExhdExuZ3MoKVswXTtcclxuICAgIGNvbnN0IGVuZCA9IHBvbC5nZXRMYXRMbmdzKClbMV07XHJcbiAgICByZXR1cm4gbmV3IEwuTGF0TG5nKHN0YXJ0LmxhdCArICgoZW5kLmxhdCAtIHN0YXJ0LmxhdCkgLyAyKSwgc3RhcnQubG5nICsgKChlbmQubG5nIC0gc3RhcnQubG5nKSAvIDIpKTtcclxufVxyXG5cclxuZnVuY3Rpb24gc3BsaXRQb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSkge1xyXG4gICAgaWYgKHBvbHlsaW5lLldheXBvaW50cy5sZW5ndGggPT09IDIgJiYgcG9seWxpbmUuRHVtbXlIYW5kbGUgaW5zdGFuY2VvZiBXYXlwb2ludCkge1xyXG4gICAgICAgIGNvbnN0IHcxID0gcG9seWxpbmUuV2F5cG9pbnRzWzBdO1xyXG4gICAgICAgIGNvbnN0IHcyID0gcG9seWxpbmUuRHVtbXlIYW5kbGU7XHJcbiAgICAgICAgY29uc3QgdzMgPSBwb2x5bGluZS5XYXlwb2ludHNbMV07XHJcbiAgICAgICAgdzIuUmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICB3Mi5BZGRUb1BvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICB3My5SZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgIGFkZER1bW15SGFuZGxlKHBvbHlsaW5lKTtcclxuICAgICAgICBhZGREdW1teUhhbmRsZShtYXBWaWV3TW9kZWwuQWRkUG9seWxpbmUoW3cyLCB3M10pKTtcclxuICAgICAgICByZXR1cm47XHJcbiAgICB9XHJcbiAgICB0aHJvdyBuZXcgRXJyb3IoXCJDYW5ub3Qgc3BsaXQgcG9seWxpbmUuIFBvbHlsaW5lIGhhcyBubyBkdW1teSBoYW5kbGUgb3IgbGVzcyBvciBtb3JlIHRoYW4gMiB3YXlwb2ludHNcIik7XHJcbn1cclxuXHJcblxyXG5mdW5jdGlvbiByZW1vdmVQb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSkge1xyXG4gICAgZm9yIChsZXQgd2F5cG9pbnQgb2YgcG9seWxpbmUuV2F5cG9pbnRzKSB7XHJcbiAgICAgICAgd2F5cG9pbnQuUmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgIH1cclxuICAgIGlmIChwb2x5bGluZS5EdW1teUhhbmRsZSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUuUmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZS5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICB9XHJcbiAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKHBvbHlsaW5lKTtcclxufVxyXG5cclxuZnVuY3Rpb24gYWRkRHVtbXlIYW5kbGUocG9seWxpbmU6IEwuUG9seWxpbmUpIHtcclxuICAgIGlmIChwb2x5bGluZS5EdW1teUhhbmRsZSA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUgPSBtYXBWaWV3TW9kZWwuQ3JlYXRlV2F5cG9pbnQoZ2V0TWlkZGxlKHBvbHlsaW5lKSwgTWFya2VyVHlwZS5EdW1teSk7XHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUuQWRkVG9Qb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICB9XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHJlZHJhd1BvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lKSB7XHJcbiAgICBjb25zdCBtaWRkbGVMYXRMbmcgPSBnZXRNaWRkbGUocG9seWxpbmUpO1xyXG4gICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgYWRkRHVtbXlIYW5kbGUocG9seWxpbmUpO1xyXG4gICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlLkxvbmdpdHVkZSgpICE9PSBtaWRkbGVMYXRMbmcubG5nIHx8IHBvbHlsaW5lLkR1bW15SGFuZGxlLkxhdGl0dWRlKCkgIT09IG1pZGRsZUxhdExuZy5sYXQpXHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUuU2V0TGF0TG5nKG1pZGRsZUxhdExuZyk7XHJcbiAgICBlbHNlXHJcbiAgICAgICAgcG9seWxpbmUucmVkcmF3KCk7XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHJlbW92ZUZyb21Qb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSwgbGF0TG5nOiBMLkxhdExuZykge1xyXG4gICAgcmVtb3ZlRnJvbUFycmF5KHBvbHlsaW5lLmdldExhdExuZ3MoKSwgbGF0TG5nKTtcclxuICAgIHBvbHlsaW5lLnJlZHJhdygpO1xyXG59XHJcblxyXG5mdW5jdGlvbiByZW1vdmVGcm9tQXJyYXk8VD4oYXJyOiBUW10sIG9iajogVCk6IGJvb2xlYW4ge1xyXG4gICAgY29uc3QgdG1wQXJyID0gbmV3IEFycmF5PFQ+KCk7XHJcbiAgICBmb3IgKGxldCBpdGVtIG9mIGFycikge1xyXG4gICAgICAgIGlmIChpdGVtICE9PSBvYmopXHJcbiAgICAgICAgICAgIHRtcEFyci5wdXNoKGl0ZW0pO1xyXG4gICAgfVxyXG4gICAgaWYgKHRtcEFyci5sZW5ndGggPT09IGFyci5sZW5ndGgpXHJcbiAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgd2hpbGUgKGFyci5wb3AoKSkge1xyXG4gICAgfVxyXG4gICAgd2hpbGUgKHRtcEFyci5sZW5ndGggPiAwKSB7XHJcbiAgICAgICAgYXJyLnB1c2godG1wQXJyLnNoaWZ0KCkpO1xyXG4gICAgfVxyXG4gICAgcmV0dXJuIHRydWU7XHJcbn1cclxuXHJcbmVudW0gTWFwTW9kZSB7XHJcbiAgICBBZG1pbixcclxuICAgIFZpZXcsXHJcbiAgICBUcmlwUGxhbm5pbmcsXHJcbiAgICBSb3V0ZURyYXdpbmdcclxufVxyXG5cclxuZGVjbGFyZSBuYW1lc3BhY2UgTCB7XHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIFBvbHlsaW5lIGV4dGVuZHMgUGF0aCB7XHJcblxyXG4gICAgICAgIFdheXBvaW50czogQXJyYXk8V2F5cG9pbnQ+O1xyXG4gICAgICAgIER1bW15SGFuZGxlOiBXYXlwb2ludDtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIExhdExuZyB7XHJcbiAgICAgICAgUG9seWxpbmVzOiBQb2x5bGluZVtdO1xyXG4gICAgICAgIFdheXBvaW50OiBXYXlwb2ludDtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIE1hcmtlciB7XHJcbiAgICAgICAgV2F5cG9pbnQ6IFdheXBvaW50O1xyXG4gICAgICAgIFBvaW50OiBMLlBvaW50O1xyXG4gICAgICAgIF9pY29uO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgQ2lyY2xlTWFya2VyIHtcclxuICAgICAgICBXYXlwb2ludDogV2F5cG9pbnQ7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGludGVyZmFjZSBQYXRoT3B0aW9ucyB7XHJcbiAgICAgICAgRHJhZ2dhYmxlPzogYm9vbGVhbjtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIE1hcmtlck9wdGlvbnMge1xyXG4gICAgICAgIGNvbnRleHRtZW51PzogYm9vbGVhbjtcclxuICAgICAgICBjb250ZXh0bWVudVdpZHRoPzogbnVtYmVyO1xyXG4gICAgICAgIGNvbnRleHRtZW51QW5jaG9yPzogTC5Qb2ludCB8IEwuUG9pbnRbXTtcclxuICAgICAgICBjb250ZXh0bWVudUl0ZW1zPzogY29udGV4dG1lbnVJdGVtW107XHJcbiAgICAgICAgY29udGV4dG1lbnVJbmhlcml0SXRlbXM6IGJvb2xlYW47XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgY29udGV4dG1lbnVJdGVtIHtcclxuICAgICAgICB0ZXh0Pzogc3RyaW5nO1xyXG4gICAgICAgIGljb24/OiBzdHJpbmc7XHJcbiAgICAgICAgaWNvbkNscz86IHN0cmluZztcclxuICAgICAgICBjYWxsYmFjaz86IEZ1bmN0aW9uO1xyXG4gICAgICAgIGNvbnRleHQ/OiBPYmplY3Q7XHJcbiAgICAgICAgZGlzYWJsZWQ/OiBib29sZWFuO1xyXG4gICAgICAgIHNlcGFyYXRvcj86IGJvb2xlYW47XHJcbiAgICAgICAgaGlkZU9uU2VsZWN0PzogYm9vbGVhbjtcclxuICAgICAgICBpbmRleD86IG51bWJlcjtcclxuICAgIH1cclxuXHJcblxyXG59XHJcblxyXG5jbGFzcyBNYXBWaWV3TW9kZWwge1xyXG4gICAgY29uc3RydWN0b3IobWFwTW9kZTogTWFwTW9kZSkge1xyXG4gICAgICAgIEwubWFwYm94XHJcbiAgICAgICAgICAgIC5hY2Nlc3NUb2tlbiA9XHJcbiAgICAgICAgICAgIFwicGsuZXlKMUlqb2laR0Z1YVdWc0xXdDFiMjRpTENKaElqb2lZMmxsZG5WdFkyOWlNREJpT0hReGJUQnZaekJxWldsNmNDSjkuVUVjMllxSDU5cEIxWVRwdjIydmc4QVwiO1xyXG4gICAgICAgIHRoaXMuTWFwTW9kZShtYXBNb2RlKTtcclxuICAgICAgICB0aGlzLk1hcE1vZGUuc3Vic2NyaWJlKCgpID0+IHtcclxuICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTWFwKCk7XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgY29uc3QgbWFwT3B0aW9uczogTC5tYXBib3guTWFwT3B0aW9ucyA9IHtcclxuICAgICAgICAgICAgY29udGV4dG1lbnU6IG1hcE1vZGUgPT09IE1hcE1vZGUuQWRtaW4sXHJcbiAgICAgICAgICAgIGNvbnRleHRtZW51SXRlbXM6IFtcclxuICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICB0ZXh0OiBcIk5ldWVyIEhhZmVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2s6IGZ1bmN0aW9uIChlKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGUpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRWRpdGluZ0hhcmJvdXIobWFwVmlld01vZGVsLkNyZWF0ZUhhcmJvdXIoXCJcIiwgZS5sYXRsbmcpKVxyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgXVxyXG4gICAgICAgIH07XHJcbiAgICAgICAgdGhpcy5NYXAgPSBMLm1hcGJveC5tYXAoXCJtYXBcIixcclxuICAgICAgICAgICAgXCJtYXBib3guc3RyZWV0c1wiLFxyXG4gICAgICAgICAgICBtYXBPcHRpb25zKTtcclxuICAgICAgICB0aGlzLk1hcC5zZXRWaWV3KFs1NC40MDc3NDE2NjgyMDA2OSwgMTAuNTIzNTI5MDUyNzM0MzczXSwgOSk7XHJcbiAgICAgICAgTC50aWxlTGF5ZXIoXCJodHRwOi8vdDEub3BlbnNlYW1hcC5vcmcvc2VhbWFyay97en0ve3h9L3t5fS5wbmdcIikuYWRkVG8odGhpcy5NYXApO1xyXG4gICAgICAgIHRoaXMuTG9hZERhdGEoKTtcclxuICAgICAgICB0aGlzLlNlbGVjdGVkSGFyYm91ci5zdWJzY3JpYmUoKG5ld0hhcmJvdXIpID0+IHtcclxuICAgICAgICAgICAgaWYgKG5ld0hhcmJvdXIgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkNhbGN1bGF0ZURpc3RhbmNlcyhuZXdIYXJib3VyKTtcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5IYXJib3Vycy5zb3J0KChoMSwgaDIpID0+IGgxLkRpc3RhbmNlKCkgLSBoMi5EaXN0YW5jZSgpKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGggb2YgbWFwVmlld01vZGVsLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAgICAgICAgICAgICBoLkRpc3RhbmNlKDApO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkID0gZmFsc2U7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5IaWRlUm91dGUoKTtcclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkVkaXRpbmdIYXJib3VyLnN1YnNjcmliZSgoaGFyYm91cikgPT4ge1xyXG4gICAgICAgICAgICBpZiAoaGFyYm91ciA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBlZGl0aW5nSGFyYm91ck1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIGhhcmJvdXIuU2F2ZVN0YXRlKCk7XHJcbiAgICAgICAgICAgICAgICBlZGl0aW5nSGFyYm91ck1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuRWRpdGluZ0hhcmJvdXIuc3Vic2NyaWJlKChoYXJib3VyKSA9PiB7XHJcbiAgICAgICAgICAgIGlmIChoYXJib3VyICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGhhcmJvdXIuUmV2ZXJ0U3RhdGUodHJ1ZSk7XHJcbiAgICAgICAgICAgICAgICBpZiAoaGFyYm91ci5JZCgpID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihoYXJib3VyLm1hcmtlcik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9LFxyXG4gICAgICAgICAgICB0aGlzLFxyXG4gICAgICAgICAgICBcImJlZm9yZUNoYW5nZVwiKTtcclxuICAgICAgICB0aGlzLkRlbGV0aW5nSGFyYm91ci5zdWJzY3JpYmUoKGgpID0+IHtcclxuICAgICAgICAgICAgaWYgKGggPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgZGVsZXRpbmdIYXJib3VyTW9kYWwubW9kYWwoXCJoaWRlXCIpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgZGVsZXRpbmdIYXJib3VyTW9kYWwubW9kYWwoXCJzaG93XCIpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5FZGl0aW5nV2F5cG9pbnQuc3Vic2NyaWJlKCh3YXlwb2ludCkgPT4ge1xyXG4gICAgICAgICAgICBpZiAod2F5cG9pbnQgPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgZWRpdGluZ1dheXBvaW50TW9kYWwubW9kYWwoXCJoaWRlXCIpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgd2F5cG9pbnQuU2F2ZVN0YXRlKCk7XHJcbiAgICAgICAgICAgICAgICBlZGl0aW5nV2F5cG9pbnRNb2RhbC5tb2RhbChcInNob3dcIik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkVkaXRpbmdXYXlwb2ludC5zdWJzY3JpYmUoKHdheXBvaW50KSA9PiB7XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuRWRpdGluZ1dheXBvaW50KCkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5FZGl0aW5nV2F5cG9pbnQoKS5SZXZlcnRTdGF0ZSh0cnVlKTtcclxuICAgICAgICB9LFxyXG4gICAgICAgICAgICB0aGlzLFxyXG4gICAgICAgICAgICBcImJlZm9yZUNoYW5nZVwiKTtcclxuICAgICAgICB0aGlzLkRlbGV0aW5nV2F5cG9pbnQuc3Vic2NyaWJlKChoKSA9PiB7XHJcbiAgICAgICAgICAgIGlmIChoID09PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGRlbGV0aW5nV2F5cG9pbnRNb2RhbC5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICBkZWxldGluZ1dheXBvaW50TW9kYWwubW9kYWwoXCJzaG93XCIpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5FZGl0aW5nSm9iLnN1YnNjcmliZSgoam9iKSA9PiB7XHJcbiAgICAgICAgICAgIGlmIChqb2IgPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgZWRpdGluZ0pvYk1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIGpvYi5TYXZlU3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVkaXRpbmdKb2JNb2RhbC5tb2RhbChcInNob3dcIik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkVkaXRpbmdKb2Iuc3Vic2NyaWJlKChqb2IpID0+IHtcclxuICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5FZGl0aW5nSm9iKCkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5FZGl0aW5nSm9iKCkuUmV2ZXJ0U3RhdGUodHJ1ZSk7XHJcbiAgICAgICAgfSxcclxuICAgICAgICAgICAgdGhpcyxcclxuICAgICAgICAgICAgXCJiZWZvcmVDaGFuZ2VcIik7XHJcbiAgICAgICAgdGhpcy5EZWxldGluZ0pvYi5zdWJzY3JpYmUoKGgpID0+IHtcclxuICAgICAgICAgICAgaWYgKGggPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgZGVsZXRpbmdKb2JNb2RhbC5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICBkZWxldGluZ0pvYk1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuU2VsZWN0ZWRIYXJib3VyLnN1YnNjcmliZSgoaCkgPT4ge1xyXG4gICAgICAgICAgICBpZiAoaCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgcmlnaHRTaWRlYmFyLkhpZGUoKTtcclxuICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgcmlnaHRTaWRlYmFyLlNob3coKTtcclxuXHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5TZWxlY3RlZFRyaXAuc3Vic2NyaWJlKCh0KSA9PiB7XHJcbiAgICAgICAgICAgIGlmICh0ID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICBib3R0b21TaWRlYmFyLkhpZGUoKTtcclxuICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgYm90dG9tU2lkZWJhci5TaG93KCk7XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5NYXAuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdXNlbW92ZVwiLFxyXG4gICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuR2V0TWFwTW9kZSgpID09PSBNYXBNb2RlLlJvdXRlRHJhd2luZykge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ0xhdExuZy5sYXQgPSBlLmxhdGxuZy5sYXQ7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nTGF0TG5nLmxuZyA9IGUubGF0bG5nLmxuZztcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdQb2x5bGluZS5yZWRyYXcoKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLk1hcE1vZGUoKSA9PT0gTWFwTW9kZS5BZG1pbilcclxuICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBtYXJrZXIgb2YgdGhpcy5XYXlwb2ludE1hcmtlcnMpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKG1hcmtlci5Qb2ludC5kaXN0YW5jZVRvKGUuY29udGFpbmVyUG9pbnQpIDwgMTUwKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFya2VyLnNldE9wYWNpdHkobWFya2VyLldheXBvaW50LklzRHVtbXkoKSA/IDAuMCA6IDEpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXJrZXIuc2V0T3BhY2l0eShtYXJrZXIuV2F5cG9pbnQuSXNEdW1teSgpID8gMC4wIDogMC44KTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lICE9PSB1bmRlZmluZWQgJiYgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lLkR1bW15SGFuZGxlICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBwb2x5bGluZSA9IG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZTtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBwMSA9IG1hcFZpZXdNb2RlbC5NYXAubGF0TG5nVG9Db250YWluZXJQb2ludChwb2x5bGluZS5nZXRMYXRMbmdzKClbMF0pO1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IHAyID0gbWFwVmlld01vZGVsLk1hcC5sYXRMbmdUb0NvbnRhaW5lclBvaW50KHBvbHlsaW5lLmdldExhdExuZ3MoKVsxXSk7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHAxLmRpc3RhbmNlVG8oZS5jb250YWluZXJQb2ludCkgPCAyMCB8fCBwMi5kaXN0YW5jZVRvKGUuY29udGFpbmVyUG9pbnQpIDwgMjApIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZS5EdW1teUhhbmRsZS5tYXJrZXIuc2V0T3BhY2l0eSgwLjgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSG92ZXJlZFBvbHlpbmUuRHVtbXlIYW5kbGVcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5TZXRMYXRMbmcobWFwVmlld01vZGVsLk1hcFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5jb250YWluZXJQb2ludFRvTGF0TG5nKEwuTGluZVV0aWxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgLmNsb3Nlc3RQb2ludE9uU2VnbWVudChlLmNvbnRhaW5lclBvaW50LFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBwMSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgcDIpKSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGZhbHNlKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwiY2xpY2tcIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkdldE1hcE1vZGUoKSA9PT0gTWFwTW9kZS5Sb3V0ZURyYXdpbmcpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCB3YXlwb2ludCA9IG1hcFZpZXdNb2RlbC5DcmVhdGVXYXlwb2ludChlLmxhdGxuZywgTWFya2VyVHlwZS5XYXlwb2ludCk7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3Qgc3RhcnRJZCA9IHRoaXMuRHJhd2luZ1BvbHlsaW5lLldheXBvaW50c1swXS5JZCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIHdheXBvaW50LlNhdmVUb1NlcnZlcigpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5kb25lKHcgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAuQ29ubmVjdCh3LklkLCBzdGFydElkKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICAgICAgd2F5cG9pbnQuQWRkVG9Qb2x5bGluZSh0aGlzLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgYWRkRHVtbXlIYW5kbGUodGhpcy5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21Qb2x5bGluZSh0aGlzLkRyYXdpbmdQb2x5bGluZSwgdGhpcy5EcmF3aW5nTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdQb2x5bGluZSA9IHRoaXMuQWRkUG9seWxpbmUod2F5cG9pbnQpO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ0xhdExuZyA9IG5ldyBMLkxhdExuZyhlLmxhdGxuZy5sYXQsIGUubGF0bG5nLmxuZyk7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nUG9seWxpbmUuYWRkTGF0TG5nKHRoaXMuRHJhd2luZ0xhdExuZyk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0pO1xyXG5cclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwiZGJsY2xpY2tcIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkdldE1hcE1vZGUoKSA9PT0gTWFwTW9kZS5Sb3V0ZURyYXdpbmcpIHtcclxuICAgICAgICAgICAgICAgICAgICBlLm9yaWdpbmFsRXZlbnQuY2FuY2VsQnViYmxlID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgICAgICBlLm9yaWdpbmFsRXZlbnQucHJldmVudERlZmF1bHQoKTtcclxuICAgICAgICAgICAgICAgICAgICBlLm9yaWdpbmFsRXZlbnQuc3RvcFByb3BhZ2F0aW9uKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nUG9seWxpbmUuYWRkTGF0TG5nKGUubGF0bG5nKTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdMYXRMbmcgPSBlLmxhdGxuZztcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgJChkb2N1bWVudClcclxuICAgICAgICAgICAgLmtleXVwKChlOiBKUXVlcnlLZXlFdmVudE9iamVjdCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuR2V0TWFwTW9kZSgpID09PSBNYXBNb2RlLlJvdXRlRHJhd2luZykge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChlLmtleUNvZGUgPT09IDI3KSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuUmVtb3ZlUG9seWxpbmUodGhpcy5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5NYXAuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdmVcIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IG1hcmtlciBvZiB0aGlzLldheXBvaW50TWFya2Vycykge1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcmtlci5Qb2ludCA9IHRoaXMuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQobWFya2VyLmdldExhdExuZygpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5NYXAuYWRkRXZlbnRMaXN0ZW5lcihcInpvb21cIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IG1hcmtlciBvZiB0aGlzLldheXBvaW50TWFya2Vycykge1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcmtlci5Qb2ludCA9IHRoaXMuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQobWFya2VyLmdldExhdExuZygpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgcHJpdmF0ZSByb3V0ZVBvbHlsaW5lID0ga28ub2JzZXJ2YWJsZTxMLlBvbHlsaW5lPigpO1xyXG5cclxuICAgIFN0YXJ0Um91dGUoKSB7XHJcbiAgICAgICAgY29uc3QgdHJpcCA9IG5ldyBDbGllbnRNb2RlbC5UcmlwKCk7XHJcbiAgICAgICAgY29uc3QgdGFjayA9IG5ldyBDbGllbnRNb2RlbC5UYWNrKCk7XHJcbiAgICAgICAgY29uc3QgaGFyYm91ciA9IG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIoKTtcclxuICAgICAgICB0YWNrLlN0YXJ0KGhhcmJvdXIpO1xyXG4gICAgICAgIHRyaXAuVGFja3MucHVzaCh0YWNrKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKHRyaXApO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZVBvbHlsaW5lKEwucG9seWxpbmUoW10sXHJcbiAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgIGNvbG9yOiBcIiMwMDk5MDBcIlxyXG4gICAgICAgICAgICB9KSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRUbyhtYXBWaWV3TW9kZWwuTWFwKTtcclxuICAgIH1cclxuXHJcbiAgICBJc0xhc3RUYWtJblJvdXRlID0ga28uY29tcHV0ZWQoe1xyXG4gICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgdmFyIHRyaXAgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKCk7XHJcbiAgICAgICAgICAgIHZhciBoID0gbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cigpO1xyXG4gICAgICAgICAgICByZXR1cm4gdHJpcCAhPT0gdW5kZWZpbmVkICYmIGggIT09IHVuZGVmaW5lZCAmJiB0cmlwLlRhY2tzKClbdHJpcC5UYWNrcygpLmxlbmd0aCAtIDFdLlN0YXJ0KCkgPT09IGg7XHJcbiAgICAgICAgfSxcclxuICAgICAgICBkZWZlckV2YWx1YXRpb246IHRydWVcclxuICAgIH0pO1xyXG5cclxuICAgIEdldFJvdXRlRGlzdGFuY2UgPSBrby5jb21wdXRlZCh7XHJcbiAgICAgICAgcmVhZDogKCkgPT4ge1xyXG4gICAgICAgICAgICB2YXIgZGlzdGFuY2UgPSAwO1xyXG4gICAgICAgICAgICBmb3IgKGxldCB0YWNrIG9mIG1hcFZpZXdNb2RlbC5TZWxlY3RlZFRyaXAoKS5UYWNrcygpKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAoIWlzTmFOKHRhY2suRGlzdGFuY2UoKSkpXHJcbiAgICAgICAgICAgICAgICAgICAgZGlzdGFuY2UgKz0gdGFjay5EaXN0YW5jZSgpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiBkaXN0YW5jZTtcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG4gICAgVG9wSm9icyA9IGtvLmNvbXB1dGVkKHtcclxuICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgIHJldHVybiBtYXBWaWV3TW9kZWwuSm9icygpLmZpbHRlcigoaikgPT4gai5TdXBlckpvYklkKCkgPT09IHVuZGVmaW5lZCk7XHJcbiAgICAgICAgfSxcclxuICAgICAgICBkZWZlckV2YWx1YXRpb246IHRydWVcclxuICAgIH0pO1xyXG5cclxuICAgIEFkZFRvUm91dGUoKSB7XHJcbiAgICAgICAgY29uc3QgdHJpcCA9IG1hcFZpZXdNb2RlbC5TZWxlY3RlZFRyaXAoKTtcclxuICAgICAgICBjb25zdCB0YXJnZXRIYXJib3VyID0gbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cigpO1xyXG4gICAgICAgIGNvbnN0IHRhY2sgPSBuZXcgQ2xpZW50TW9kZWwuVGFjaygpO1xyXG4gICAgICAgIGNvbnN0IGxhc3RUYWNrID0gdHJpcC5UYWNrcygpW3RyaXAuVGFja3MoKS5sZW5ndGggLSAxXTtcclxuICAgICAgICBjb25zdCBzdGFydEhhcmJvdXIgPSBsYXN0VGFjay5TdGFydCgpO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5DYWxjdWxhdGVEaXN0YW5jZXModGFyZ2V0SGFyYm91ciwgc3RhcnRIYXJib3VyKTtcclxuICAgICAgICBsYXN0VGFjay5EaXN0YW5jZShzdGFydEhhcmJvdXIuUm91dGVEaXN0YW5jZSgpKTtcclxuICAgICAgICBsZXQgd3A6IENsaWVudE1vZGVsLldheXBvaW50ID0gc3RhcnRIYXJib3VyO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZVBvbHlsaW5lKCkuYWRkTGF0TG5nKHdwLkxhdExuZyk7XHJcbiAgICAgICAgd2hpbGUgKHdwLlJvdXRlUHJlY2Vzc29yKCkgIT09IHVuZGVmaW5lZCAvKiYmIHdwLlJvdXRlUHJlY2Vzc29yKCkgIT09IHN0YXJ0SGFyYm91ciovKSB7XHJcbiAgICAgICAgICAgIHdwID0gd3AuUm91dGVQcmVjZXNzb3IoKTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRMYXRMbmcod3AuTGF0TG5nKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGxhc3RUYWNrLkVuZCh0YXJnZXRIYXJib3VyKTtcclxuICAgICAgICB0YWNrLlN0YXJ0KHRhcmdldEhhcmJvdXIpO1xyXG4gICAgICAgIHRyaXAuVGFja3MucHVzaCh0YWNrKTtcclxuICAgIH1cclxuXHJcbiAgICBSZWRyYXdUcmlwKCkge1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5NYXAucmVtb3ZlTGF5ZXIobWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoTC5wb2x5bGluZShbXSxcclxuICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgY29sb3I6IFwiIzAwOTkwMFwiXHJcbiAgICAgICAgICAgIH0pKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpLmFkZFRvKG1hcFZpZXdNb2RlbC5NYXApO1xyXG4gICAgICAgIGZvciAobGV0IHRhY2sgb2YgbWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpLlRhY2tzKCkpIHtcclxuICAgICAgICAgICAgY29uc3QgdGFyZ2V0SGFyYm91ciA9IHRhY2suRW5kKCk7XHJcbiAgICAgICAgICAgIGNvbnN0IHN0YXJ0SGFyYm91ciA9IHRhY2suU3RhcnQoKTtcclxuICAgICAgICAgICAgaWYgKHRhcmdldEhhcmJvdXIgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIGNvbnRpbnVlO1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuQ2FsY3VsYXRlRGlzdGFuY2VzKHRhcmdldEhhcmJvdXIsIHN0YXJ0SGFyYm91cik7XHJcbiAgICAgICAgICAgIHRhY2suRGlzdGFuY2Uoc3RhcnRIYXJib3VyLlJvdXRlRGlzdGFuY2UoKSk7XHJcbiAgICAgICAgICAgIGxldCB3cDogQ2xpZW50TW9kZWwuV2F5cG9pbnQgPSBzdGFydEhhcmJvdXI7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZVBvbHlsaW5lKCkuYWRkTGF0TG5nKHdwLkxhdExuZyk7XHJcbiAgICAgICAgICAgIHdoaWxlICh3cC5Sb3V0ZVByZWNlc3NvcigpICE9PSB1bmRlZmluZWQgLyomJiB3cC5Sb3V0ZVByZWNlc3NvcigpICE9PSBzdGFydEhhcmJvdXIqLykge1xyXG4gICAgICAgICAgICAgICAgd3AgPSB3cC5Sb3V0ZVByZWNlc3NvcigpO1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRMYXRMbmcod3AuTGF0TG5nKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBQdWxsVGFjaygpIHtcclxuICAgICAgICBjb25zdCB0YWNrOiBDbGllbnRNb2RlbC5UYWNrID0gPGFueT50aGlzO1xyXG4gICAgICAgIGNvbnN0IHRhY2tzID0gbWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpLlRhY2tzO1xyXG4gICAgICAgIGNvbnN0IGluZGV4ID0gdGFja3MuaW5kZXhPZih0YWNrKTtcclxuICAgICAgICBjb25zdCBwcmV2VGFjayA9IHRhY2tzKClbaW5kZXggLSAxXTtcclxuICAgICAgICB2YXIgdG1wRW5kID0gdGFjay5FbmQoKTtcclxuICAgICAgICB0YWNrLkVuZChwcmV2VGFjay5TdGFydCgpKTtcclxuICAgICAgICBwcmV2VGFjay5FbmQodG1wRW5kKTtcclxuICAgICAgICBpZiAoaW5kZXggPiAxKSB7XHJcbiAgICAgICAgICAgIHRhY2tzKClbaW5kZXggLSAyXS5FbmQodGFjay5TdGFydCgpKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgdGFja3Muc3BsaWNlKGluZGV4IC0gMSwgMiwgdGFjaywgcHJldlRhY2spO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5SZWRyYXdUcmlwKCk7XHJcbiAgICB9XHJcblxyXG4gICAgUHVzaFRhY2soKSB7XHJcbiAgICAgICAgY29uc3QgdGFjazogQ2xpZW50TW9kZWwuVGFjayA9IDxhbnk+dGhpcztcclxuICAgICAgICBjb25zdCB0YWNrcyA9IG1hcFZpZXdNb2RlbC5TZWxlY3RlZFRyaXAoKS5UYWNrcztcclxuICAgICAgICBjb25zdCBpbmRleCA9IHRhY2tzLmluZGV4T2YodGFjayk7XHJcbiAgICAgICAgY29uc3QgbmV4dFRhY2sgPSB0YWNrcygpW2luZGV4ICsgMV07XHJcbiAgICAgICAgdGFjay5FbmQobmV4dFRhY2suRW5kKCkpO1xyXG4gICAgICAgIG5leHRUYWNrLkVuZCh0YWNrLlN0YXJ0KCkpO1xyXG4gICAgICAgIGlmIChpbmRleCA+IDApIHtcclxuICAgICAgICAgICAgdGFja3MoKVtpbmRleCAtIDFdLkVuZChuZXh0VGFjay5TdGFydCgpKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgdGFja3Muc3BsaWNlKGluZGV4LCAyLCBuZXh0VGFjaywgdGFjayk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlJlZHJhd1RyaXAoKTtcclxuICAgIH1cclxuXHJcbiAgICBSZW1vdmVUYWNrKCkge1xyXG4gICAgICAgIGNvbnN0IHRhY2s6IENsaWVudE1vZGVsLlRhY2sgPSA8YW55PnRoaXM7XHJcbiAgICAgICAgY29uc3QgdGFja3MgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKCkuVGFja3M7XHJcbiAgICAgICAgY29uc3QgaW5kZXggPSB0YWNrcy5pbmRleE9mKHRhY2spO1xyXG4gICAgICAgIGNvbnN0IHByZXZUYWNrID0gdGFja3MoKVtpbmRleCAtIDFdO1xyXG4gICAgICAgIGlmIChwcmV2VGFjayAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICBwcmV2VGFjay5FbmQodGFjay5FbmQoKSk7XHJcbiAgICAgICAgdGFja3MucmVtb3ZlKHRhY2spO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5SZWRyYXdUcmlwKCk7XHJcbiAgICB9XHJcblxyXG4gICAgSXNJblZpZXdNb2RlID0ga28uY29tcHV0ZWQ8Ym9vbGVhbj4oe1xyXG4gICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuVmlldztcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG4gICAgSXNJbkFkbWluTW9kZSA9IGtvLmNvbXB1dGVkPGJvb2xlYW4+KHtcclxuICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgIHJldHVybiBtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluO1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgZGVmZXJFdmFsdWF0aW9uOiB0cnVlXHJcbiAgICB9KTtcclxuXHJcblxyXG4gICAgTG9hZERhdGEoKSB7XHJcbiAgICAgICAgU2VydmVyQXBpLldheXBvaW50QXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoc0VudGl0eS5UeXBlID09PSBTZXJ2ZXJNb2RlbC5XYXlwb2ludC5HZXRUeXBlKCkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRzLnB1c2gobWFwVmlld01vZGVsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAuQ3JlYXRlV2F5cG9pbnQoTC5sYXRMbmcoc0VudGl0eS5MYXRpdHVkZSwgc0VudGl0eS5Mb25naXR1ZGUpLCBNYXJrZXJUeXBlLldheXBvaW50KVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgICAgICBlbHNlIGlmIChzRW50aXR5LlR5cGUgPT09IFNlcnZlck1vZGVsLkhhcmJvdXIuR2V0VHlwZSgpKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGNvbnN0IGhhcmJvdXIgPSBtYXBWaWV3TW9kZWxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5DcmVhdGVIYXJib3VyKHNFbnRpdHkuTmFtZSwgTC5sYXRMbmcoc0VudGl0eS5MYXRpdHVkZSwgc0VudGl0eS5Mb25naXR1ZGUpKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkgYXMgU0hhcmJvdXIpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkhhcmJvdXJzLnB1c2goaGFyYm91cik7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5XYXlwb2ludENvbm5lY3Rpb25BcGkuR2V0RGVmYXVsdCgpXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRDb25uZWN0aW9ucy5wdXNoKHNFbnRpdHkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5XYXlwb2ludENvbm5lY3Rpb25zTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5QZXJzb25BcGkuR2V0RGVmYXVsdCgpXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuUGVyc29ucy5wdXNoKG5ldyBDbGllbnRNb2RlbC5QZXJzb24oKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLlBlcnNvbnNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLkpvYkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Kb2JzLnB1c2gobmV3IENsaWVudE1vZGVsLkpvYigpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuSm9ic0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuVHJpcEFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Ucmlwcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5UcmlwKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5Ucmlwc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuQWRkcmVzc0FwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5BZGRyZXNzZXMucHVzaChuZXcgQ2xpZW50TW9kZWwuQWRkcmVzcygpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuQWRkcmVzc2VzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5JbWFnZUFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5JbWFnZXMucHVzaChuZXcgQ2xpZW50TW9kZWwuSW1hZ2UoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLkltYWdlc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuQWxidW1BcGkuR2V0RGVmYXVsdCgpXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuQWxidW1zLnB1c2gobmV3IENsaWVudE1vZGVsLkFsYnVtKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5BbGJ1bXNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgLy9TZXJ2ZXJBcGkuV2F5cG9pbnRUYWNrQXBpLkdldERlZmF1bHQoKS5HZXQoKS5kb25lKGQgPT4ge1xyXG4gICAgICAgIC8vICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkgeyB0aGlzLldheXBvaW50VGFja3MucHVzaChuZXcgQ2xpZW50TW9kZWwuV2F5cG9pbnRUYWNrKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpOyB9XHJcbiAgICAgICAgLy8gICAgdGhpcy5XYXlwb2ludFRhY2tzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAvLyAgICB0aGlzLkluaXRpYWxvemVNb2RlbCgpO1xyXG4gICAgICAgIC8vfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLlRhY2tBcGkuR2V0RGVmYXVsdCgpXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuVGFja3MucHVzaChuZXcgQ2xpZW50TW9kZWwuVGFjaygpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuVGFja3NMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLkxvY2F0aW9uQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoc0VudGl0eS5UeXBlID09PSBTZXJ2ZXJNb2RlbC5Mb2NhdGlvbi5HZXRUeXBlKCkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuTG9jYXRpb25zLnB1c2gobmV3IENsaWVudE1vZGVsLkxvY2F0aW9uKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsc2UgaWYgKHNFbnRpdHkuVHlwZSA9PT0gU2VydmVyTW9kZWwuUmVzdGF1cmFudC5HZXRUeXBlKCkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuUmVzdGF1cmFudHMucHVzaChuZXcgQ2xpZW50TW9kZWwuUmVzdGF1cmFudCgpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgICAgICBlbHNlIGlmIChzRW50aXR5LlR5cGUgPT09IFNlcnZlck1vZGVsLlN1cGVybWFya2V0LkdldFR5cGUoKSlcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5TdXBlcm1hcmtldHMucHVzaChuZXcgQ2xpZW50TW9kZWwuU3VwZXJtYXJrZXQoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLkxvY2F0aW9uc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgIH1cclxuXHJcbiAgICBJbml0aWFsaXplTW9kZWwoKSB7XHJcbiAgICAgICAgaWYgKHRoaXMuV2F5cG9pbnRzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRDb25uZWN0aW9uc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLlBlcnNvbnNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5Kb2JzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuVHJpcHNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5BZGRyZXNzZXNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5JbWFnZXNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5BbGJ1bXNMb2FkZWQgJiZcclxuICAgICAgICAgICAgLy90aGlzLldheXBvaW50VGFja3NMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5UYWNrc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLkxvY2F0aW9uc0xvYWRlZCkge1xyXG4gICAgICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5Kb2JzKCkpIHtcclxuICAgICAgICAgICAgICAgIGlmIChlbnRpdHkuQXNzaWduZWRUb0lkKCkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICBlbnRpdHkuQXNzaWduZWRUbyh0aGlzLkdldFBlcnNvbkJ5SWQoZW50aXR5LkFzc2lnbmVkVG9JZCgpKSk7XHJcbiAgICAgICAgICAgICAgICBpZiAoZW50aXR5LlRyaXBJZCgpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgZW50aXR5LlRyaXAodGhpcy5HZXRUcmlwQnlJZChlbnRpdHkuVHJpcElkKCkpKTtcclxuICAgICAgICAgICAgICAgIGlmIChlbnRpdHkuU3VwZXJKb2JJZCgpICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBlbnRpdHkuU3VwZXJKb2IodGhpcy5HZXRKb2JCeUlkKGVudGl0eS5TdXBlckpvYklkKCkpKTtcclxuICAgICAgICAgICAgICAgICAgICBlbnRpdHkuU3VwZXJKb2IoKS5TdWJKb2JzLnB1c2goZW50aXR5KTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgICAgICBlbnRpdHkuQWxidW0odGhpcy5HZXRBbGJ1bUJ5SWQoZW50aXR5LkFsYnVtSWQoKSkpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkxvY2F0aW9ucygpKSB7XHJcbiAgICAgICAgICAgICAgICBlbnRpdHkuQWRkcmVzcyh0aGlzLkdldEFkZHJlc3NCeUlkKGVudGl0eS5BZGRyZXNzSWQoKSkpO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5HZXRIYXJib3VyQnlJZChlbnRpdHkuSGFyYm91cklkKCkpLkxvY2F0aW9ucy5wdXNoKGVudGl0eSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSW1hZ2VzKCkpIHtcclxuICAgICAgICAgICAgICAgIHRoaXMuR2V0QWxidW1CeUlkKGVudGl0eS5QYXJlbnRBbGJ1bUlkKCkpLkltYWdlcy5wdXNoKGVudGl0eSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgY29ubmVjdGlvbiBvZiBtYXBWaWV3TW9kZWwuV2F5cG9pbnRDb25uZWN0aW9ucygpKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwb2x5bGluZSA9IG1hcFZpZXdNb2RlbC5BZGRQb2x5bGluZShbXHJcbiAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkdldFdheVBvaW50QnlJZChjb25uZWN0aW9uLldheXBvaW50MUlkKSwgbWFwVmlld01vZGVsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5HZXRXYXlQb2ludEJ5SWQoY29ubmVjdGlvbi5XYXlwb2ludDJJZClcclxuICAgICAgICAgICAgICAgIF0pO1xyXG4gICAgICAgICAgICAgICAgYWRkRHVtbXlIYW5kbGUocG9seWxpbmUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICQoXCIjbG9hZGluZ092ZXJsYXlcIikucmVtb3ZlKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIEluaXRpYWxpemVNYXAoKSB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cih1bmRlZmluZWQpO1xyXG4gICAgICAgIGZvciAobGV0IHdwIG9mIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMoKSkge1xyXG4gICAgICAgICAgICBpZiAod3AubWFya2VyICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKHdwLm1hcmtlcik7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DcmVhdGVNYXJrZXIoTWFya2VyVHlwZS5XYXlwb2ludCwgd3ApO1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCBoIG9mIG1hcFZpZXdNb2RlbC5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgIGlmIChoLm1hcmtlciAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihoLm1hcmtlcik7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DcmVhdGVNYXJrZXIoTWFya2VyVHlwZS5IYXJib3VyLCBoKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgcCBvZiBtYXBWaWV3TW9kZWwuUG9seWxpbmVzKSB7XHJcbiAgICAgICAgICAgIGlmIChwLkR1bW15SGFuZGxlLm1hcmtlciAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihwLkR1bW15SGFuZGxlLm1hcmtlcik7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DcmVhdGVNYXJrZXIoTWFya2VyVHlwZS5EdW1teSwgcC5EdW1teUhhbmRsZSk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKSB7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHAgb2YgbWFwVmlld01vZGVsLlBvbHlsaW5lcykge1xyXG4gICAgICAgICAgICAgICAgcC5hZGRUbyhtYXBWaWV3TW9kZWwuTWFwKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLmNvbnRleHRtZW51LmVuYWJsZSgpO1xyXG4gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHAgb2YgbWFwVmlld01vZGVsLlBvbHlsaW5lcykge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihwKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLmNvbnRleHRtZW51LmRpc2FibGUoKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgTWFwOiBMLm1hcGJveC5NYXA7XHJcblxyXG4gICAgR2V0V2F5cG9pbnRCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5XYXlwb2ludCB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gV2F5cG9pbnQgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRIYXJib3VyQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuSGFyYm91ciB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSGFyYm91ciB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgfVxyXG5cclxuICAgIEdldFBlcnNvbkJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLlBlcnNvbiB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuUGVyc29ucygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBQZXJzb24gd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRKb2JCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5Kb2Ige1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkpvYnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSm9iIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICB9XHJcblxyXG4gICAgR2V0VHJpcEJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLlRyaXAge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLlRyaXBzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgLy90aHJvdyBcIk5vIFRyaXAgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRBZGRyZXNzQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuQWRkcmVzcyB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuQWRkcmVzc2VzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgLy90aHJvdyBcIk5vIEFkZHJlc3Mgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRJbWFnZUJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLkltYWdlIHtcclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5JbWFnZXMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSW1hZ2Ugd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRUYWNrQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuVGFjayB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuVGFja3MoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gVGFjayB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgfVxyXG5cclxuICAgIEdldEFsYnVtQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuQWxidW0ge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkFsYnVtcygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBUYWNrIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICB9XHJcblxyXG4gICAgR2V0TG9jYXRpb25CeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5Mb2NhdGlvbiB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuTG9jYXRpb25zKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuUmVzdGF1cmFudHMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5SZXN0YXVyYW50cygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBMb2NhdGlvbiB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgfVxyXG5cclxuICAgIFdheXBvaW50c0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgV2F5cG9pbnRDb25uZWN0aW9uc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgUGVyc29uc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgSm9ic0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgVHJpcHNMb2FkZWQgPSBmYWxzZTtcclxuICAgIEFkZHJlc3Nlc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgSW1hZ2VzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBBbGJ1bXNMb2FkZWQgPSBmYWxzZTtcclxuICAgIFdheXBvaW50VGFja3NMb2FkZWQgPSBmYWxzZTtcclxuICAgIFRhY2tzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBMb2NhdGlvbnNMb2FkZWQgPSBmYWxzZTtcclxuXHJcbiAgICBXYXlwb2ludHMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuV2F5cG9pbnQ+KCk7XHJcbiAgICBXYXlwb2ludENvbm5lY3Rpb25zID0ga28ub2JzZXJ2YWJsZUFycmF5PFNlcnZlck1vZGVsLldheXBvaW50Q29ubmVjdGlvbj4oKTtcclxuICAgIEhhcmJvdXJzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkhhcmJvdXI+KCk7XHJcbiAgICBQZXJzb25zID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLlBlcnNvbj4oKTtcclxuICAgIEpvYnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuSm9iPigpO1xyXG4gICAgVHJpcHMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuVHJpcD4oKTtcclxuICAgIEFkZHJlc3NlcyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5BZGRyZXNzPigpO1xyXG4gICAgSW1hZ2VzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkltYWdlPigpO1xyXG4gICAgVGFja3MgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuVGFjaz4oKTtcclxuICAgIExvY2F0aW9ucyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5Mb2NhdGlvbj4oKTtcclxuICAgIFN1cGVybWFya2V0cyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5TdXBlcm1hcmtldD4oKTtcclxuICAgIFJlc3RhdXJhbnRzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLlJlc3RhdXJhbnQ+KCk7XHJcbiAgICBBbGJ1bXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuQWxidW0+KCk7XHJcblxyXG5cclxuICAgIFNlbGVjdGVkV2F5cG9pbnQgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLldheXBvaW50PigpO1xyXG4gICAgU2VsZWN0ZWRIYXJib3VyID0ga28ub2JzZXJ2YWJsZTxDbGllbnRNb2RlbC5IYXJib3VyPigpO1xyXG4gICAgU2VsZWN0ZWRQZXJzb24gPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLlBlcnNvbj4oKTtcclxuICAgIFNlbGVjdGVkSm9iID0ga28ub2JzZXJ2YWJsZTxDbGllbnRNb2RlbC5Kb2I+KCk7XHJcbiAgICBTZWxlY3RlZFRyaXAgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLlRyaXA+KCk7XHJcbiAgICBTZWxlY3RlZEFkZHJlc3MgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLkFkZHJlc3M+KCk7XHJcbiAgICBTZWxlY3RlZEltYWdlID0ga28ub2JzZXJ2YWJsZTxDbGllbnRNb2RlbC5JbWFnZT4oKTtcclxuICAgIFNlbGVjdGVkVGFjayA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuVGFjaz4oKTtcclxuICAgIFNlbGVjdGVkTG9jYXRpb24gPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLkxvY2F0aW9uPigpO1xyXG4gICAgU2VsZWN0ZWRTdXBlcm1hcmtldCA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuU3VwZXJtYXJrZXQ+KCk7XHJcbiAgICBTZWxlY3RlZFJlc3RhdXJhbnQgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLlJlc3RhdXJhbnQ+KCk7XHJcblxyXG4gICAgSW5pdEdhbGxlcnkoKSB7XHJcbiAgICAgICAgY29uc3QgaXRlbXMgPSBuZXcgQXJyYXk8UGhvdG9Td2lwZS5JdGVtPigpO1xyXG4gICAgICAgIGNvbnN0IGN1cnJJbWFnZTogQ2xpZW50TW9kZWwuSW1hZ2UgPSB0aGlzIGFzIGFueTtcclxuICAgICAgICBmb3IgKGxldCBkYXRhIG9mIG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIoKS5BbGJ1bSgpLkltYWdlcygpKSB7XHJcbiAgICAgICAgICAgIGl0ZW1zLnB1c2goKHtcclxuICAgICAgICAgICAgICAgIGg6IGRhdGEuSGVpZ2h0KCksXHJcbiAgICAgICAgICAgICAgICB3OiBkYXRhLldpZHRoKCksXHJcbiAgICAgICAgICAgICAgICBzcmM6IGRhdGEuUGF0aCgpXHJcbiAgICAgICAgICAgIH0gYXMgYW55KSk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGdhbGxlcnkgPSBuZXcgUGhvdG9Td2lwZShwc3dwLFxyXG4gICAgICAgICAgICBQaG90b1N3aXBlVUlfRGVmYXVsdCxcclxuICAgICAgICAgICAgaXRlbXMsXHJcbiAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgIGluZGV4OiBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRIYXJib3VyKCkuQWxidW0oKS5JbWFnZXMuaW5kZXhPZihjdXJySW1hZ2UpIGFzIG51bWJlcixcclxuICAgICAgICAgICAgICAgIGdldFRodW1iQm91bmRzRm46IChpbmRleDogbnVtYmVyKTogeyB4OiBudW1iZXI7IHk6IG51bWJlcjsgdzogbnVtYmVyIH0gPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IGVsZW0gPSAkKFwiLmltYWdlczpmaXJzdCBpbWdcIilbaW5kZXhdO1xyXG4gICAgICAgICAgICAgICAgICAgIHZhciBwYWRkaW5nID0gcGFyc2VGbG9hdCh3aW5kb3cuZ2V0Q29tcHV0ZWRTdHlsZShlbGVtLCBudWxsKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAuZ2V0UHJvcGVydHlWYWx1ZShcInBhZGRpbmctbGVmdFwiKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAucmVwbGFjZShcInB4XCIsIFwiXCIpKTtcclxuICAgICAgICAgICAgICAgICAgICBlbGVtLnNjcm9sbEludG9WaWV3KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgYm91bmRzID0gZWxlbS5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTtcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB4OiBib3VuZHMubGVmdCArIHBhZGRpbmcsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHk6IGJvdW5kcy50b3AgKyB3aW5kb3cuc2NyZWVuWSArIHBhZGRpbmcsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHc6IGJvdW5kcy53aWR0aCAtICgyICogcGFkZGluZylcclxuICAgICAgICAgICAgICAgICAgICB9O1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBnYWxsZXJ5LmluaXQoKTtcclxuICAgIH1cclxuXHJcbiAgICBBZGRIYXJib3VyKCk6IHZvaWQge1xyXG4gICAgICAgIGNvbnN0IGhhcmJvdXIgPSBtYXBWaWV3TW9kZWwuQ3JlYXRlSGFyYm91cihgSGFmZW4gJHt0aGlzLkhhcmJvdXJzLmxlbmd0aH1gLCB0aGlzLk1hcC5nZXRDZW50ZXIoKSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJzLnB1c2goaGFyYm91cik7XHJcbiAgICAgICAgaGFyYm91ci5TYXZlVG9TZXJ2ZXIoKTtcclxuICAgIH1cclxuXHJcbiAgICBSZW1vdmVIYXJib3VyID0gKCkgPT4ge1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5TZWxlY3RlZFdheXBvaW50KCkuUmVtb3ZlRnJvbU1hcCgpO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMucmVtb3ZlKHRoaXMuU2VsZWN0ZWRXYXlwb2ludCgpKTtcclxuICAgIH07XHJcbiAgICBSZW1vdmVXYXlwb2ludCA9ICgpID0+IHtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRIYXJib3VyKCkuUmVtb3ZlRnJvbU1hcCgpO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5IYXJib3Vycy5yZW1vdmUodGhpcy5TZWxlY3RlZEhhcmJvdXIoKSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJzLnJlbW92ZSh0aGlzLlNlbGVjdGVkSGFyYm91cigpKTtcclxuICAgIH07XHJcblxyXG4gICAgLy9Db3B5SGFyYm91cihoMTogSGFyYm91ciwgaDI6IEhhcmJvdXIpOiB2b2lkIHtcclxuICAgIC8vICAgIHRoaXMuQ29weVdheXBvaW50KGgxLCBoMik7XHJcbiAgICAvL31cclxuXHJcbiAgICAvL0NvcHlXYXlwb2ludCh3MTogV2F5cG9pbnQsIHcyOiBXYXlwb2ludCkge1xyXG4gICAgLy8gICAgdzIuV2F5cG9pbnROdW1iZXIodzEuV2F5cG9pbnROdW1iZXIoKSk7XHJcbiAgICAvLyAgICB3Mi5MYXRpdHVkZSh3MS5MYXRpdHVkZSgpKTtcclxuICAgIC8vICAgIHcyLkxvbmdpdHVkZSh3MS5Mb25naXR1ZGUoKSk7XHJcbiAgICAvLyAgICB3Mi5OYW1lKHcxLk5hbWUoKSk7XHJcbiAgICAvLyAgICB3Mi5EZXNjcmlwdGlvbih3MS5EZXNjcmlwdGlvbigpKTtcclxuICAgIC8vfVxyXG5cclxuICAgIEFkZFBvbHlsaW5lKHdheXBvaW50PzogV2F5cG9pbnQpOiBMLlBvbHlsaW5lO1xyXG4gICAgQWRkUG9seWxpbmUod2F5cG9pbnRzPzogV2F5cG9pbnRbXSk6IEwuUG9seWxpbmU7XHJcbiAgICBBZGRQb2x5bGluZShhcmc/KTogTC5Qb2x5bGluZSB7XHJcbiAgICAgICAgY29uc3QgcG9seWxpbmUgPSBuZXcgTC5Qb2x5bGluZShbXSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlBvbHlsaW5lcy5wdXNoKHBvbHlsaW5lKTtcclxuICAgICAgICBpZiAobWFwVmlld01vZGVsLk1hcE1vZGUoKSA9PT0gTWFwTW9kZS5BZG1pbilcclxuICAgICAgICAgICAgcG9seWxpbmUuYWRkVG8odGhpcy5NYXApO1xyXG4gICAgICAgIHBvbHlsaW5lLldheXBvaW50cyA9IG5ldyBBcnJheSgpO1xyXG4gICAgICAgIGlmIChhcmcgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgaWYgKGFyZyBpbnN0YW5jZW9mIFdheXBvaW50KVxyXG4gICAgICAgICAgICAgICAgKGFyZyBhcyBXYXlwb2ludCkuQWRkVG9Qb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHdheXBvaW50IG9mIGFyZyBhcyBXYXlwb2ludFtdKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgd2F5cG9pbnQuQWRkVG9Qb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgcG9seWxpbmUuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdXNlb3ZlclwiLFxyXG4gICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSG92ZXJlZFBvbHlpbmUgPSBwb2x5bGluZTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgcmV0dXJuIHBvbHlsaW5lO1xyXG4gICAgfVxyXG5cclxuXHJcbiAgICBNYXBNb2RlID0ga28ub2JzZXJ2YWJsZTxNYXBNb2RlPigpO1xyXG4gICAgRHJhd2luZ0xhdExuZzogTC5MYXRMbmc7XHJcbiAgICBEcmF3aW5nU291cmNlV2F5cG9pbnQ6IFdheXBvaW50O1xyXG4gICAgRHJhd2luZ1RhcmdldFdheXBvaW50OiBXYXlwb2ludDtcclxuICAgIFJlbW92ZVBvbHlsaW5lID0gKHBvbHlsaW5lOiBMLlBvbHlsaW5lKSA9PiB7XHJcbiAgICAgICAgdGhpcy5NYXAucmVtb3ZlTGF5ZXIocG9seWxpbmUpO1xyXG4gICAgICAgIHRoaXMuRHJhd2luZ1BvbHlsaW5lID0gdW5kZWZpbmVkO1xyXG4gICAgfTtcclxuXHJcbiAgICBHZXRNYXBNb2RlKCk6IE1hcE1vZGUge1xyXG4gICAgICAgIGlmICh0aGlzLkRyYXdpbmdQb2x5bGluZSAhPT0gdW5kZWZpbmVkICYmIHRoaXMuRHJhd2luZ0xhdExuZyAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICByZXR1cm4gTWFwTW9kZS5Sb3V0ZURyYXdpbmc7XHJcbiAgICAgICAgcmV0dXJuIHRoaXMuTWFwTW9kZSgpO1xyXG4gICAgfVxyXG5cclxuICAgIEdldFdheVBvaW50QnlJZChpZDogbnVtYmVyKTogV2F5cG9pbnQge1xyXG4gICAgICAgIGZvciAobGV0IHdheXBvaW50IG9mIHRoaXMuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgaWYgKHdheXBvaW50LklkKCkgPT09IGlkKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHdheXBvaW50O1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCB3YXlwb2ludCBvZiB0aGlzLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAgICAgaWYgKHdheXBvaW50LklkKCkgPT09IGlkKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHdheXBvaW50O1xyXG4gICAgICAgIH1cclxuICAgICAgICB0aHJvdyBgTm8gV2F5cG9pbnQgd2l0aCBpZCAke2lkfSBpbiBtb2RlbGA7XHJcbiAgICB9XHJcblxyXG4gICAgQ2FsY3VsYXRlRGlzdGFuY2VzKHN0YXJ0ID0gbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cigpLCB0YXJnZXQ/OiBDbGllbnRNb2RlbC5XYXlwb2ludCkge1xyXG5cclxuICAgICAgICBjb25zdCB3YXlwb2ludHM6IEFycmF5PFdheXBvaW50PiA9IFtzdGFydF07XHJcbiAgICAgICAgY29uc3QgY2FsY3VsYXRpbmcgPSBuZXcgQXJyYXk8V2F5cG9pbnREaXN0YW5jZT4oKTtcclxuICAgICAgICBjb25zdCBjYWxjdWxhdGVkID0gbmV3IEFycmF5PFdheXBvaW50RGlzdGFuY2U+KCk7XHJcbiAgICAgICAgY29uc3QgY2FsY3VsYXRlUm91dGUgPSB0YXJnZXQgIT09IHVuZGVmaW5lZDtcclxuICAgICAgICBjYWxjdWxhdGluZy5wdXNoKG5ldyBXYXlwb2ludERpc3RhbmNlKHVuZGVmaW5lZCwgc3RhcnQsIDAsIHdheXBvaW50cywgY2FsY3VsYXRlUm91dGUpKTtcclxuICAgICAgICAvL2ZvciAobGV0IHdheXBvaW50IG9mIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMoKSkge1xyXG4gICAgICAgIC8vICAgIHdheXBvaW50cy5wdXNoKG5ldyBXYXlwb2ludERpc3RhbmNlKG51bGwsIHdheXBvaW50LCBOdW1iZXIuUE9TSVRJVkVfSU5GSU5JVFkpKTtcclxuICAgICAgICAvL31cclxuICAgICAgICAvL2ZvciAobGV0IGhhcmJvdXIgb2YgbWFwVmlld01vZGVsLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAvLyAgICBpZiAoaGFyYm91ciAhPT0gc3RhcnQpIHtcclxuICAgICAgICAvLyAgICAgICAgd2F5cG9pbnRzLnB1c2gobmV3IFdheXBvaW50RGlzdGFuY2UobnVsbCwgaGFyYm91ciwgTnVtYmVyLlBPU0lUSVZFX0lORklOSVRZKSk7XHJcbiAgICAgICAgLy8gICAgfVxyXG4gICAgICAgIC8vfVxyXG4gICAgICAgIGlmIChjYWxjdWxhdGVSb3V0ZSkge1xyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgICAgIHdwLlJvdXRlUHJlY2Vzc29yKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgaCBvZiBtYXBWaWV3TW9kZWwuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICAgICAgaC5Sb3V0ZVByZWNlc3Nvcih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgbWFwVmlld01vZGVsLldheXBvaW50cygpKSB7XHJcbiAgICAgICAgICAgICAgICB3cC5QcmVjZXNzb3IodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBoIG9mIG1hcFZpZXdNb2RlbC5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgICAgICBoLlByZWNlc3Nvcih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgICAgIHdoaWxlIChjYWxjdWxhdGluZy5sZW5ndGggPiAwKSB7XHJcbiAgICAgICAgICAgIGxldCBtaW5pbWFsRGlzdCA9IE51bWJlci5QT1NJVElWRV9JTkZJTklUWTtcclxuICAgICAgICAgICAgbGV0IG1pbmltYWxXUDogV2F5cG9pbnREaXN0YW5jZTtcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgY2FsY3VsYXRpbmcpIHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGNXUCBvZiB3cC5Db25uZWN0ZWRXYXlQb2ludHMpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoKGNhbGN1bGF0ZVJvdXRlID8gY1dQLlJvdXRlUHJlY2Vzc29yKCkgOiBjV1AuUHJlY2Vzc29yKCkpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheSh3cC5Db25uZWN0ZWRXYXlQb2ludHMsIGNXUCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICBpZiAod3AuQ29ubmVjdGVkV2F5UG9pbnRzLmxlbmd0aCA9PT0gMCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheShjYWxjdWxhdGluZywgd3ApO1xyXG4gICAgICAgICAgICAgICAgICAgIGNhbGN1bGF0ZWQucHVzaCh3cCk7XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IGRpc3QgPSB3cC5EaXN0YW5jZSArIHdwLkNvbm5lY3RlZFdheVBvaW50c1swXS5MYXRMbmcuZGlzdGFuY2VUbyh3cC5MYXRMbmcpO1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChkaXN0IDwgbWluaW1hbERpc3QpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWluaW1hbERpc3QgPSBkaXN0O1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtaW5pbWFsV1AgPSB3cDtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKG1pbmltYWxXUCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBjYWxjdWxhdGluZy5wdXNoKG5ldyBXYXlwb2ludERpc3RhbmNlKG1pbmltYWxXUC5XYXlwb2ludCxcclxuICAgICAgICAgICAgICAgICAgICBtaW5pbWFsV1AuQ29ubmVjdGVkV2F5UG9pbnRzLnNoaWZ0KCksXHJcbiAgICAgICAgICAgICAgICAgICAgbWluaW1hbERpc3QsXHJcbiAgICAgICAgICAgICAgICAgICAgd2F5cG9pbnRzLFxyXG4gICAgICAgICAgICAgICAgICAgIGNhbGN1bGF0ZVJvdXRlKSk7XHJcbiAgICAgICAgICAgICAgICAvL2lmIChtaW5pbWFsV1AuV2F5cG9pbnQgPT09IHRhcmdldClcclxuICAgICAgICAgICAgICAgIC8vICAgIGJyZWFrO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGlmIChjYWxjdWxhdGVSb3V0ZSlcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgY2FsY3VsYXRlZCkge1xyXG4gICAgICAgICAgICAgICAgd3AuV2F5cG9pbnQuUm91dGVEaXN0YW5jZShNYXRoLnJvdW5kKHdwLkRpc3RhbmNlIC8gMTAwKSAvIDEwKTtcclxuICAgICAgICAgICAgICAgIC8vd3AuV2F5cG9pbnQuUHJlY2Vzc29yKHdwLlByZWNlc3Nvcik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgIGZvciAobGV0IHdwIG9mIGNhbGN1bGF0ZWQpIHtcclxuICAgICAgICAgICAgICAgIHdwLldheXBvaW50LkRpc3RhbmNlKE1hdGgucm91bmQod3AuRGlzdGFuY2UgLyAxMDApIC8gMTApO1xyXG4gICAgICAgICAgICAgICAgLy93cC5XYXlwb2ludC5QcmVjZXNzb3Iod3AuUHJlY2Vzc29yKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIHByaXZhdGUgaGlnaGxpZ2h0ZWRSb3V0ZTogTC5Qb2x5bGluZTtcclxuICAgIHByaXZhdGUgcm91dGVGaXhlZCA9IGZhbHNlO1xyXG4gICAgcHJpdmF0ZSBwcmV2aW91c0JvdW5kczogTC5MYXRMbmdCb3VuZHM7XHJcbiAgICBwcml2YXRlIG5vUmV2ZXJ0VG9QcmV2aW91c0JvdW5kcyA9IGZhbHNlO1xyXG5cclxuICAgIFNob3dSb3V0ZShoOiBDbGllbnRNb2RlbC5XYXlwb2ludCkge1xyXG4gICAgICAgIGlmIChtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkID0gZmFsc2U7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5IaWRlUm91dGUoKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgaWYgKGggPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgaCA9ICh0aGlzIGFzIGFueSk7XHJcbiAgICAgICAgaWYgKCEoaCBpbnN0YW5jZW9mIENsaWVudE1vZGVsLkhhcmJvdXIpKVxyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgY29uc3QgbGF0TG5ncyA9IFtoLkxhdExuZ107XHJcbiAgICAgICAgbGV0IGRpc3QgPSBoLkRpc3RhbmNlKCk7XHJcbiAgICAgICAgaWYgKGRpc3QgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgZGlzdCA9IDA7XHJcbiAgICAgICAgd2hpbGUgKGguUHJlY2Vzc29yKCkgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICBoID0gaC5QcmVjZXNzb3IoKTtcclxuICAgICAgICAgICAgbGF0TG5ncy5wdXNoKGguTGF0TG5nKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgbWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUgPSBMLnBvbHlsaW5lKGxhdExuZ3MpO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlLmFkZFRvKG1hcFZpZXdNb2RlbC5NYXApO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlLmJpbmRMYWJlbChkaXN0LnRvU3RyaW5nKCkgKyBcIiBrbVwiLCB7IG5vSGlkZTogdHJ1ZSB9KTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuRml0Qm91bmRzKG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlLmdldEJvdW5kcygpKTtcclxuICAgIH1cclxuXHJcbiAgICBGaXRCb3VuZHMoYm91bmRzOiBMLkxhdExuZ0JvdW5kcykge1xyXG4gICAgICAgIGNvbnN0IG1hcCA9IG1hcFZpZXdNb2RlbC5NYXA7XHJcbiAgICAgICAgY29uc3QgY3VycmVudEJvdW5kcyA9IG1hcC5nZXRCb3VuZHMoKTtcclxuICAgICAgICBpZiAoIWN1cnJlbnRCb3VuZHMuY29udGFpbnMoYm91bmRzKSkge1xyXG4gICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPSBjdXJyZW50Qm91bmRzO1xyXG4gICAgICAgICAgICBtYXAuZml0Qm91bmRzKGJvdW5kcyk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIEhpZGVSb3V0ZShmb3JjZSA9IGZhbHNlKSB7XHJcbiAgICAgICAgaWYgKCghbWFwVmlld01vZGVsLnJvdXRlRml4ZWQgfHwgZm9yY2UpICYmIG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlRml4ZWQgPSBmYWxzZTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZSk7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICBpZiAoIW1hcFZpZXdNb2RlbC5ub1JldmVydFRvUHJldmlvdXNCb3VuZHMgJiYgbWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGNvbnN0IHRtcEJvdW5kcyA9IG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcztcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgIHdpbmRvdy5zZXRUaW1lb3V0KCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5NYXAuZml0Qm91bmRzKHRtcEJvdW5kcyk7XHJcbiAgICAgICAgICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPSB0bXBCb3VuZHM7XHJcbiAgICAgICAgICAgICAgICB9LCAxMDApO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIEZpeFJvdXRlKCkge1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkID0gdHJ1ZTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPSB1bmRlZmluZWQ7XHJcbiAgICB9XHJcblxyXG4gICAgQ3JlYXRlV2F5cG9pbnQobGF0TG5nOiBMLkxhdExuZywgbWFya2VyVHlwZTogTWFya2VyVHlwZSk6IFdheXBvaW50IHtcclxuICAgICAgICBjb25zdCB3cCA9IG5ldyBXYXlwb2ludChsYXRMbmcsIG1hcmtlclR5cGUsIG1hcFZpZXdNb2RlbC5NYXAgYXMgTC5tYXBib3guTWFwKTtcclxuICAgICAgICB0aGlzLkluaXRpYWxpemVXYXlwb2ludCh3cCwgbWFya2VyVHlwZSk7XHJcbiAgICAgICAgcmV0dXJuIHdwO1xyXG4gICAgfVxyXG5cclxuICAgIEluaXRpYWxpemVXYXlwb2ludCh3cDogV2F5cG9pbnQsIG1hcmtlclR5cGU6IE1hcmtlclR5cGUpIHtcclxuICAgICAgICB0aGlzLkNyZWF0ZU1hcmtlcihtYXJrZXJUeXBlLCB3cCk7XHJcbiAgICB9XHJcblxyXG4gICAgQ3JlYXRlTWFya2VyKG1hcmtlclR5cGU6IE1hcmtlclR5cGUsIHdwOiBDbGllbnRNb2RlbC5XYXlwb2ludCkge1xyXG4gICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluIHx8IG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuSGFyYm91cikge1xyXG4gICAgICAgICAgICBjb25zdCBvcHRpb25zOiBMLk1hcmtlck9wdGlvbnMgPSB7XHJcbiAgICAgICAgICAgICAgICBkcmFnZ2FibGU6IChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKVxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgICAgICBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSkge1xyXG4gICAgICAgICAgICAgICAgb3B0aW9ucy5vcGFjaXR5ID0gMDtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLk1hcE1vZGUoKSA9PT0gTWFwTW9kZS5BZG1pbiAmJlxyXG4gICAgICAgICAgICAgICAgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuV2F5cG9pbnQgfHwgbWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSkpIHtcclxuICAgICAgICAgICAgICAgIG9wdGlvbnMuaWNvbiA9IG5ldyBMLkljb24oe1xyXG4gICAgICAgICAgICAgICAgICAgIGljb25Vcmw6IFwiL2ltYWdlcy93YXlwb2ludGhhbmRsZS5wbmdcIixcclxuICAgICAgICAgICAgICAgICAgICBpY29uU2l6ZTogbmV3IEwuUG9pbnQoMTAsIDEwLCB0cnVlKSxcclxuICAgICAgICAgICAgICAgICAgICBjbGFzc05hbWU6IFwid2F5cG9pbnRcIlxyXG4gICAgICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKSB7XHJcbiAgICAgICAgICAgICAgICBvcHRpb25zLmNvbnRleHRtZW51ID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIG9wdGlvbnMuY29udGV4dG1lbnVJbmhlcml0SXRlbXMgPSBmYWxzZTtcclxuICAgICAgICAgICAgICAgIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkhhcmJvdXIpIHtcclxuICAgICAgICAgICAgICAgICAgICBvcHRpb25zLmNvbnRleHRtZW51SXRlbXMgPSBbXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRleHQ6IFwiQmVhcmJlaXRlblwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY29udGV4dDogd3AsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjYWxsYmFjazogZnVuY3Rpb24gKCkgeyBtYXBWaWV3TW9kZWwuRWRpdGluZ0hhcmJvdXIodGhpcykgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9LFxyXG4gICAgICAgICAgICAgICAgICAgICAgICB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB0ZXh0OiBcIkzDtnNjaGVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0OiB3cCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrOiBmdW5jdGlvbiAoKSB7IG1hcFZpZXdNb2RlbC5EZWxldGluZ0hhcmJvdXIodGhpcykgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgXTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgb3B0aW9ucy5jb250ZXh0bWVudUl0ZW1zID0gW1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB0ZXh0OiBcIkJlYXJiZWl0ZW5cIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnRleHQ6IHdwLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2s6IGZ1bmN0aW9uICgpIHsgbWFwVmlld01vZGVsLkVkaXRpbmdXYXlwb2ludCh0aGlzKSB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0sXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRleHQ6IFwiTMO2c2NoZW5cIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnRleHQ6IHdwLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2s6IGZ1bmN0aW9uICgpIHsgbWFwVmlld01vZGVsLkRlbGV0aW5nV2F5cG9pbnQodGhpcykgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgXTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgY29uc3QgbWFya2VyID0gbmV3IEwuTWFya2VyKHdwLkxhdExuZywgb3B0aW9ucyk7XHJcbiAgICAgICAgICAgIG1hcmtlci5hZGRUbyh0aGlzLk1hcCk7XHJcbiAgICAgICAgICAgIG1hcmtlci5XYXlwb2ludCA9IHdwO1xyXG4gICAgICAgICAgICB3cC5tYXJrZXIgPSBtYXJrZXI7XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSlcclxuICAgICAgICAgICAgICAgICAgICBtYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdXNlb3V0XCIsIChlKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmIChlLnRhcmdldC5XYXlwb2ludC5Jc0R1bW15KCkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgIH0pXHJcbiAgICAgICAgICAgICAgICBtYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcImRyYWdcIixcclxuICAgICAgICAgICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB3cC5TZXRMYXRMbmcod3AubWFya2VyLmdldExhdExuZygpKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLldheXBvaW50IHx8IG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuRHVtbXkpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLldheXBvaW50TWFya2Vycy5wdXNoKHdwLm1hcmtlcik7XHJcbiAgICAgICAgICAgICAgICAgICAgd3AubWFya2VyLlBvaW50ID0gbWFwVmlld01vZGVsLk1hcC5sYXRMbmdUb0NvbnRhaW5lclBvaW50KHdwLkxhdExuZyk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcImNsaWNrXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKHdwLklzRHVtbXkoKSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLldheXBvaW50cy5wdXNoKHdwKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHdwLmNvbnZlcnRGcm9tRHVtbXlIYW5kbGUoKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLkdldE1hcE1vZGUoKSA9PT0gTWFwTW9kZS5Sb3V0ZURyYXdpbmcpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGlmICghd3AuSXNJblBvbHlsaW5lKG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUpKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgLkNvbm5lY3Qod3AuSWQoKSwgbWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZS5XYXlwb2ludHNbMF0uSWQoKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgd3AuQWRkVG9Qb2x5bGluZShtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICByZW1vdmVGcm9tUG9seWxpbmUobWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSwgbWFwVmlld01vZGVsLkRyYXdpbmdMYXRMbmcpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIGFkZER1bW15SGFuZGxlKG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdMYXRMbmcgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIHJlbW92ZVBvbHlsaW5lKG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdMYXRMbmcgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIHdwLm1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwiZGJsY2xpY2tcIixcclxuICAgICAgICAgICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lID0gbWFwVmlld01vZGVsLkFkZFBvbHlsaW5lKHdwKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdMYXRMbmcgPSBuZXcgTC5MYXRMbmcoZS5sYXRsbmcubGF0LCBlLmxhdGxuZy5sbmcpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lLmFkZExhdExuZyhtYXBWaWV3TW9kZWwuRHJhd2luZ0xhdExuZyk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSlcclxuICAgICAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkT25lVGltZUV2ZW50TGlzdGVuZXIoXCJkcmFnXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIChlOiBMLkxlYWZsZXRNb3VzZUV2ZW50KSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB3cC5jb252ZXJ0RnJvbUR1bW15SGFuZGxlKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzLnB1c2god3ApO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIC8vZWxzZSBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5XYXlwb2ludCkge1xyXG4gICAgICAgICAgICAgICAgLy8gICAgd3AuTmFtZShgV2VncHVua3QgJHttYXBWaWV3TW9kZWwuV2F5cG9pbnRzKCkubGVuZ3RoICsgMX1gKTtcclxuICAgICAgICAgICAgICAgIC8vfVxyXG4gICAgICAgICAgICAgICAgd3AubWFya2VyLmFkZEV2ZW50TGlzdGVuZXIoXCJkcmFnZW5kXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgd3AuU2F2ZVRvU2VydmVyKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0gZWxzZSBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5IYXJib3VyKSB7XHJcbiAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdXNlb3ZlclwiLFxyXG4gICAgICAgICAgICAgICAgICAgICgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLlNob3dSb3V0ZSh3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcImNsaWNrXCIsICgpID0+IG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIod3AgYXMgQ2xpZW50TW9kZWwuSGFyYm91cikpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIENyZWF0ZUhhcmJvdXIobmFtZTogc3RyaW5nLCBsYXRMbmc6IEwuTGF0TG5nKSB7XHJcbiAgICAgICAgY29uc3QgaCA9IG5ldyBIYXJib3VyKG5hbWUsIGxhdExuZywgdGhpcy5NYXAgYXMgTC5tYXBib3guTWFwKTtcclxuICAgICAgICB0aGlzLkluaXRpYWxpemVXYXlwb2ludChoLCBNYXJrZXJUeXBlLkhhcmJvdXIpO1xyXG4gICAgICAgIHJldHVybiBoO1xyXG4gICAgfVxyXG5cclxuICAgIFNhdmVIYXJib3VyKCkge1xyXG4gICAgICAgIGNvbnN0IGhhcmJvdXI6IENsaWVudE1vZGVsLkhhcmJvdXIgPSB0aGlzIGFzIGFueTtcclxuICAgICAgICBpZiAoaGFyYm91ci5JZCgpID09PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJzLnB1c2goaGFyYm91cik7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGhhcmJvdXIuU2F2ZVRvU2VydmVyKClcclxuICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkVkaXRpbmdIYXJib3VyKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIERlbGV0ZUhhcmJvdXIoKSB7XHJcbiAgICAgICAgdmFyIGggPSBtYXBWaWV3TW9kZWwuRGVsZXRpbmdIYXJib3VyKCk7XHJcbiAgICAgICAgU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkRpc2Nvbm5lY3QoaC5JZCgpKVxyXG4gICAgICAgICAgICAuZG9uZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBoLkRlbGV0ZU9uU2VydmVyKClcclxuICAgICAgICAgICAgICAgICAgICAuZG9uZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGguUmVtb3ZlRnJvbU1hcCgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSGFyYm91cnMucmVtb3ZlKGgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRGVsZXRpbmdIYXJib3VyKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIFNhdmVXYXlwb2ludCgpIHtcclxuICAgICAgICBjb25zdCB3YXlwb2ludDogQ2xpZW50TW9kZWwuV2F5cG9pbnQgPSB0aGlzIGFzIGFueTtcclxuICAgICAgICB3YXlwb2ludC5TYXZlVG9TZXJ2ZXIoKVxyXG4gICAgICAgICAgICAuZG9uZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRWRpdGluZ1dheXBvaW50KHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIERlbGV0ZVdheXBvaW50KCkge1xyXG4gICAgICAgIHZhciB3cCA9IG1hcFZpZXdNb2RlbC5EZWxldGluZ1dheXBvaW50KCk7XHJcbiAgICAgICAgU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkRpc2Nvbm5lY3Qod3AuSWQoKSlcclxuICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgd3AuRGVsZXRlT25TZXJ2ZXIoKVxyXG4gICAgICAgICAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgd3AuUmVtb3ZlRnJvbU1hcCgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzLnJlbW92ZSh3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EZWxldGluZ1dheXBvaW50KHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgfTtcclxuXHJcblxyXG4gICAgU2F2ZUpvYigpIHtcclxuICAgICAgICBjb25zdCBqb2I6IENsaWVudE1vZGVsLkpvYiA9IHRoaXMgYXMgYW55O1xyXG4gICAgICAgIGNvbnN0IG5ld0pvYiA9IGpvYi5JZCgpID09PSB1bmRlZmluZWQ7XHJcbiAgICAgICAgam9iLlNhdmVUb1NlcnZlcigpXHJcbiAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmIChuZXdKb2IpIHtcclxuICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSm9icy5wdXNoKG1hcFZpZXdNb2RlbC5FZGl0aW5nSm9iKCkpO1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuRWRpdGluZ0pvYigpLlN1cGVySm9iSWQoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuR2V0Sm9iQnlJZChtYXBWaWV3TW9kZWwuRWRpdGluZ0pvYigpLlN1cGVySm9iSWQoKSkuU3ViSm9icy5wdXNoKG1hcFZpZXdNb2RlbC5FZGl0aW5nSm9iKCkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkVkaXRpbmdKb2IodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgRGVsZXRlSm9iKCkge1xyXG4gICAgICAgIGNvbnN0IGpvYiA9IG1hcFZpZXdNb2RlbC5EZWxldGluZ0pvYigpO1xyXG4gICAgICAgIGpvYi5EZWxldGVPblNlcnZlcigpXHJcbiAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Kb2JzLnJlbW92ZShqb2IpO1xyXG4gICAgICAgICAgICAgICAgaWYgKGpvYi5TdXBlckpvYklkKCkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuR2V0Sm9iQnlJZChqb2IuU3VwZXJKb2JJZCgpKS5TdWJKb2JzLnJlbW92ZShqb2IpO1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRlbGV0aW5nSm9iKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgfVxyXG5cclxuXHJcbiAgICBEcmF3aW5nUG9seWxpbmU6IEwuUG9seWxpbmU7XHJcbiAgICBQb2x5bGluZXMgPSBuZXcgQXJyYXk8TC5Qb2x5bGluZT4oKTtcclxuICAgIEVkaXRpbmdIYXJib3VyID0ga28ub2JzZXJ2YWJsZTxIYXJib3VyPigpO1xyXG4gICAgRGVsZXRpbmdIYXJib3VyID0ga28ub2JzZXJ2YWJsZTxIYXJib3VyPigpO1xyXG4gICAgRWRpdGluZ1dheXBvaW50ID0ga28ub2JzZXJ2YWJsZTxXYXlwb2ludD4oKTtcclxuICAgIERlbGV0aW5nV2F5cG9pbnQgPSBrby5vYnNlcnZhYmxlPFdheXBvaW50PigpO1xyXG4gICAgRGVsZXRpbmdKb2IgPSBrby5vYnNlcnZhYmxlPEpvYj4oKTtcclxuICAgIEVkaXRpbmdKb2IgPSBrby5vYnNlcnZhYmxlPEpvYj4oKTtcclxuICAgIFdheXBvaW50TWFya2VycyA9IG5ldyBBcnJheSgpO1xyXG4gICAgSG92ZXJlZFBvbHlpbmU6IEwuUG9seWxpbmU7XHJcblxyXG59XHJcblxyXG52YXIgbWFwVmlld01vZGVsID0gbmV3IE1hcFZpZXdNb2RlbChNYXBNb2RlLlZpZXcpO1xyXG5rby5hcHBseUJpbmRpbmdzKG1hcFZpZXdNb2RlbCk7XHJcbnZhciBkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnID0gZmFsc2U7XHJcblxyXG52YXIgZHJvcHpvbmVNb2RhbCA9ICQoXCIjZHJvcHpvbmVNb2RhbFwiKTtcclxudmFyIGVkaXRpbmdIYXJib3VyTW9kYWwgPSAkKFwiI2VkaXRpbmdIYXJib3VyTW9kYWxcIik7XHJcbnZhciBkZWxldGluZ0hhcmJvdXJNb2RhbCA9ICQoXCIjZGVsZXRpbmdIYXJib3VyTW9kYWxcIik7XHJcbnZhciBlZGl0aW5nV2F5cG9pbnRNb2RhbCA9ICQoXCIjZWRpdGluZ1dheXBvaW50TW9kYWxcIik7XHJcbnZhciBkZWxldGluZ1dheXBvaW50TW9kYWwgPSAkKFwiI2RlbGV0aW5nV2F5cG9pbnRNb2RhbFwiKTtcclxudmFyIGRlbGV0aW5nSm9iTW9kYWwgPSAkKFwiI2RlbGV0aW5nSm9iTW9kYWxcIik7XHJcbnZhciBlZGl0aW5nSm9iTW9kYWwgPSAkKFwiI2VkaXRpbmdKb2JNb2RhbFwiKTtcclxudmFyIGpvYk92ZXJ2aWV3TW9kYWwgPSAkKFwiI2pvYk92ZXJ2aWV3TW9kYWxcIik7XHJcbnZhciBkcm9wem9uZTogRHJvcHpvbmU7XHJcbnZhciBoYXNEcmFnID0gZmFsc2U7XHJcbnZhciB1cGxvYWRNb2RhbFZpc2libGUgPSBmYWxzZTtcclxudmFyIHBzd3AgPSAkKFwiLnBzd3BcIilbMF07XHJcblxyXG52YXIgbGVmdFNpZGViYXIgPSBuZXcgU2lkZWJhcigkKFwiI2xlZnRTaWRlYmFyXCIpKTtcclxudmFyIHJpZ2h0U2lkZWJhciA9IG5ldyBTaWRlYmFyKCQoXCIjcmlnaHRTaWRlYmFyXCIpKTtcclxudmFyIGJvdHRvbVNpZGViYXIgPSBuZXcgU2lkZWJhcigkKFwiI2JvdHRvbVNpZGViYXJcIikpO1xyXG52YXIgaGFyYm91ckluZm8gPSAkKFwiI2hhcmJvdXJJbmZvXCIpO1xyXG5Ecm9wem9uZS5vcHRpb25zLmRyb3B6b25lID1cclxuICAgIHtcclxuICAgICAgICBhY2NlcHRlZEZpbGVzOiBcImltYWdlL2pwZWcsaW1hZ2UvcG5nXCIsXHJcbiAgICAgICAgZGljdEludmFsaWRGaWxlVHlwZTogXCJEaWVzZXIgRGF0ZWl0eXAgd2lyZCBuaWNodCB1bnRlcnN0w7x0enRcIixcclxuICAgICAgICBkaWN0RGVmYXVsdE1lc3NhZ2U6IFwiRGF0ZWllbiBoaWVyIGFibGVnZW5cIixcclxuICAgICAgICBpbml0KCkge1xyXG4gICAgICAgICAgICBkcm9wem9uZSA9IHRoaXM7XHJcbiAgICAgICAgICAgIGRyb3B6b25lLm9uKFwic3VjY2Vzc1wiLFxyXG4gICAgICAgICAgICAgICAgKGUsIGRhdGE6IFNlcnZlck1vZGVsLkltYWdlKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgdmFyIGltYWdlID0gbmV3IENsaWVudE1vZGVsLkltYWdlKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoZGF0YSk7XHJcbiAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkltYWdlcy5wdXNoKGltYWdlKTtcclxuICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuR2V0QWxidW1CeUlkKGltYWdlLlBhcmVudEFsYnVtSWQoKSkuSW1hZ2VzLnB1c2goaW1hZ2UpO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIGRyb3B6b25lLm9uKFwicXVldWVjb21wbGV0ZVwiLFxyXG4gICAgICAgICAgICAgICAgKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBkcm9wem9uZU1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICBkcm9wem9uZS5vbihcImRyYWdvdmVyXCIsXHJcbiAgICAgICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgaGFzRHJhZyA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5cclxuZG9jdW1lbnQub25kcmFnZW50ZXIgPVxyXG4gICAgKGU6IERyYWdFdmVudCkgPT4ge1xyXG4gICAgICAgIGlmICghdXBsb2FkTW9kYWxWaXNpYmxlICYmXHJcbiAgICAgICAgICAgICFoYXNEcmFnICYmXHJcbiAgICAgICAgICAgICFkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnICYmXHJcbiAgICAgICAgICAgIGRyb3B6b25lTW9kYWwuaXMoXCI6bm90KC5pbilcIikgJiZcclxuICAgICAgICAgICAgZS5kYXRhVHJhbnNmZXIudHlwZXNbMF0gPT09IFwiRmlsZXNcIiAmJlxyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRIYXJib3VyKCkgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICBkcm9wem9uZU1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgdXBsb2FkTW9kYWxWaXNpYmxlID0gdHJ1ZTtcclxuICAgICAgICAgICAgZHJvcHpvbmVNb2RhbE9wZW5lZEJ5RHJhZyA9IHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBoYXNEcmFnID0gdHJ1ZTtcclxuICAgICAgICBlLnByZXZlbnREZWZhdWx0KCk7XHJcbiAgICAgICAgZS5zdG9wUHJvcGFnYXRpb24oKTtcclxuICAgIH07XHJcbmRvY3VtZW50Lm9uZHJhZ292ZXIgPVxyXG4gICAgKGU6IERyYWdFdmVudCkgPT4ge1xyXG4gICAgICAgIGhhc0RyYWcgPSB0cnVlO1xyXG4gICAgfTtcclxuZG9jdW1lbnQub25kcmFnbGVhdmUgPVxyXG4gICAgKGU6IERyYWdFdmVudCkgPT4ge1xyXG4gICAgICAgIGlmICh1cGxvYWRNb2RhbFZpc2libGUgJiYgaGFzRHJhZyAmJiBkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnICYmIGRyb3B6b25lLmdldFF1ZXVlZEZpbGVzKCkubGVuZ3RoID09PSAwIHx8XHJcbiAgICAgICAgICAgIGRyb3B6b25lLmdldFVwbG9hZGluZ0ZpbGVzKCkubGVuZ3RoID09PSAwKSB7XHJcbiAgICAgICAgICAgIGhhc0RyYWcgPSBmYWxzZTtcclxuICAgICAgICAgICAgd2luZG93LnNldFRpbWVvdXQoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKCFoYXNEcmFnKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZHJvcHpvbmVNb2RhbC5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgICAgICAgICAgdXBsb2FkTW9kYWxWaXNpYmxlID0gZmFsc2U7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0sXHJcbiAgICAgICAgICAgICAgICAxMDAwKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGUucHJldmVudERlZmF1bHQoKTtcclxuICAgICAgICBlLnN0b3BQcm9wYWdhdGlvbigpO1xyXG4gICAgfTtcclxuZHJvcHpvbmVNb2RhbC5vbihcImhpZGUuYnMubW9kYWxcIixcclxuICAgIGUgPT4ge1xyXG4gICAgICAgIGlmIChkcm9wem9uZS5nZXRRdWV1ZWRGaWxlcygpLmxlbmd0aCA+IDAgfHwgZHJvcHpvbmUuZ2V0VXBsb2FkaW5nRmlsZXMoKS5sZW5ndGggPiAwKSB7XHJcbiAgICAgICAgICAgIGUucHJldmVudERlZmF1bHQoKTtcclxuICAgICAgICAgICAgZS5zdG9wSW1tZWRpYXRlUHJvcGFnYXRpb24oKTtcclxuICAgICAgICAgICAgYWxlcnQoXCJEYXMgRmVuc3RlciBrYW5uIG5pY2h0IGdlc2NobG9zc2VuIHdlcmRlbiwgd8OkaHJlbmQgRGF0ZWllbiBob2NoZ2VsYWRlbiB3ZXJkZW4uXCIpO1xyXG4gICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgZHJvcHpvbmUucmVtb3ZlQWxsRmlsZXMoKTtcclxuICAgICAgICAgICAgZHJvcHpvbmVNb2RhbE9wZW5lZEJ5RHJhZyA9IGZhbHNlO1xyXG4gICAgICAgIH1cclxuICAgIH0pO1xyXG52YXIgZ2FsbGVyeTogUGhvdG9Td2lwZTxQaG90b1N3aXBlLk9wdGlvbnM+O1xyXG5cclxuJChcIi5tb2RhbFwiKS5vbihcImhpZGRlbi5icy5tb2RhbFwiLCBmdW5jdGlvbiAoZXZlbnQpIHtcclxuICAgICQodGhpcykucmVtb3ZlQ2xhc3MoXCJmdi1tb2RhbC1zdGFja1wiKTtcclxuICAgICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiLCAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikgLSAxKTtcclxufSk7XHJcblxyXG5cclxuJChcIi5tb2RhbFwiKS5vbihcInNob3duLmJzLm1vZGFsXCIsIGZ1bmN0aW9uIChldmVudCkge1xyXG5cclxuICAgIC8vIGtlZXAgdHJhY2sgb2YgdGhlIG51bWJlciBvZiBvcGVuIG1vZGFsc1xyXG5cclxuICAgIGlmICh0eXBlb2YgKCQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiKSkgPT0gXCJ1bmRlZmluZWRcIikge1xyXG4gICAgICAgICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiLCAwKTtcclxuICAgIH1cclxuXHJcblxyXG4gICAgLy8gaWYgdGhlIHotaW5kZXggb2YgdGhpcyBtb2RhbCBoYXMgYmVlbiBzZXQsIGlnbm9yZS5cclxuXHJcbiAgICBpZiAoJCh0aGlzKS5oYXNDbGFzcyhcImZ2LW1vZGFsLXN0YWNrXCIpKSB7XHJcbiAgICAgICAgcmV0dXJuO1xyXG4gICAgfVxyXG5cclxuICAgICQodGhpcykuYWRkQ2xhc3MoXCJmdi1tb2RhbC1zdGFja1wiKTtcclxuXHJcbiAgICAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIiwgJChcImJvZHlcIikuZGF0YShcImZ2X29wZW5fbW9kYWxzXCIpICsgMSk7XHJcblxyXG4gICAgJCh0aGlzKS5jc3MoXCJ6LWluZGV4XCIsIDEwNDAgKyAoMTAgKiAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikpKTtcclxuXHJcbiAgICAkKFwiLm1vZGFsLWJhY2tkcm9wXCIpLm5vdChcIi5mdi1tb2RhbC1zdGFja1wiKVxyXG4gICAgICAgIC5jc3MoXCJ6LWluZGV4XCIsIDEwMzkgKyAoMTAgKiAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikpKTtcclxuXHJcblxyXG4gICAgJChcIi5tb2RhbC1iYWNrZHJvcFwiKS5ub3QoXCJmdi1tb2RhbC1zdGFja1wiKVxyXG4gICAgICAgIC5hZGRDbGFzcyhcImZ2LW1vZGFsLXN0YWNrXCIpO1xyXG5cclxufSk7XHJcblxyXG5cclxuIl0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
