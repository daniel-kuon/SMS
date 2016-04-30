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
        this.AlbumImagesLoaded = false;
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
        ServerApi.AlbumImageApi.GetDefault().Get().done(function (d) {
            for (var _i = 0, d_9 = d; _i < d_9.length; _i++) {
                var ai = d_9[_i];
                _this.AlbumImages.push(ai);
            }
            _this.AlbumImagesLoaded = true;
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
            for (var _i = 0, d_10 = d; _i < d_10.length; _i++) {
                var sEntity = d_10[_i];
                _this.Tacks.push(new ClientModel.Tack().LoadFromServerEntity(sEntity));
            }
            _this.TacksLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.LocationApi.GetDefault()
            .Get()
            .done(function (d) {
            for (var _i = 0, d_11 = d; _i < d_11.length; _i++) {
                var sEntity = d_11[_i];
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
            this.LocationsLoaded &&
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

//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIlZpZXdNb2RlbHMvSG9tZS9NYXAudHMiXSwibmFtZXMiOltdLCJtYXBwaW5ncyI6IkFBQUEsSUFBTyxRQUFRLEdBQUcsV0FBVyxDQUFDLE9BQU8sQ0FBQztBQUV0QyxJQUFPLFFBQVEsR0FBRyxXQUFXLENBQUMsUUFBUSxDQUFDO0FBQ3ZDLElBQU8sT0FBTyxHQUFHLFdBQVcsQ0FBQyxPQUFPLENBQUM7QUFDckMsSUFBTyxHQUFHLEdBQUcsV0FBVyxDQUFDLEdBQUcsQ0FBQztBQUM3QixJQUFPLGdCQUFnQixHQUFHLFdBQVcsQ0FBQyxnQkFBZ0IsQ0FBQztBQUV2RCxtQkFBbUIsR0FBZTtJQUM5QixJQUFNLEtBQUssR0FBRyxHQUFHLENBQUMsVUFBVSxFQUFFLENBQUMsQ0FBQyxDQUFDLENBQUM7SUFDbEMsSUFBTSxHQUFHLEdBQUcsR0FBRyxDQUFDLFVBQVUsRUFBRSxDQUFDLENBQUMsQ0FBQyxDQUFDO0lBQ2hDLE1BQU0sQ0FBQyxJQUFJLENBQUMsQ0FBQyxNQUFNLENBQUMsS0FBSyxDQUFDLEdBQUcsR0FBRyxDQUFDLENBQUMsR0FBRyxDQUFDLEdBQUcsR0FBRyxLQUFLLENBQUMsR0FBRyxDQUFDLEdBQUcsQ0FBQyxDQUFDLEVBQUUsS0FBSyxDQUFDLEdBQUcsR0FBRyxDQUFDLENBQUMsR0FBRyxDQUFDLEdBQUcsR0FBRyxLQUFLLENBQUMsR0FBRyxDQUFDLEdBQUcsQ0FBQyxDQUFDLENBQUMsQ0FBQztBQUMxRyxDQUFDO0FBRUQsdUJBQXVCLFFBQW9CO0lBQ3ZDLEVBQUUsQ0FBQyxDQUFDLFFBQVEsQ0FBQyxTQUFTLENBQUMsTUFBTSxLQUFLLENBQUMsSUFBSSxRQUFRLENBQUMsV0FBVyxZQUFZLFFBQVEsQ0FBQyxDQUFDLENBQUM7UUFDOUUsSUFBTSxFQUFFLEdBQUcsUUFBUSxDQUFDLFNBQVMsQ0FBQyxDQUFDLENBQUMsQ0FBQztRQUNqQyxJQUFNLEVBQUUsR0FBRyxRQUFRLENBQUMsV0FBVyxDQUFDO1FBQ2hDLElBQU0sRUFBRSxHQUFHLFFBQVEs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QVcsQ0FBQyxDQUFDLENBQUM7UUFDM0QsQ0FBQyxDQUFDLE1BQU0sQ0FBQyxDQUFDLElBQUksQ0FBQyxnQkFBZ0IsRUFBRSxDQUFDLENBQUMsQ0FBQztJQUN4QyxDQUFDO0lBR0QscURBQXFEO0lBRXJELEVBQUUsQ0FBQyxDQUFDLENBQUMsQ0FBQyxJQUFJLENBQUMsQ0FBQyxRQUFRLENBQUMsZ0JBQWdCLENBQUMsQ0FBQyxDQUFDLENBQUM7UUFDckMsTUFBTSxDQUFDO0lBQ1gsQ0FBQztJQUVELENBQUMsQ0FBQyxJQUFJLENBQUMsQ0FBQyxRQUFRLENBQUMsZ0JBQWdCLENBQUMsQ0FBQztJQUVuQyxDQUFDLENBQUMsTUFBTSxDQUFDLENBQUMsSUFBSSxDQUFDLGdCQUFnQixFQUFFLENBQUMsQ0FBQyxNQUFNLENBQUMsQ0FBQyxJQUFJLENBQUMsZ0JBQWdCLENBQUMsR0FBRyxDQUFDLENBQUMsQ0FBQztJQUV2RSxDQUFDLENBQUMsSUFBSSxDQUFDLENBQUMsR0FBRyxDQUFDLFNBQVMsRUFBRSxJQUFJLEdBQUcsQ0FBQyxFQUFFLEdBQUcsQ0FBQyxDQUFDLE1BQU0sQ0FBQyxDQUFDLElBQUksQ0FBQyxnQkFBZ0IsQ0FBQyxDQUFDLENBQUMsQ0FBQztJQUV2RSxDQUFDLENBQUMsaUJBQWlCLENBQUMsQ0FBQyxHQUFHLENBQUMsaUJBQWlCLENBQUM7U0FDdEMsR0FBRyxDQUFDLFNBQVMsRUFBRSxJQUFJLEdBQUcsQ0FBQyxFQUFFLEdBQUcsQ0FBQyxDQUFDLE1BQU0sQ0FBQyxDQUFDLElBQUksQ0FBQyxnQkFBZ0IsQ0FBQyxDQUFDLENBQUMsQ0FBQztJQUdwRSxDQUFDLENBQUMsaUJBQWlCLENBQUMsQ0FBQyxHQUFHLENBQUMsZ0JBQWdCLENBQUM7U0FDckMsUUFBUSxDQUFDLGdCQUFnQixDQUFDLENBQUM7QUFFcEMsQ0FBQyxDQUFDLENBQUMiLCJmaWxlIjoiVmlld01vZGVscy9Ib21lL01hcC5qcyIsInNvdXJjZXNDb250ZW50IjpbImltcG9ydCBTSGFyYm91ciA9IFNlcnZlck1vZGVsLkhhcmJvdXI7XHJcblxyXG5pbXBvcnQgV2F5cG9pbnQgPSBDbGllbnRNb2RlbC5XYXlwb2ludDtcclxuaW1wb3J0IEhhcmJvdXIgPSBDbGllbnRNb2RlbC5IYXJib3VyO1xyXG5pbXBvcnQgSm9iID0gQ2xpZW50TW9kZWwuSm9iO1xyXG5pbXBvcnQgV2F5cG9pbnREaXN0YW5jZSA9IENsaWVudE1vZGVsLldheXBvaW50RGlzdGFuY2U7XHJcblxyXG5mdW5jdGlvbiBnZXRNaWRkbGUocG9sOiBMLlBvbHlsaW5lKTogTC5MYXRMbmcge1xyXG4gICAgY29uc3Qgc3RhcnQgPSBwb2wuZ2V0TGF0TG5ncygpWzBdO1xyXG4gICAgY29uc3QgZW5kID0gcG9sLmdldExhdExuZ3MoKVsxXTtcclxuICAgIHJldHVybiBuZXcgTC5MYXRMbmcoc3RhcnQubGF0ICsgKChlbmQubGF0IC0gc3RhcnQubGF0KSAvIDIpLCBzdGFydC5sbmcgKyAoKGVuZC5sbmcgLSBzdGFydC5sbmcpIC8gMikpO1xyXG59XHJcblxyXG5mdW5jdGlvbiBzcGxpdFBvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lKSB7XHJcbiAgICBpZiAocG9seWxpbmUuV2F5cG9pbnRzLmxlbmd0aCA9PT0gMiAmJiBwb2x5bGluZS5EdW1teUhhbmRsZSBpbnN0YW5jZW9mIFdheXBvaW50KSB7XHJcbiAgICAgICAgY29uc3QgdzEgPSBwb2x5bGluZS5XYXlwb2ludHNbMF07XHJcbiAgICAgICAgY29uc3QgdzIgPSBwb2x5bGluZS5EdW1teUhhbmRsZTtcclxuICAgICAgICBjb25zdCB3MyA9IHBvbHlsaW5lLldheXBvaW50c1sxXTtcclxuICAgICAgICB3Mi5SZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgIHBvbHlsaW5lLkR1bW15SGFuZGxlID0gdW5kZWZpbmVkO1xyXG4gICAgICAgIHcyLkFkZFRvUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgIHczLlJlbW92ZUZyb21Qb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICAgICAgYWRkRHVtbXlIYW5kbGUocG9seWxpbmUpO1xyXG4gICAgICAgIGFkZER1bW15SGFuZGxlKG1hcFZpZXdNb2RlbC5BZGRQb2x5bGluZShbdzIsIHczXSkpO1xyXG4gICAgICAgIHJldHVybjtcclxuICAgIH1cclxuICAgIHRocm93IG5ldyBFcnJvcihcIkNhbm5vdCBzcGxpdCBwb2x5bGluZS4gUG9seWxpbmUgaGFzIG5vIGR1bW15IGhhbmRsZSBvciBsZXNzIG9yIG1vcmUgdGhhbiAyIHdheXBvaW50c1wiKTtcclxufVxyXG5cclxuXHJcbmZ1bmN0aW9uIHJlbW92ZVBvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lKSB7XHJcbiAgICBmb3IgKGxldCB3YXlwb2ludCBvZiBwb2x5bGluZS5XYXlwb2ludHMpIHtcclxuICAgICAgICB3YXlwb2ludC5SZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgfVxyXG4gICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZS5SZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgIHBvbHlsaW5lLkR1bW15SGFuZGxlLlJlbW92ZUZyb21NYXAoKTtcclxuICAgIH1cclxuICAgIG1hcFZpZXdNb2RlbC5NYXAucmVtb3ZlTGF5ZXIocG9seWxpbmUpO1xyXG59XHJcblxyXG5mdW5jdGlvbiBhZGREdW1teUhhbmRsZShwb2x5bGluZTogTC5Qb2x5bGluZSkge1xyXG4gICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlID09PSB1bmRlZmluZWQpIHtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZSA9IG1hcFZpZXdNb2RlbC5DcmVhdGVXYXlwb2ludChnZXRNaWRkbGUocG9seWxpbmUpLCBNYXJrZXJUeXBlLkR1bW15KTtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZS5BZGRUb1BvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgIH1cclxufVxyXG5cclxuZnVuY3Rpb24gcmVkcmF3UG9seWxpbmUocG9seWxpbmU6IEwuUG9seWxpbmUpIHtcclxuICAgIGNvbnN0IG1pZGRsZUxhdExuZyA9IGdldE1pZGRsZShwb2x5bGluZSk7XHJcbiAgICBpZiAocG9seWxpbmUuRHVtbXlIYW5kbGUgPT09IHVuZGVmaW5lZClcclxuICAgICAgICBhZGREdW1teUhhbmRsZShwb2x5bGluZSk7XHJcbiAgICBpZiAocG9seWxpbmUuRHVtbXlIYW5kbGUuTG9uZ2l0dWRlKCkgIT09IG1pZGRsZUxhdExuZy5sbmcgfHwgcG9seWxpbmUuRHVtbXlIYW5kbGUuTGF0aXR1ZGUoKSAhPT0gbWlkZGxlTGF0TG5nLmxhdClcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZS5TZXRMYXRMbmcobWlkZGxlTGF0TG5nKTtcclxuICAgIGVsc2VcclxuICAgICAgICBwb2x5bGluZS5yZWRyYXcoKTtcclxufVxyXG5cclxuZnVuY3Rpb24gcmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lLCBsYXRMbmc6IEwuTGF0TG5nKSB7XHJcbiAgICByZW1vdmVGcm9tQXJyYXkocG9seWxpbmUuZ2V0TGF0TG5ncygpLCBsYXRMbmcpO1xyXG4gICAgcG9seWxpbmUucmVkcmF3KCk7XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHJlbW92ZUZyb21BcnJheTxUPihhcnI6IFRbXSwgb2JqOiBUKTogYm9vbGVhbiB7XHJcbiAgICBjb25zdCB0bXBBcnIgPSBuZXcgQXJyYXk8VD4oKTtcclxuICAgIGZvciAobGV0IGl0ZW0gb2YgYXJyKSB7XHJcbiAgICAgICAgaWYgKGl0ZW0gIT09IG9iailcclxuICAgICAgICAgICAgdG1wQXJyLnB1c2goaXRlbSk7XHJcbiAgICB9XHJcbiAgICBpZiAodG1wQXJyLmxlbmd0aCA9PT0gYXJyLmxlbmd0aClcclxuICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICB3aGlsZSAoYXJyLnBvcCgpKSB7XHJcbiAgICB9XHJcbiAgICB3aGlsZSAodG1wQXJyLmxlbmd0aCA+IDApIHtcclxuICAgICAgICBhcnIucHVzaCh0bXBBcnIuc2hpZnQoKSk7XHJcbiAgICB9XHJcbiAgICByZXR1cm4gdHJ1ZTtcclxufVxyXG5cclxuZW51bSBNYXBNb2RlIHtcclxuICAgIEFkbWluLFxyXG4gICAgVmlldyxcclxuICAgIFRyaXBQbGFubmluZyxcclxuICAgIFJvdXRlRHJhd2luZ1xyXG59XHJcblxyXG5kZWNsYXJlIG5hbWVzcGFjZSBMIHtcclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgUG9seWxpbmUgZXh0ZW5kcyBQYXRoIHtcclxuXHJcbiAgICAgICAgV2F5cG9pbnRzOiBBcnJheTxXYXlwb2ludD47XHJcbiAgICAgICAgRHVtbXlIYW5kbGU6IFdheXBvaW50O1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgTGF0TG5nIHtcclxuICAgICAgICBQb2x5bGluZXM6IFBvbHlsaW5lW107XHJcbiAgICAgICAgV2F5cG9pbnQ6IFdheXBvaW50O1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgTWFya2VyIHtcclxuICAgICAgICBXYXlwb2ludDogV2F5cG9pbnQ7XHJcbiAgICAgICAgUG9pbnQ6IEwuUG9pbnQ7XHJcbiAgICAgICAgX2ljb247XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGludGVyZmFjZSBDaXJjbGVNYXJrZXIge1xyXG4gICAgICAgIFdheXBvaW50OiBXYXlwb2ludDtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIFBhdGhPcHRpb25zIHtcclxuICAgICAgICBEcmFnZ2FibGU/OiBib29sZWFuO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgTWFya2VyT3B0aW9ucyB7XHJcbiAgICAgICAgY29udGV4dG1lbnU/OiBib29sZWFuO1xyXG4gICAgICAgIGNvbnRleHRtZW51V2lkdGg/OiBudW1iZXI7XHJcbiAgICAgICAgY29udGV4dG1lbnVBbmNob3I/OiBMLlBvaW50IHwgTC5Qb2ludFtdO1xyXG4gICAgICAgIGNvbnRleHRtZW51SXRlbXM/OiBjb250ZXh0bWVudUl0ZW1bXTtcclxuICAgICAgICBjb250ZXh0bWVudUluaGVyaXRJdGVtczogYm9vbGVhbjtcclxuXHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGludGVyZmFjZSBjb250ZXh0bWVudUl0ZW0ge1xyXG4gICAgICAgIHRleHQ/OiBzdHJpbmc7XHJcbiAgICAgICAgaWNvbj86IHN0cmluZztcclxuICAgICAgICBpY29uQ2xzPzogc3RyaW5nO1xyXG4gICAgICAgIGNhbGxiYWNrPzogRnVuY3Rpb247XHJcbiAgICAgICAgY29udGV4dD86IE9iamVjdDtcclxuICAgICAgICBkaXNhYmxlZD86IGJvb2xlYW47XHJcbiAgICAgICAgc2VwYXJhdG9yPzogYm9vbGVhbjtcclxuICAgICAgICBoaWRlT25TZWxlY3Q/OiBib29sZWFuO1xyXG4gICAgICAgIGluZGV4PzogbnVtYmVyO1xyXG4gICAgfVxyXG5cclxuXHJcbn1cclxuXHJcbmNsYXNzIE1hcFZpZXdNb2RlbCB7XHJcbiAgICBjb25zdHJ1Y3RvcihtYXBNb2RlOiBNYXBNb2RlKSB7XHJcbiAgICAgICAgTC5tYXBib3hcclxuICAgICAgICAgICAgLmFjY2Vzc1Rva2VuID1cclxuICAgICAgICAgICAgXCJway5leUoxSWpvaVpHRnVhV1ZzTFd0MWIyNGlMQ0poSWpvaVkybGxkblZ0WTI5aU1EQmlPSFF4YlRCdlp6QnFaV2w2Y0NKOS5VRWMyWXFINTlwQjFZVHB2MjJ2ZzhBXCI7XHJcbiAgICAgICAgdGhpcy5NYXBNb2RlKG1hcE1vZGUpO1xyXG4gICAgICAgIHRoaXMuTWFwTW9kZS5zdWJzY3JpYmUoKCkgPT4ge1xyXG4gICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNYXAoKTtcclxuICAgICAgICB9KTtcclxuICAgICAgICBjb25zdCBtYXBPcHRpb25zOiBMLm1hcGJveC5NYXBPcHRpb25zID0ge1xyXG4gICAgICAgICAgICBjb250ZXh0bWVudTogbWFwTW9kZSA9PT0gTWFwTW9kZS5BZG1pbixcclxuICAgICAgICAgICAgY29udGV4dG1lbnVJdGVtczogW1xyXG4gICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgIHRleHQ6IFwiTmV1ZXIgSGFmZW5cIixcclxuICAgICAgICAgICAgICAgICAgICBjYWxsYmFjazogZnVuY3Rpb24gKGUpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29uc29sZS5sb2coZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5FZGl0aW5nSGFyYm91cihtYXBWaWV3TW9kZWwuQ3JlYXRlSGFyYm91cihcIlwiLCBlLmxhdGxuZykpXHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBdXHJcbiAgICAgICAgfTtcclxuICAgICAgICB0aGlzLk1hcCA9IEwubWFwYm94Lm1hcChcIm1hcFwiLFxyXG4gICAgICAgICAgICBcIm1hcGJveC5zdHJlZXRzXCIsXHJcbiAgICAgICAgICAgIG1hcE9wdGlvbnMpO1xyXG4gICAgICAgIHRoaXMuTWFwLnNldFZpZXcoWzU0LjQwNzc0MTY2ODIwMDY5LCAxMC41MjM1MjkwNTI3MzQzNzNdLCA5KTtcclxuICAgICAgICBMLnRpbGVMYXllcihcImh0dHA6Ly90MS5vcGVuc2VhbWFwLm9yZy9zZWFtYXJrL3t6fS97eH0ve3l9LnBuZ1wiKS5hZGRUbyh0aGlzLk1hcCk7XHJcbiAgICAgICAgdGhpcy5Mb2FkRGF0YSgpO1xyXG4gICAgICAgIHRoaXMuU2VsZWN0ZWRIYXJib3VyLnN1YnNjcmliZSgobmV3SGFyYm91cikgPT4ge1xyXG4gICAgICAgICAgICBpZiAobmV3SGFyYm91ciAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuQ2FsY3VsYXRlRGlzdGFuY2VzKG5ld0hhcmJvdXIpO1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJzLnNvcnQoKGgxLCBoMikgPT4gaDEuRGlzdGFuY2UoKSAtIGgyLkRpc3RhbmNlKCkpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgaCBvZiBtYXBWaWV3TW9kZWwuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICAgICAgICAgIGguRGlzdGFuY2UoMCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlRml4ZWQgPSBmYWxzZTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkhpZGVSb3V0ZSgpO1xyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuRWRpdGluZ0hhcmJvdXIuc3Vic2NyaWJlKChoYXJib3VyKSA9PiB7XHJcbiAgICAgICAgICAgIGlmIChoYXJib3VyID09PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGVkaXRpbmdIYXJib3VyTW9kYWwubW9kYWwoXCJoaWRlXCIpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgaGFyYm91ci5TYXZlU3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVkaXRpbmdIYXJib3VyTW9kYWwubW9kYWwoXCJzaG93XCIpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5FZGl0aW5nSGFyYm91ci5zdWJzY3JpYmUoKGhhcmJvdXIpID0+IHtcclxuICAgICAgICAgICAgaWYgKGhhcmJvdXIgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgaGFyYm91ci5SZXZlcnRTdGF0ZSh0cnVlKTtcclxuICAgICAgICAgICAgICAgIGlmIChoYXJib3VyLklkKCkgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKGhhcmJvdXIubWFya2VyKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0sXHJcbiAgICAgICAgICAgIHRoaXMsXHJcbiAgICAgICAgICAgIFwiYmVmb3JlQ2hhbmdlXCIpO1xyXG4gICAgICAgIHRoaXMuRGVsZXRpbmdIYXJib3VyLnN1YnNjcmliZSgoaCkgPT4ge1xyXG4gICAgICAgICAgICBpZiAoaCA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBkZWxldGluZ0hhcmJvdXJNb2RhbC5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICBkZWxldGluZ0hhcmJvdXJNb2RhbC5tb2RhbChcInNob3dcIik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkVkaXRpbmdXYXlwb2ludC5zdWJzY3JpYmUoKHdheXBvaW50KSA9PiB7XHJcbiAgICAgICAgICAgIGlmICh3YXlwb2ludCA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBlZGl0aW5nV2F5cG9pbnRNb2RhbC5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICB3YXlwb2ludC5TYXZlU3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVkaXRpbmdXYXlwb2ludE1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuRWRpdGluZ1dheXBvaW50LnN1YnNjcmliZSgod2F5cG9pbnQpID0+IHtcclxuICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5FZGl0aW5nV2F5cG9pbnQoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkVkaXRpbmdXYXlwb2ludCgpLlJldmVydFN0YXRlKHRydWUpO1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgICAgIHRoaXMsXHJcbiAgICAgICAgICAgIFwiYmVmb3JlQ2hhbmdlXCIpO1xyXG4gICAgICAgIHRoaXMuRGVsZXRpbmdXYXlwb2ludC5zdWJzY3JpYmUoKGgpID0+IHtcclxuICAgICAgICAgICAgaWYgKGggPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgZGVsZXRpbmdXYXlwb2ludE1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIGRlbGV0aW5nV2F5cG9pbnRNb2RhbC5tb2RhbChcInNob3dcIik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkVkaXRpbmdKb2Iuc3Vic2NyaWJlKChqb2IpID0+IHtcclxuICAgICAgICAgICAgaWYgKGpvYiA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBlZGl0aW5nSm9iTW9kYWwubW9kYWwoXCJoaWRlXCIpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgam9iLlNhdmVTdGF0ZSgpO1xyXG4gICAgICAgICAgICAgICAgZWRpdGluZ0pvYk1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuRWRpdGluZ0pvYi5zdWJzY3JpYmUoKGpvYikgPT4ge1xyXG4gICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLkVkaXRpbmdKb2IoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkVkaXRpbmdKb2IoKS5SZXZlcnRTdGF0ZSh0cnVlKTtcclxuICAgICAgICB9LFxyXG4gICAgICAgICAgICB0aGlzLFxyXG4gICAgICAgICAgICBcImJlZm9yZUNoYW5nZVwiKTtcclxuICAgICAgICB0aGlzLkRlbGV0aW5nSm9iLnN1YnNjcmliZSgoaCkgPT4ge1xyXG4gICAgICAgICAgICBpZiAoaCA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBkZWxldGluZ0pvYk1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIGRlbGV0aW5nSm9iTW9kYWwubW9kYWwoXCJzaG93XCIpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5TZWxlY3RlZEhhcmJvdXIuc3Vic2NyaWJlKChoKSA9PiB7XHJcbiAgICAgICAgICAgIGlmIChoID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICByaWdodFNpZGViYXIuSGlkZSgpO1xyXG4gICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICByaWdodFNpZGViYXIuU2hvdygpO1xyXG5cclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLlNlbGVjdGVkVHJpcC5zdWJzY3JpYmUoKHQpID0+IHtcclxuICAgICAgICAgICAgaWYgKHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIGJvdHRvbVNpZGViYXIuSGlkZSgpO1xyXG4gICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICBib3R0b21TaWRlYmFyLlNob3coKTtcclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwibW91c2Vtb3ZlXCIsXHJcbiAgICAgICAgICAgIChlOiBMLkxlYWZsZXRNb3VzZUV2ZW50KSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAodGhpcy5HZXRNYXBNb2RlKCkgPT09IE1hcE1vZGUuUm91dGVEcmF3aW5nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nTGF0TG5nLmxhdCA9IGUubGF0bG5nLmxhdDtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdMYXRMbmcubG5nID0gZS5sYXRsbmcubG5nO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ1BvbHlsaW5lLnJlZHJhdygpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKVxyXG4gICAgICAgICAgICAgICAgICAgIGZvciAobGV0IG1hcmtlciBvZiB0aGlzLldheXBvaW50TWFya2Vycykge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAobWFya2VyLlBvaW50LmRpc3RhbmNlVG8oZS5jb250YWluZXJQb2ludCkgPCAxNTApXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXJrZXIuc2V0T3BhY2l0eShtYXJrZXIuV2F5cG9pbnQuSXNEdW1teSgpID8gMC4wIDogMSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcmtlci5zZXRPcGFjaXR5KG1hcmtlci5XYXlwb2ludC5Jc0R1bW15KCkgPyAwLjAgOiAwLjgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuSG92ZXJlZFBvbHlpbmUgIT09IHVuZGVmaW5lZCAmJiBtYXBWaWV3TW9kZWwuSG92ZXJlZFBvbHlpbmUuRHVtbXlIYW5kbGUgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IHBvbHlsaW5lID0gbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lO1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IHAxID0gbWFwVmlld01vZGVsLk1hcC5sYXRMbmdUb0NvbnRhaW5lclBvaW50KHBvbHlsaW5lLmdldExhdExuZ3MoKVswXSk7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgcDIgPSBtYXBWaWV3TW9kZWwuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQocG9seWxpbmUuZ2V0TGF0TG5ncygpWzFdKTtcclxuICAgICAgICAgICAgICAgICAgICBpZiAocDEuZGlzdGFuY2VUbyhlLmNvbnRhaW5lclBvaW50KSA8IDIwIHx8IHAyLmRpc3RhbmNlVG8oZS5jb250YWluZXJQb2ludCkgPCAyMCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSG92ZXJlZFBvbHlpbmUgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lLkR1bW15SGFuZGxlLm1hcmtlci5zZXRPcGFjaXR5KDAuOCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZS5EdW1teUhhbmRsZVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLlNldExhdExuZyhtYXBWaWV3TW9kZWwuTWFwXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgLmNvbnRhaW5lclBvaW50VG9MYXRMbmcoTC5MaW5lVXRpbFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAuY2xvc2VzdFBvaW50T25TZWdtZW50KGUuY29udGFpbmVyUG9pbnQsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIHAxLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBwMikpLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgZmFsc2UpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH1cclxuXHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuTWFwLmFkZEV2ZW50TGlzdGVuZXIoXCJjbGlja1wiLFxyXG4gICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuR2V0TWFwTW9kZSgpID09PSBNYXBNb2RlLlJvdXRlRHJhd2luZykge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IHdheXBvaW50ID0gbWFwVmlld01vZGVsLkNyZWF0ZVdheXBvaW50KGUubGF0bG5nLCBNYXJrZXJUeXBlLldheXBvaW50KTtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBzdGFydElkID0gdGhpcy5EcmF3aW5nUG9seWxpbmUuV2F5cG9pbnRzWzBdLklkKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgd2F5cG9pbnQuU2F2ZVRvU2VydmVyKClcclxuICAgICAgICAgICAgICAgICAgICAgICAgLmRvbmUodyA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBTZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5Db25uZWN0KHcuSWQsIHN0YXJ0SWQpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgICAgICB3YXlwb2ludC5BZGRUb1BvbHlsaW5lKHRoaXMuRHJhd2luZ1BvbHlsaW5lKTtcclxuICAgICAgICAgICAgICAgICAgICBhZGREdW1teUhhbmRsZSh0aGlzLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgcmVtb3ZlRnJvbVBvbHlsaW5lKHRoaXMuRHJhd2luZ1BvbHlsaW5lLCB0aGlzLkRyYXdpbmdMYXRMbmcpO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ1BvbHlsaW5lID0gdGhpcy5BZGRQb2x5bGluZSh3YXlwb2ludCk7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nTGF0TG5nID0gbmV3IEwuTGF0TG5nKGUubGF0bG5nLmxhdCwgZS5sYXRsbmcubG5nKTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdQb2x5bGluZS5hZGRMYXRMbmcodGhpcy5EcmF3aW5nTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIHRoaXMuTWFwLmFkZEV2ZW50TGlzdGVuZXIoXCJkYmxjbGlja1wiLFxyXG4gICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuR2V0TWFwTW9kZSgpID09PSBNYXBNb2RlLlJvdXRlRHJhd2luZykge1xyXG4gICAgICAgICAgICAgICAgICAgIGUub3JpZ2luYWxFdmVudC5jYW5jZWxCdWJibGUgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgICAgIGUub3JpZ2luYWxFdmVudC5wcmV2ZW50RGVmYXVsdCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIGUub3JpZ2luYWxFdmVudC5zdG9wUHJvcGFnYXRpb24oKTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdQb2x5bGluZS5hZGRMYXRMbmcoZS5sYXRsbmcpO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ0xhdExuZyA9IGUubGF0bG5nO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAkKGRvY3VtZW50KVxyXG4gICAgICAgICAgICAua2V5dXAoKGU6IEpRdWVyeUtleUV2ZW50T2JqZWN0KSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAodGhpcy5HZXRNYXBNb2RlKCkgPT09IE1hcE1vZGUuUm91dGVEcmF3aW5nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKGUua2V5Q29kZSA9PT0gMjcpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5SZW1vdmVQb2x5bGluZSh0aGlzLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwibW92ZVwiLFxyXG4gICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgbWFya2VyIG9mIHRoaXMuV2F5cG9pbnRNYXJrZXJzKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgbWFya2VyLlBvaW50ID0gdGhpcy5NYXAubGF0TG5nVG9Db250YWluZXJQb2ludChtYXJrZXIuZ2V0TGF0TG5nKCkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwiem9vbVwiLFxyXG4gICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgbWFya2VyIG9mIHRoaXMuV2F5cG9pbnRNYXJrZXJzKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgbWFya2VyLlBvaW50ID0gdGhpcy5NYXAubGF0TG5nVG9Db250YWluZXJQb2ludChtYXJrZXIuZ2V0TGF0TG5nKCkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgIH1cclxuXHJcbiAgICBwcml2YXRlIHJvdXRlUG9seWxpbmUgPSBrby5vYnNlcnZhYmxlPEwuUG9seWxpbmU+KCk7XHJcblxyXG4gICAgU3RhcnRSb3V0ZSgpIHtcclxuICAgICAgICBjb25zdCB0cmlwID0gbmV3IENsaWVudE1vZGVsLlRyaXAoKTtcclxuICAgICAgICBjb25zdCB0YWNrID0gbmV3IENsaWVudE1vZGVsLlRhY2soKTtcclxuICAgICAgICBjb25zdCBoYXJib3VyID0gbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cigpO1xyXG4gICAgICAgIHRhY2suU3RhcnQoaGFyYm91cik7XHJcbiAgICAgICAgdHJpcC5UYWNrcy5wdXNoKHRhY2spO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5TZWxlY3RlZFRyaXAodHJpcCk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoTC5wb2x5bGluZShbXSxcclxuICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgY29sb3I6IFwiIzAwOTkwMFwiXHJcbiAgICAgICAgICAgIH0pKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpLmFkZFRvKG1hcFZpZXdNb2RlbC5NYXApO1xyXG4gICAgfVxyXG5cclxuICAgIElzTGFzdFRha0luUm91dGUgPSBrby5jb21wdXRlZCh7XHJcbiAgICAgICAgcmVhZDogKCkgPT4ge1xyXG4gICAgICAgICAgICB2YXIgdHJpcCA9IG1hcFZpZXdNb2RlbC5TZWxlY3RlZFRyaXAoKTtcclxuICAgICAgICAgICAgdmFyIGggPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRIYXJib3VyKCk7XHJcbiAgICAgICAgICAgIHJldHVybiB0cmlwICE9PSB1bmRlZmluZWQgJiYgaCAhPT0gdW5kZWZpbmVkICYmIHRyaXAuVGFja3MoKVt0cmlwLlRhY2tzKCkubGVuZ3RoIC0gMV0uU3RhcnQoKSA9PT0gaDtcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG4gICAgR2V0Um91dGVEaXN0YW5jZSA9IGtvLmNvbXB1dGVkKHtcclxuICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgIHZhciBkaXN0YW5jZSA9IDA7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHRhY2sgb2YgbWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpLlRhY2tzKCkpIHtcclxuICAgICAgICAgICAgICAgIGlmICghaXNOYU4odGFjay5EaXN0YW5jZSgpKSlcclxuICAgICAgICAgICAgICAgICAgICBkaXN0YW5jZSArPSB0YWNrLkRpc3RhbmNlKCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIGRpc3RhbmNlO1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgZGVmZXJFdmFsdWF0aW9uOiB0cnVlXHJcbiAgICB9KTtcclxuXHJcbiAgICBUb3BKb2JzID0ga28uY29tcHV0ZWQoe1xyXG4gICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5Kb2JzKCkuZmlsdGVyKChqKSA9PiBqLlN1cGVySm9iSWQoKSA9PT0gdW5kZWZpbmVkKTtcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG4gICAgQWRkVG9Sb3V0ZSgpIHtcclxuICAgICAgICBjb25zdCB0cmlwID0gbWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpO1xyXG4gICAgICAgIGNvbnN0IHRhcmdldEhhcmJvdXIgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRIYXJib3VyKCk7XHJcbiAgICAgICAgY29uc3QgdGFjayA9IG5ldyBDbGllbnRNb2RlbC5UYWNrKCk7XHJcbiAgICAgICAgY29uc3QgbGFzdFRhY2sgPSB0cmlwLlRhY2tzKClbdHJpcC5UYWNrcygpLmxlbmd0aCAtIDFdO1xyXG4gICAgICAgIGNvbnN0IHN0YXJ0SGFyYm91ciA9IGxhc3RUYWNrLlN0YXJ0KCk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkNhbGN1bGF0ZURpc3RhbmNlcyh0YXJnZXRIYXJib3VyLCBzdGFydEhhcmJvdXIpO1xyXG4gICAgICAgIGxhc3RUYWNrLkRpc3RhbmNlKHN0YXJ0SGFyYm91ci5Sb3V0ZURpc3RhbmNlKCkpO1xyXG4gICAgICAgIGxldCB3cDogQ2xpZW50TW9kZWwuV2F5cG9pbnQgPSBzdGFydEhhcmJvdXI7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRMYXRMbmcod3AuTGF0TG5nKTtcclxuICAgICAgICB3aGlsZSAod3AuUm91dGVQcmVjZXNzb3IoKSAhPT0gdW5kZWZpbmVkIC8qJiYgd3AuUm91dGVQcmVjZXNzb3IoKSAhPT0gc3RhcnRIYXJib3VyKi8pIHtcclxuICAgICAgICAgICAgd3AgPSB3cC5Sb3V0ZVByZWNlc3NvcigpO1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpLmFkZExhdExuZyh3cC5MYXRMbmcpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgbGFzdFRhY2suRW5kKHRhcmdldEhhcmJvdXIpO1xyXG4gICAgICAgIHRhY2suU3RhcnQodGFyZ2V0SGFyYm91cik7XHJcbiAgICAgICAgdHJpcC5UYWNrcy5wdXNoKHRhY2spO1xyXG4gICAgfVxyXG5cclxuICAgIFJlZHJhd1RyaXAoKSB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZShMLnBvbHlsaW5lKFtdLFxyXG4gICAgICAgICAgICB7XHJcbiAgICAgICAgICAgICAgICBjb2xvcjogXCIjMDA5OTAwXCJcclxuICAgICAgICAgICAgfSkpO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZVBvbHlsaW5lKCkuYWRkVG8obWFwVmlld01vZGVsLk1hcCk7XHJcbiAgICAgICAgZm9yIChsZXQgdGFjayBvZiBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKCkuVGFja3MoKSkge1xyXG4gICAgICAgICAgICBjb25zdCB0YXJnZXRIYXJib3VyID0gdGFjay5FbmQoKTtcclxuICAgICAgICAgICAgY29uc3Qgc3RhcnRIYXJib3VyID0gdGFjay5TdGFydCgpO1xyXG4gICAgICAgICAgICBpZiAodGFyZ2V0SGFyYm91ciA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgY29udGludWU7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DYWxjdWxhdGVEaXN0YW5jZXModGFyZ2V0SGFyYm91ciwgc3RhcnRIYXJib3VyKTtcclxuICAgICAgICAgICAgdGFjay5EaXN0YW5jZShzdGFydEhhcmJvdXIuUm91dGVEaXN0YW5jZSgpKTtcclxuICAgICAgICAgICAgbGV0IHdwOiBDbGllbnRNb2RlbC5XYXlwb2ludCA9IHN0YXJ0SGFyYm91cjtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRMYXRMbmcod3AuTGF0TG5nKTtcclxuICAgICAgICAgICAgd2hpbGUgKHdwLlJvdXRlUHJlY2Vzc29yKCkgIT09IHVuZGVmaW5lZCAvKiYmIHdwLlJvdXRlUHJlY2Vzc29yKCkgIT09IHN0YXJ0SGFyYm91ciovKSB7XHJcbiAgICAgICAgICAgICAgICB3cCA9IHdwLlJvdXRlUHJlY2Vzc29yKCk7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpLmFkZExhdExuZyh3cC5MYXRMbmcpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIFB1bGxUYWNrKCkge1xyXG4gICAgICAgIGNvbnN0IHRhY2s6IENsaWVudE1vZGVsLlRhY2sgPSA8YW55PnRoaXM7XHJcbiAgICAgICAgY29uc3QgdGFja3MgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKCkuVGFja3M7XHJcbiAgICAgICAgY29uc3QgaW5kZXggPSB0YWNrcy5pbmRleE9mKHRhY2spO1xyXG4gICAgICAgIGNvbnN0IHByZXZUYWNrID0gdGFja3MoKVtpbmRleCAtIDFdO1xyXG4gICAgICAgIHZhciB0bXBFbmQgPSB0YWNrLkVuZCgpO1xyXG4gICAgICAgIHRhY2suRW5kKHByZXZUYWNrLlN0YXJ0KCkpO1xyXG4gICAgICAgIHByZXZUYWNrLkVuZCh0bXBFbmQpO1xyXG4gICAgICAgIGlmIChpbmRleCA+IDEpIHtcclxuICAgICAgICAgICAgdGFja3MoKVtpbmRleCAtIDJdLkVuZCh0YWNrLlN0YXJ0KCkpO1xyXG4gICAgICAgIH1cclxuICAgICAgICB0YWNrcy5zcGxpY2UoaW5kZXggLSAxLCAyLCB0YWNrLCBwcmV2VGFjayk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlJlZHJhd1RyaXAoKTtcclxuICAgIH1cclxuXHJcbiAgICBQdXNoVGFjaygpIHtcclxuICAgICAgICBjb25zdCB0YWNrOiBDbGllbnRNb2RlbC5UYWNrID0gPGFueT50aGlzO1xyXG4gICAgICAgIGNvbnN0IHRhY2tzID0gbWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpLlRhY2tzO1xyXG4gICAgICAgIGNvbnN0IGluZGV4ID0gdGFja3MuaW5kZXhPZih0YWNrKTtcclxuICAgICAgICBjb25zdCBuZXh0VGFjayA9IHRhY2tzKClbaW5kZXggKyAxXTtcclxuICAgICAgICB0YWNrLkVuZChuZXh0VGFjay5FbmQoKSk7XHJcbiAgICAgICAgbmV4dFRhY2suRW5kKHRhY2suU3RhcnQoKSk7XHJcbiAgICAgICAgaWYgKGluZGV4ID4gMCkge1xyXG4gICAgICAgICAgICB0YWNrcygpW2luZGV4IC0gMV0uRW5kKG5leHRUYWNrLlN0YXJ0KCkpO1xyXG4gICAgICAgIH1cclxuICAgICAgICB0YWNrcy5zcGxpY2UoaW5kZXgsIDIsIG5leHRUYWNrLCB0YWNrKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuUmVkcmF3VHJpcCgpO1xyXG4gICAgfVxyXG5cclxuICAgIFJlbW92ZVRhY2soKSB7XHJcbiAgICAgICAgY29uc3QgdGFjazogQ2xpZW50TW9kZWwuVGFjayA9IDxhbnk+dGhpcztcclxuICAgICAgICBjb25zdCB0YWNrcyA9IG1hcFZpZXdNb2RlbC5TZWxlY3RlZFRyaXAoKS5UYWNrcztcclxuICAgICAgICBjb25zdCBpbmRleCA9IHRhY2tzLmluZGV4T2YodGFjayk7XHJcbiAgICAgICAgY29uc3QgcHJldlRhY2sgPSB0YWNrcygpW2luZGV4IC0gMV07XHJcbiAgICAgICAgaWYgKHByZXZUYWNrICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgIHByZXZUYWNrLkVuZCh0YWNrLkVuZCgpKTtcclxuICAgICAgICB0YWNrcy5yZW1vdmUodGFjayk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlJlZHJhd1RyaXAoKTtcclxuICAgIH1cclxuXHJcbiAgICBJc0luVmlld01vZGUgPSBrby5jb21wdXRlZDxib29sZWFuPih7XHJcbiAgICAgICAgcmVhZDogKCkgPT4ge1xyXG4gICAgICAgICAgICByZXR1cm4gbWFwVmlld01vZGVsLk1hcE1vZGUoKSA9PT0gTWFwTW9kZS5WaWV3O1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgZGVmZXJFdmFsdWF0aW9uOiB0cnVlXHJcbiAgICB9KTtcclxuXHJcbiAgICBJc0luQWRtaW5Nb2RlID0ga28uY29tcHV0ZWQ8Ym9vbGVhbj4oe1xyXG4gICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW47XHJcbiAgICAgICAgfSxcclxuICAgICAgICBkZWZlckV2YWx1YXRpb246IHRydWVcclxuICAgIH0pO1xyXG5cclxuXHJcbiAgICBMb2FkRGF0YSgpIHtcclxuICAgICAgICBTZXJ2ZXJBcGkuV2F5cG9pbnRBcGkuR2V0RGVmYXVsdCgpXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChzRW50aXR5LlR5cGUgPT09IFNlcnZlck1vZGVsLldheXBvaW50LkdldFR5cGUoKSlcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5XYXlwb2ludHMucHVzaChtYXBWaWV3TW9kZWxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5DcmVhdGVXYXlwb2ludChMLmxhdExuZyhzRW50aXR5LkxhdGl0dWRlLCBzRW50aXR5LkxvbmdpdHVkZSksIE1hcmtlclR5cGUuV2F5cG9pbnQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsc2UgaWYgKHNFbnRpdHkuVHlwZSA9PT0gU2VydmVyTW9kZWwuSGFyYm91ci5HZXRUeXBlKCkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29uc3QgaGFyYm91ciA9IG1hcFZpZXdNb2RlbFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLkNyZWF0ZUhhcmJvdXIoc0VudGl0eS5OYW1lLCBMLmxhdExuZyhzRW50aXR5LkxhdGl0dWRlLCBzRW50aXR5LkxvbmdpdHVkZSkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSBhcyBTSGFyYm91cik7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuSGFyYm91cnMucHVzaChoYXJib3VyKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICAgICAgdGhpcy5XYXlwb2ludHNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5XYXlwb2ludENvbm5lY3Rpb25zLnB1c2goc0VudGl0eSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLldheXBvaW50Q29ubmVjdGlvbnNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLlBlcnNvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5QZXJzb25zLnB1c2gobmV3IENsaWVudE1vZGVsLlBlcnNvbigpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuUGVyc29uc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuSm9iQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkpvYnMucHVzaChuZXcgQ2xpZW50TW9kZWwuSm9iKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5Kb2JzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5UcmlwQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLlRyaXBzLnB1c2gobmV3IENsaWVudE1vZGVsLlRyaXAoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLlRyaXBzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5BZGRyZXNzQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkFkZHJlc3Nlcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5BZGRyZXNzKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5BZGRyZXNzZXNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLkltYWdlQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkltYWdlcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5JbWFnZSgpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuSW1hZ2VzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5BbGJ1bUFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5BbGJ1bXMucHVzaChuZXcgQ2xpZW50TW9kZWwuQWxidW0oKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLkFsYnVtc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuQWxidW1JbWFnZUFwaS5HZXREZWZhdWx0KCkuR2V0KCkuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgZm9yIChsZXQgYWkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5BbGJ1bUltYWdlcy5wdXNoKGFpKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB0aGlzLkFsYnVtSW1hZ2VzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICB9KVxyXG4gICAgICAgIC8vU2VydmVyQXBpLldheXBvaW50VGFja0FwaS5HZXREZWZhdWx0KCkuR2V0KCkuZG9uZShkID0+IHtcclxuICAgICAgICAvLyAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHsgdGhpcy5XYXlwb2ludFRhY2tzLnB1c2gobmV3IENsaWVudE1vZGVsLldheXBvaW50VGFjaygpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTsgfVxyXG4gICAgICAgIC8vICAgIHRoaXMuV2F5cG9pbnRUYWNrc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgLy8gICAgdGhpcy5Jbml0aWFsb3plTW9kZWwoKTtcclxuICAgICAgICAvL30pO1xyXG4gICAgICAgIFNlcnZlckFwaS5UYWNrQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLlRhY2tzLnB1c2gobmV3IENsaWVudE1vZGVsLlRhY2soKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLlRhY2tzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5Mb2NhdGlvbkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHNFbnRpdHkuVHlwZSA9PT0gU2VydmVyTW9kZWwuTG9jYXRpb24uR2V0VHlwZSgpKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkxvY2F0aW9ucy5wdXNoKG5ldyBDbGllbnRNb2RlbC5Mb2NhdGlvbigpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgICAgICBlbHNlIGlmIChzRW50aXR5LlR5cGUgPT09IFNlcnZlck1vZGVsLlJlc3RhdXJhbnQuR2V0VHlwZSgpKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLlJlc3RhdXJhbnRzLnB1c2gobmV3IENsaWVudE1vZGVsLlJlc3RhdXJhbnQoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICAgICAgZWxzZSBpZiAoc0VudGl0eS5UeXBlID09PSBTZXJ2ZXJNb2RlbC5TdXBlcm1hcmtldC5HZXRUeXBlKCkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuU3VwZXJtYXJrZXRzLnB1c2gobmV3IENsaWVudE1vZGVsLlN1cGVybWFya2V0KCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5Mb2NhdGlvbnNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgSW5pdGlhbGl6ZU1vZGVsKCkge1xyXG4gICAgICAgIGlmICh0aGlzLldheXBvaW50c0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLldheXBvaW50Q29ubmVjdGlvbnNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5QZXJzb25zTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuSm9ic0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLlRyaXBzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuQWRkcmVzc2VzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuSW1hZ2VzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuQWxidW1zTG9hZGVkICYmXHJcbiAgICAgICAgICAgIC8vdGhpcy5XYXlwb2ludFRhY2tzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuVGFja3NMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5Mb2NhdGlvbnNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5BbGJ1bUltYWdlc0xvYWRlZCkge1xyXG4gICAgICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5Kb2JzKCkpIHtcclxuICAgICAgICAgICAgICAgIGlmIChlbnRpdHkuQXNzaWduZWRUb0lkKCkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICBlbnRpdHkuQXNzaWduZWRUbyh0aGlzLkdldFBlcnNvbkJ5SWQoZW50aXR5LkFzc2lnbmVkVG9JZCgpKSk7XHJcbiAgICAgICAgICAgICAgICBpZiAoZW50aXR5LlRyaXBJZCgpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgZW50aXR5LlRyaXAodGhpcy5HZXRUcmlwQnlJZChlbnRpdHkuVHJpcElkKCkpKTtcclxuICAgICAgICAgICAgICAgIGlmIChlbnRpdHkuU3VwZXJKb2JJZCgpICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBlbnRpdHkuU3VwZXJKb2IodGhpcy5HZXRKb2JCeUlkKGVudGl0eS5TdXBlckpvYklkKCkpKTtcclxuICAgICAgICAgICAgICAgICAgICBlbnRpdHkuU3VwZXJKb2IoKS5TdWJKb2JzLnB1c2goZW50aXR5KTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgICAgICBlbnRpdHkuQWxidW0odGhpcy5HZXRBbGJ1bUJ5SWQoZW50aXR5LkFsYnVtSWQoKSkpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkxvY2F0aW9ucygpKSB7XHJcbiAgICAgICAgICAgICAgICBlbnRpdHkuQWRkcmVzcyh0aGlzLkdldEFkZHJlc3NCeUlkKGVudGl0eS5BZGRyZXNzSWQoKSkpO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5HZXRIYXJib3VyQnlJZChlbnRpdHkuSGFyYm91cklkKCkpLkxvY2F0aW9ucy5wdXNoKGVudGl0eSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuQWxidW1JbWFnZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5HZXRBbGJ1bUJ5SWQoZW50aXR5LkFsYnVtSWQpLkltYWdlcy5wdXNoKHRoaXMuR2V0SW1hZ2VCeUlkKGVudGl0eS5JbWFnZUlkKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgY29ubmVjdGlvbiBvZiBtYXBWaWV3TW9kZWwuV2F5cG9pbnRDb25uZWN0aW9ucygpKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwb2x5bGluZSA9IG1hcFZpZXdNb2RlbC5BZGRQb2x5bGluZShbXHJcbiAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkdldFdheVBvaW50QnlJZChjb25uZWN0aW9uLldheXBvaW50MUlkKSwgbWFwVmlld01vZGVsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5HZXRXYXlQb2ludEJ5SWQoY29ubmVjdGlvbi5XYXlwb2ludDJJZClcclxuICAgICAgICAgICAgICAgIF0pO1xyXG4gICAgICAgICAgICAgICAgYWRkRHVtbXlIYW5kbGUocG9seWxpbmUpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICQoXCIjbG9hZGluZ092ZXJsYXlcIikucmVtb3ZlKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIEluaXRpYWxpemVNYXAoKSB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cih1bmRlZmluZWQpO1xyXG4gICAgICAgIGZvciAobGV0IHdwIG9mIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMoKSkge1xyXG4gICAgICAgICAgICBpZiAod3AubWFya2VyICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKHdwLm1hcmtlcik7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DcmVhdGVNYXJrZXIoTWFya2VyVHlwZS5XYXlwb2ludCwgd3ApO1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCBoIG9mIG1hcFZpZXdNb2RlbC5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgIGlmIChoLm1hcmtlciAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihoLm1hcmtlcik7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DcmVhdGVNYXJrZXIoTWFya2VyVHlwZS5IYXJib3VyLCBoKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgcCBvZiBtYXBWaWV3TW9kZWwuUG9seWxpbmVzKSB7XHJcbiAgICAgICAgICAgIGlmIChwLkR1bW15SGFuZGxlLm1hcmtlciAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihwLkR1bW15SGFuZGxlLm1hcmtlcik7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DcmVhdGVNYXJrZXIoTWFya2VyVHlwZS5EdW1teSwgcC5EdW1teUhhbmRsZSk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKSB7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHAgb2YgbWFwVmlld01vZGVsLlBvbHlsaW5lcykge1xyXG4gICAgICAgICAgICAgICAgcC5hZGRUbyhtYXBWaWV3TW9kZWwuTWFwKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLmNvbnRleHRtZW51LmVuYWJsZSgpO1xyXG4gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHAgb2YgbWFwVmlld01vZGVsLlBvbHlsaW5lcykge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihwKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLmNvbnRleHRtZW51LmRpc2FibGUoKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgTWFwOiBMLm1hcGJveC5NYXA7XHJcblxyXG4gICAgR2V0V2F5cG9pbnRCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5XYXlwb2ludCB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gV2F5cG9pbnQgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRIYXJib3VyQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuSGFyYm91ciB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSGFyYm91ciB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgfVxyXG5cclxuICAgIEdldFBlcnNvbkJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLlBlcnNvbiB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuUGVyc29ucygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBQZXJzb24gd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRKb2JCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5Kb2Ige1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkpvYnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSm9iIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICB9XHJcblxyXG4gICAgR2V0VHJpcEJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLlRyaXAge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLlRyaXBzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgLy90aHJvdyBcIk5vIFRyaXAgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRBZGRyZXNzQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuQWRkcmVzcyB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuQWRkcmVzc2VzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgLy90aHJvdyBcIk5vIEFkZHJlc3Mgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRJbWFnZUJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLkltYWdlIHtcclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5JbWFnZXMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSW1hZ2Ugd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgIH1cclxuXHJcbiAgICBHZXRUYWNrQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuVGFjayB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuVGFja3MoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gVGFjayB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgfVxyXG5cclxuICAgIEdldEFsYnVtQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuQWxidW0ge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkFsYnVtcygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBUYWNrIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICB9XHJcblxyXG4gICAgR2V0TG9jYXRpb25CeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5Mb2NhdGlvbiB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuTG9jYXRpb25zKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuUmVzdGF1cmFudHMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5SZXN0YXVyYW50cygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBMb2NhdGlvbiB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgfVxyXG5cclxuICAgIFdheXBvaW50c0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgV2F5cG9pbnRDb25uZWN0aW9uc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgUGVyc29uc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgSm9ic0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgVHJpcHNMb2FkZWQgPSBmYWxzZTtcclxuICAgIEFkZHJlc3Nlc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgSW1hZ2VzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBBbGJ1bXNMb2FkZWQgPSBmYWxzZTtcclxuICAgIFdheXBvaW50VGFja3NMb2FkZWQgPSBmYWxzZTtcclxuICAgIFRhY2tzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBMb2NhdGlvbnNMb2FkZWQgPSBmYWxzZTtcclxuICAgIEFsYnVtSW1hZ2VzTG9hZGVkID0gZmFsc2U7XHJcblxyXG4gICAgV2F5cG9pbnRzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLldheXBvaW50PigpO1xyXG4gICAgV2F5cG9pbnRDb25uZWN0aW9ucyA9IGtvLm9ic2VydmFibGVBcnJheTxTZXJ2ZXJNb2RlbC5XYXlwb2ludENvbm5lY3Rpb24+KCk7XHJcbiAgICBIYXJib3VycyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5IYXJib3VyPigpO1xyXG4gICAgUGVyc29ucyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5QZXJzb24+KCk7XHJcbiAgICBKb2JzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkpvYj4oKTtcclxuICAgIFRyaXBzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLlRyaXA+KCk7XHJcbiAgICBBZGRyZXNzZXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuQWRkcmVzcz4oKTtcclxuICAgIEltYWdlcyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5JbWFnZT4oKTtcclxuICAgIFRhY2tzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLlRhY2s+KCk7XHJcbiAgICBMb2NhdGlvbnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuTG9jYXRpb24+KCk7XHJcbiAgICBTdXBlcm1hcmtldHMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuU3VwZXJtYXJrZXQ+KCk7XHJcbiAgICBSZXN0YXVyYW50cyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5SZXN0YXVyYW50PigpO1xyXG4gICAgQWxidW1zID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkFsYnVtPigpO1xyXG4gICAgQWxidW1JbWFnZXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8U2VydmVyTW9kZWwuQWxidW1JbWFnZT4oKTtcclxuXHJcblxyXG4gICAgU2VsZWN0ZWRXYXlwb2ludCA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuV2F5cG9pbnQ+KCk7XHJcbiAgICBTZWxlY3RlZEhhcmJvdXIgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLkhhcmJvdXI+KCk7XHJcbiAgICBTZWxlY3RlZFBlcnNvbiA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuUGVyc29uPigpO1xyXG4gICAgU2VsZWN0ZWRKb2IgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLkpvYj4oKTtcclxuICAgIFNlbGVjdGVkVHJpcCA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuVHJpcD4oKTtcclxuICAgIFNlbGVjdGVkQWRkcmVzcyA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuQWRkcmVzcz4oKTtcclxuICAgIFNlbGVjdGVkSW1hZ2UgPSBrby5vYnNlcnZhYmxlPENsaWVudE1vZGVsLkltYWdlPigpO1xyXG4gICAgU2VsZWN0ZWRUYWNrID0ga28ub2JzZXJ2YWJsZTxDbGllbnRNb2RlbC5UYWNrPigpO1xyXG4gICAgU2VsZWN0ZWRMb2NhdGlvbiA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuTG9jYXRpb24+KCk7XHJcbiAgICBTZWxlY3RlZFN1cGVybWFya2V0ID0ga28ub2JzZXJ2YWJsZTxDbGllbnRNb2RlbC5TdXBlcm1hcmtldD4oKTtcclxuICAgIFNlbGVjdGVkUmVzdGF1cmFudCA9IGtvLm9ic2VydmFibGU8Q2xpZW50TW9kZWwuUmVzdGF1cmFudD4oKTtcclxuXHJcbiAgICBJbml0R2FsbGVyeSgpIHtcclxuICAgICAgICBjb25zdCBpdGVtcyA9IG5ldyBBcnJheTxQaG90b1N3aXBlLkl0ZW0+KCk7XHJcbiAgICAgICAgY29uc3QgY3VyckltYWdlOiBDbGllbnRNb2RlbC5JbWFnZSA9IHRoaXMgYXMgYW55O1xyXG4gICAgICAgIGZvciAobGV0IGRhdGEgb2YgbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cigpLkFsYnVtKCkuSW1hZ2VzKCkpIHtcclxuICAgICAgICAgICAgaXRlbXMucHVzaCgoe1xyXG4gICAgICAgICAgICAgICAgaDogZGF0YS5IZWlnaHQoKSxcclxuICAgICAgICAgICAgICAgIHc6IGRhdGEuV2lkdGgoKSxcclxuICAgICAgICAgICAgICAgIHNyYzogZGF0YS5QYXRoKClcclxuICAgICAgICAgICAgfSBhcyBhbnkpKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZ2FsbGVyeSA9IG5ldyBQaG90b1N3aXBlKHBzd3AsXHJcbiAgICAgICAgICAgIFBob3RvU3dpcGVVSV9EZWZhdWx0LFxyXG4gICAgICAgICAgICBpdGVtcyxcclxuICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgaW5kZXg6IG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIoKS5BbGJ1bSgpLkltYWdlcy5pbmRleE9mKGN1cnJJbWFnZSkgYXMgbnVtYmVyLFxyXG4gICAgICAgICAgICAgICAgZ2V0VGh1bWJCb3VuZHNGbjogKGluZGV4OiBudW1iZXIpOiB7IHg6IG51bWJlcjsgeTogbnVtYmVyOyB3OiBudW1iZXIgfSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgZWxlbSA9ICQoXCIuaW1hZ2VzOmZpcnN0IGltZ1wiKVtpbmRleF07XHJcbiAgICAgICAgICAgICAgICAgICAgdmFyIHBhZGRpbmcgPSBwYXJzZUZsb2F0KHdpbmRvdy5nZXRDb21wdXRlZFN0eWxlKGVsZW0sIG51bGwpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5nZXRQcm9wZXJ0eVZhbHVlKFwicGFkZGluZy1sZWZ0XCIpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5yZXBsYWNlKFwicHhcIiwgXCJcIikpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsZW0uc2Nyb2xsSW50b1ZpZXcoKTtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBib3VuZHMgPSBlbGVtLmdldEJvdW5kaW5nQ2xpZW50UmVjdCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHg6IGJvdW5kcy5sZWZ0ICsgcGFkZGluZyxcclxuICAgICAgICAgICAgICAgICAgICAgICAgeTogYm91bmRzLnRvcCArIHdpbmRvdy5zY3JlZW5ZICsgcGFkZGluZyxcclxuICAgICAgICAgICAgICAgICAgICAgICAgdzogYm91bmRzLndpZHRoIC0gKDIgKiBwYWRkaW5nKVxyXG4gICAgICAgICAgICAgICAgICAgIH07XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIGdhbGxlcnkuaW5pdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIEFkZEhhcmJvdXIoKTogdm9pZCB7XHJcbiAgICAgICAgY29uc3QgaGFyYm91ciA9IG1hcFZpZXdNb2RlbC5DcmVhdGVIYXJib3VyKGBIYWZlbiAke3RoaXMuSGFyYm91cnMubGVuZ3RofWAsIHRoaXMuTWFwLmdldENlbnRlcigpKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuSGFyYm91cnMucHVzaChoYXJib3VyKTtcclxuICAgICAgICBoYXJib3VyLlNhdmVUb1NlcnZlcigpO1xyXG4gICAgfVxyXG5cclxuICAgIFJlbW92ZUhhcmJvdXIgPSAoKSA9PiB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLlNlbGVjdGVkV2F5cG9pbnQoKS5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLldheXBvaW50cy5yZW1vdmUodGhpcy5TZWxlY3RlZFdheXBvaW50KCkpO1xyXG4gICAgfTtcclxuICAgIFJlbW92ZVdheXBvaW50ID0gKCkgPT4ge1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIoKS5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJzLnJlbW92ZSh0aGlzLlNlbGVjdGVkSGFyYm91cigpKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuSGFyYm91cnMucmVtb3ZlKHRoaXMuU2VsZWN0ZWRIYXJib3VyKCkpO1xyXG4gICAgfTtcclxuXHJcbiAgICAvL0NvcHlIYXJib3VyKGgxOiBIYXJib3VyLCBoMjogSGFyYm91cik6IHZvaWQge1xyXG4gICAgLy8gICAgdGhpcy5Db3B5V2F5cG9pbnQoaDEsIGgyKTtcclxuICAgIC8vfVxyXG5cclxuICAgIC8vQ29weVdheXBvaW50KHcxOiBXYXlwb2ludCwgdzI6IFdheXBvaW50KSB7XHJcbiAgICAvLyAgICB3Mi5XYXlwb2ludE51bWJlcih3MS5XYXlwb2ludE51bWJlcigpKTtcclxuICAgIC8vICAgIHcyLkxhdGl0dWRlKHcxLkxhdGl0dWRlKCkpO1xyXG4gICAgLy8gICAgdzIuTG9uZ2l0dWRlKHcxLkxvbmdpdHVkZSgpKTtcclxuICAgIC8vICAgIHcyLk5hbWUodzEuTmFtZSgpKTtcclxuICAgIC8vICAgIHcyLkRlc2NyaXB0aW9uKHcxLkRlc2NyaXB0aW9uKCkpO1xyXG4gICAgLy99XHJcblxyXG4gICAgQWRkUG9seWxpbmUod2F5cG9pbnQ/OiBXYXlwb2ludCk6IEwuUG9seWxpbmU7XHJcbiAgICBBZGRQb2x5bGluZSh3YXlwb2ludHM/OiBXYXlwb2ludFtdKTogTC5Qb2x5bGluZTtcclxuICAgIEFkZFBvbHlsaW5lKGFyZz8pOiBMLlBvbHlsaW5lIHtcclxuICAgICAgICBjb25zdCBwb2x5bGluZSA9IG5ldyBMLlBvbHlsaW5lKFtdKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuUG9seWxpbmVzLnB1c2gocG9seWxpbmUpO1xyXG4gICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKVxyXG4gICAgICAgICAgICBwb2x5bGluZS5hZGRUbyh0aGlzLk1hcCk7XHJcbiAgICAgICAgcG9seWxpbmUuV2F5cG9pbnRzID0gbmV3IEFycmF5KCk7XHJcbiAgICAgICAgaWYgKGFyZyAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICBpZiAoYXJnIGluc3RhbmNlb2YgV2F5cG9pbnQpXHJcbiAgICAgICAgICAgICAgICAoYXJnIGFzIFdheXBvaW50KS5BZGRUb1BvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgd2F5cG9pbnQgb2YgYXJnIGFzIFdheXBvaW50W10pIHtcclxuICAgICAgICAgICAgICAgICAgICB3YXlwb2ludC5BZGRUb1BvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICBwb2x5bGluZS5hZGRFdmVudExpc3RlbmVyKFwibW91c2VvdmVyXCIsXHJcbiAgICAgICAgICAgICgpID0+IHtcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZSA9IHBvbHlsaW5lO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICByZXR1cm4gcG9seWxpbmU7XHJcbiAgICB9XHJcblxyXG5cclxuICAgIE1hcE1vZGUgPSBrby5vYnNlcnZhYmxlPE1hcE1vZGU+KCk7XHJcbiAgICBEcmF3aW5nTGF0TG5nOiBMLkxhdExuZztcclxuICAgIERyYXdpbmdTb3VyY2VXYXlwb2ludDogV2F5cG9pbnQ7XHJcbiAgICBEcmF3aW5nVGFyZ2V0V2F5cG9pbnQ6IFdheXBvaW50O1xyXG4gICAgUmVtb3ZlUG9seWxpbmUgPSAocG9seWxpbmU6IEwuUG9seWxpbmUpID0+IHtcclxuICAgICAgICB0aGlzLk1hcC5yZW1vdmVMYXllcihwb2x5bGluZSk7XHJcbiAgICAgICAgdGhpcy5EcmF3aW5nUG9seWxpbmUgPSB1bmRlZmluZWQ7XHJcbiAgICB9O1xyXG5cclxuICAgIEdldE1hcE1vZGUoKTogTWFwTW9kZSB7XHJcbiAgICAgICAgaWYgKHRoaXMuRHJhd2luZ1BvbHlsaW5lICE9PSB1bmRlZmluZWQgJiYgdGhpcy5EcmF3aW5nTGF0TG5nICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgIHJldHVybiBNYXBNb2RlLlJvdXRlRHJhd2luZztcclxuICAgICAgICByZXR1cm4gdGhpcy5NYXBNb2RlKCk7XHJcbiAgICB9XHJcblxyXG4gICAgR2V0V2F5UG9pbnRCeUlkKGlkOiBudW1iZXIpOiBXYXlwb2ludCB7XHJcbiAgICAgICAgZm9yIChsZXQgd2F5cG9pbnQgb2YgdGhpcy5XYXlwb2ludHMoKSkge1xyXG4gICAgICAgICAgICBpZiAod2F5cG9pbnQuSWQoKSA9PT0gaWQpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gd2F5cG9pbnQ7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGZvciAobGV0IHdheXBvaW50IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICBpZiAod2F5cG9pbnQuSWQoKSA9PT0gaWQpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gd2F5cG9pbnQ7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIHRocm93IGBObyBXYXlwb2ludCB3aXRoIGlkICR7aWR9IGluIG1vZGVsYDtcclxuICAgIH1cclxuXHJcbiAgICBDYWxjdWxhdGVEaXN0YW5jZXMoc3RhcnQgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRIYXJib3VyKCksIHRhcmdldD86IENsaWVudE1vZGVsLldheXBvaW50KSB7XHJcblxyXG4gICAgICAgIGNvbnN0IHdheXBvaW50czogQXJyYXk8V2F5cG9pbnQ+ID0gW3N0YXJ0XTtcclxuICAgICAgICBjb25zdCBjYWxjdWxhdGluZyA9IG5ldyBBcnJheTxXYXlwb2ludERpc3RhbmNlPigpO1xyXG4gICAgICAgIGNvbnN0IGNhbGN1bGF0ZWQgPSBuZXcgQXJyYXk8V2F5cG9pbnREaXN0YW5jZT4oKTtcclxuICAgICAgICBjb25zdCBjYWxjdWxhdGVSb3V0ZSA9IHRhcmdldCAhPT0gdW5kZWZpbmVkO1xyXG4gICAgICAgIGNhbGN1bGF0aW5nLnB1c2gobmV3IFdheXBvaW50RGlzdGFuY2UodW5kZWZpbmVkLCBzdGFydCwgMCwgd2F5cG9pbnRzLCBjYWxjdWxhdGVSb3V0ZSkpO1xyXG4gICAgICAgIC8vZm9yIChsZXQgd2F5cG9pbnQgb2YgbWFwVmlld01vZGVsLldheXBvaW50cygpKSB7XHJcbiAgICAgICAgLy8gICAgd2F5cG9pbnRzLnB1c2gobmV3IFdheXBvaW50RGlzdGFuY2UobnVsbCwgd2F5cG9pbnQsIE51bWJlci5QT1NJVElWRV9JTkZJTklUWSkpO1xyXG4gICAgICAgIC8vfVxyXG4gICAgICAgIC8vZm9yIChsZXQgaGFyYm91ciBvZiBtYXBWaWV3TW9kZWwuSGFyYm91cnMoKSkge1xyXG4gICAgICAgIC8vICAgIGlmIChoYXJib3VyICE9PSBzdGFydCkge1xyXG4gICAgICAgIC8vICAgICAgICB3YXlwb2ludHMucHVzaChuZXcgV2F5cG9pbnREaXN0YW5jZShudWxsLCBoYXJib3VyLCBOdW1iZXIuUE9TSVRJVkVfSU5GSU5JVFkpKTtcclxuICAgICAgICAvLyAgICB9XHJcbiAgICAgICAgLy99XHJcbiAgICAgICAgaWYgKGNhbGN1bGF0ZVJvdXRlKSB7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHdwIG9mIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMoKSkge1xyXG4gICAgICAgICAgICAgICAgd3AuUm91dGVQcmVjZXNzb3IodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBoIG9mIG1hcFZpZXdNb2RlbC5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgICAgICBoLlJvdXRlUHJlY2Vzc29yKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgICAgIHdwLlByZWNlc3Nvcih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGZvciAobGV0IGggb2YgbWFwVmlld01vZGVsLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAgICAgICAgIGguUHJlY2Vzc29yKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICAgICAgd2hpbGUgKGNhbGN1bGF0aW5nLmxlbmd0aCA+IDApIHtcclxuICAgICAgICAgICAgbGV0IG1pbmltYWxEaXN0ID0gTnVtYmVyLlBPU0lUSVZFX0lORklOSVRZO1xyXG4gICAgICAgICAgICBsZXQgbWluaW1hbFdQOiBXYXlwb2ludERpc3RhbmNlO1xyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBjYWxjdWxhdGluZykge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgY1dQIG9mIHdwLkNvbm5lY3RlZFdheVBvaW50cykge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmICgoY2FsY3VsYXRlUm91dGUgPyBjV1AuUm91dGVQcmVjZXNzb3IoKSA6IGNXUC5QcmVjZXNzb3IoKSkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmVtb3ZlRnJvbUFycmF5KHdwLkNvbm5lY3RlZFdheVBvaW50cywgY1dQKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIGlmICh3cC5Db25uZWN0ZWRXYXlQb2ludHMubGVuZ3RoID09PSAwKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgcmVtb3ZlRnJvbUFycmF5KGNhbGN1bGF0aW5nLCB3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgY2FsY3VsYXRlZC5wdXNoKHdwKTtcclxuICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgZGlzdCA9IHdwLkRpc3RhbmNlICsgd3AuQ29ubmVjdGVkV2F5UG9pbnRzWzBdLkxhdExuZy5kaXN0YW5jZVRvKHdwLkxhdExuZyk7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKGRpc3QgPCBtaW5pbWFsRGlzdCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtaW5pbWFsRGlzdCA9IGRpc3Q7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1pbmltYWxXUCA9IHdwO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBpZiAobWluaW1hbFdQICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGNhbGN1bGF0aW5nLnB1c2gobmV3IFdheXBvaW50RGlzdGFuY2UobWluaW1hbFdQLldheXBvaW50LFxyXG4gICAgICAgICAgICAgICAgICAgIG1pbmltYWxXUC5Db25uZWN0ZWRXYXlQb2ludHMuc2hpZnQoKSxcclxuICAgICAgICAgICAgICAgICAgICBtaW5pbWFsRGlzdCxcclxuICAgICAgICAgICAgICAgICAgICB3YXlwb2ludHMsXHJcbiAgICAgICAgICAgICAgICAgICAgY2FsY3VsYXRlUm91dGUpKTtcclxuICAgICAgICAgICAgICAgIC8vaWYgKG1pbmltYWxXUC5XYXlwb2ludCA9PT0gdGFyZ2V0KVxyXG4gICAgICAgICAgICAgICAgLy8gICAgYnJlYWs7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICAgICAgaWYgKGNhbGN1bGF0ZVJvdXRlKVxyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBjYWxjdWxhdGVkKSB7XHJcbiAgICAgICAgICAgICAgICB3cC5XYXlwb2ludC5Sb3V0ZURpc3RhbmNlKE1hdGgucm91bmQod3AuRGlzdGFuY2UgLyAxMDApIC8gMTApO1xyXG4gICAgICAgICAgICAgICAgLy93cC5XYXlwb2ludC5QcmVjZXNzb3Iod3AuUHJlY2Vzc29yKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIGVsc2VcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgY2FsY3VsYXRlZCkge1xyXG4gICAgICAgICAgICAgICAgd3AuV2F5cG9pbnQuRGlzdGFuY2UoTWF0aC5yb3VuZCh3cC5EaXN0YW5jZSAvIDEwMCkgLyAxMCk7XHJcbiAgICAgICAgICAgICAgICAvL3dwLldheXBvaW50LlByZWNlc3Nvcih3cC5QcmVjZXNzb3IpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgcHJpdmF0ZSBoaWdobGlnaHRlZFJvdXRlOiBMLlBvbHlsaW5lO1xyXG4gICAgcHJpdmF0ZSByb3V0ZUZpeGVkID0gZmFsc2U7XHJcbiAgICBwcml2YXRlIHByZXZpb3VzQm91bmRzOiBMLkxhdExuZ0JvdW5kcztcclxuICAgIHByaXZhdGUgbm9SZXZlcnRUb1ByZXZpb3VzQm91bmRzID0gZmFsc2U7XHJcblxyXG4gICAgU2hvd1JvdXRlKGg6IENsaWVudE1vZGVsLldheXBvaW50KSB7XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlRml4ZWQgPSBmYWxzZTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkhpZGVSb3V0ZSgpO1xyXG4gICAgICAgIH1cclxuICAgICAgICBpZiAoaCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICBoID0gKHRoaXMgYXMgYW55KTtcclxuICAgICAgICBpZiAoIShoIGluc3RhbmNlb2YgQ2xpZW50TW9kZWwuSGFyYm91cikpXHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICBjb25zdCBsYXRMbmdzID0gW2guTGF0TG5nXTtcclxuICAgICAgICBsZXQgZGlzdCA9IGguRGlzdGFuY2UoKTtcclxuICAgICAgICBpZiAoZGlzdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICBkaXN0ID0gMDtcclxuICAgICAgICB3aGlsZSAoaC5QcmVjZXNzb3IoKSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIGggPSBoLlByZWNlc3NvcigpO1xyXG4gICAgICAgICAgICBsYXRMbmdzLnB1c2goaC5MYXRMbmcpO1xyXG4gICAgICAgIH1cclxuICAgICAgICBtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZSA9IEwucG9seWxpbmUobGF0TG5ncyk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUuYWRkVG8obWFwVmlld01vZGVsLk1hcCk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUuYmluZExhYmVsKGRpc3QudG9TdHJpbmcoKSArIFwiIGttXCIsIHsgbm9IaWRlOiB0cnVlIH0pO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5GaXRCb3VuZHMobWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUuZ2V0Qm91bmRzKCkpO1xyXG4gICAgfVxyXG5cclxuICAgIEZpdEJvdW5kcyhib3VuZHM6IEwuTGF0TG5nQm91bmRzKSB7XHJcbiAgICAgICAgY29uc3QgbWFwID0gbWFwVmlld01vZGVsLk1hcDtcclxuICAgICAgICBjb25zdCBjdXJyZW50Qm91bmRzID0gbWFwLmdldEJvdW5kcygpO1xyXG4gICAgICAgIGlmICghY3VycmVudEJvdW5kcy5jb250YWlucyhib3VuZHMpKSB7XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyA9IGN1cnJlbnRCb3VuZHM7XHJcbiAgICAgICAgICAgIG1hcC5maXRCb3VuZHMoYm91bmRzKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgSGlkZVJvdXRlKGZvcmNlID0gZmFsc2UpIHtcclxuICAgICAgICBpZiAoKCFtYXBWaWV3TW9kZWwucm91dGVGaXhlZCB8fCBmb3JjZSkgJiYgbWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVGaXhlZCA9IGZhbHNlO1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlKTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgIGlmICghbWFwVmlld01vZGVsLm5vUmV2ZXJ0VG9QcmV2aW91c0JvdW5kcyAmJiBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgdG1wQm91bmRzID0gbWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzO1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgd2luZG93LnNldFRpbWVvdXQoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLk1hcC5maXRCb3VuZHModG1wQm91bmRzKTtcclxuICAgICAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyA9IHRtcEJvdW5kcztcclxuICAgICAgICAgICAgICAgIH0sIDEwMCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgRml4Um91dGUoKSB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlRml4ZWQgPSB0cnVlO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyA9IHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBDcmVhdGVXYXlwb2ludChsYXRMbmc6IEwuTGF0TG5nLCBtYXJrZXJUeXBlOiBNYXJrZXJUeXBlKTogV2F5cG9pbnQge1xyXG4gICAgICAgIGNvbnN0IHdwID0gbmV3IFdheXBvaW50KGxhdExuZywgbWFya2VyVHlwZSwgbWFwVmlld01vZGVsLk1hcCBhcyBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIHRoaXMuSW5pdGlhbGl6ZVdheXBvaW50KHdwLCBtYXJrZXJUeXBlKTtcclxuICAgICAgICByZXR1cm4gd3A7XHJcbiAgICB9XHJcblxyXG4gICAgSW5pdGlhbGl6ZVdheXBvaW50KHdwOiBXYXlwb2ludCwgbWFya2VyVHlwZTogTWFya2VyVHlwZSkge1xyXG4gICAgICAgIHRoaXMuQ3JlYXRlTWFya2VyKG1hcmtlclR5cGUsIHdwKTtcclxuICAgIH1cclxuXHJcbiAgICBDcmVhdGVNYXJrZXIobWFya2VyVHlwZTogTWFya2VyVHlwZSwgd3A6IENsaWVudE1vZGVsLldheXBvaW50KSB7XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4gfHwgbWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5IYXJib3VyKSB7XHJcbiAgICAgICAgICAgIGNvbnN0IG9wdGlvbnM6IEwuTWFya2VyT3B0aW9ucyA9IHtcclxuICAgICAgICAgICAgICAgIGRyYWdnYWJsZTogKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pXHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgICAgIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15KSB7XHJcbiAgICAgICAgICAgICAgICBvcHRpb25zLm9wYWNpdHkgPSAwO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluICYmXHJcbiAgICAgICAgICAgICAgICAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5XYXlwb2ludCB8fCBtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15KSkge1xyXG4gICAgICAgICAgICAgICAgb3B0aW9ucy5pY29uID0gbmV3IEwuSWNvbih7XHJcbiAgICAgICAgICAgICAgICAgICAgaWNvblVybDogXCIvaW1hZ2VzL3dheXBvaW50aGFuZGxlLnBuZ1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIGljb25TaXplOiBuZXcgTC5Qb2ludCgxMCwgMTAsIHRydWUpLFxyXG4gICAgICAgICAgICAgICAgICAgIGNsYXNzTmFtZTogXCJ3YXlwb2ludFwiXHJcbiAgICAgICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pIHtcclxuICAgICAgICAgICAgICAgIG9wdGlvbnMuY29udGV4dG1lbnUgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgb3B0aW9ucy5jb250ZXh0bWVudUluaGVyaXRJdGVtcyA9IGZhbHNlO1xyXG4gICAgICAgICAgICAgICAgaWYgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuSGFyYm91cikge1xyXG4gICAgICAgICAgICAgICAgICAgIG9wdGlvbnMuY29udGV4dG1lbnVJdGVtcyA9IFtcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdGV4dDogXCJCZWFyYmVpdGVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0OiB3cCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrOiBmdW5jdGlvbiAoKSB7IG1hcFZpZXdNb2RlbC5FZGl0aW5nSGFyYm91cih0aGlzKSB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0sXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRleHQ6IFwiTMO2c2NoZW5cIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnRleHQ6IHdwLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2s6IGZ1bmN0aW9uICgpIHsgbWFwVmlld01vZGVsLkRlbGV0aW5nSGFyYm91cih0aGlzKSB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICBdO1xyXG4gICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICBvcHRpb25zLmNvbnRleHRtZW51SXRlbXMgPSBbXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRleHQ6IFwiQmVhcmJlaXRlblwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY29udGV4dDogd3AsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjYWxsYmFjazogZnVuY3Rpb24gKCkgeyBtYXBWaWV3TW9kZWwuRWRpdGluZ1dheXBvaW50KHRoaXMpIH1cclxuICAgICAgICAgICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdGV4dDogXCJMw7ZzY2hlblwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY29udGV4dDogd3AsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjYWxsYmFjazogZnVuY3Rpb24gKCkgeyBtYXBWaWV3TW9kZWwuRGVsZXRpbmdXYXlwb2ludCh0aGlzKSB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICBdO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICBjb25zdCBtYXJrZXIgPSBuZXcgTC5NYXJrZXIod3AuTGF0TG5nLCBvcHRpb25zKTtcclxuICAgICAgICAgICAgbWFya2VyLmFkZFRvKHRoaXMuTWFwKTtcclxuICAgICAgICAgICAgbWFya2VyLldheXBvaW50ID0gd3A7XHJcbiAgICAgICAgICAgIHdwLm1hcmtlciA9IG1hcmtlcjtcclxuICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pIHtcclxuICAgICAgICAgICAgICAgIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15KVxyXG4gICAgICAgICAgICAgICAgICAgIG1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwibW91c2VvdXRcIiwgKGUpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKGUudGFyZ2V0LldheXBvaW50LklzRHVtbXkoKSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgfSlcclxuICAgICAgICAgICAgICAgIG1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwiZHJhZ1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIChlOiBMLkxlYWZsZXRNb3VzZUV2ZW50KSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHdwLlNldExhdExuZyh3cC5tYXJrZXIuZ2V0TGF0TG5nKCkpO1xyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgaWYgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuV2F5cG9pbnQgfHwgbWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRNYXJrZXJzLnB1c2god3AubWFya2VyKTtcclxuICAgICAgICAgICAgICAgICAgICB3cC5tYXJrZXIuUG9pbnQgPSBtYXBWaWV3TW9kZWwuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQod3AuTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHdwLm1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwiY2xpY2tcIixcclxuICAgICAgICAgICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAod3AuSXNEdW1teSgpKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzLnB1c2god3ApO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgd3AuY29udmVydEZyb21EdW1teUhhbmRsZSgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuR2V0TWFwTW9kZSgpID09PSBNYXBNb2RlLlJvdXRlRHJhd2luZykge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgaWYgKCF3cC5Jc0luUG9seWxpbmUobWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBTZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAuQ29ubmVjdCh3cC5JZCgpLCBtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lLldheXBvaW50c1swXS5JZCgpKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICB3cC5BZGRUb1BvbHlsaW5lKG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21Qb2x5bGluZShtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lLCBtYXBWaWV3TW9kZWwuRHJhd2luZ0xhdExuZyk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgYWRkRHVtbXlIYW5kbGUobWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ0xhdExuZyA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgcmVtb3ZlUG9seWxpbmUobWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ0xhdExuZyA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgd3AubWFya2VyLmFkZEV2ZW50TGlzdGVuZXIoXCJkYmxjbGlja1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIChlOiBMLkxlYWZsZXRNb3VzZUV2ZW50KSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUgPSBtYXBWaWV3TW9kZWwuQWRkUG9seWxpbmUod3ApO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ0xhdExuZyA9IG5ldyBMLkxhdExuZyhlLmxhdGxuZy5sYXQsIGUubGF0bG5nLmxuZyk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUuYWRkTGF0TG5nKG1hcFZpZXdNb2RlbC5EcmF3aW5nTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15KVxyXG4gICAgICAgICAgICAgICAgICAgIHdwLm1hcmtlci5hZGRPbmVUaW1lRXZlbnRMaXN0ZW5lcihcImRyYWdcIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHdwLmNvbnZlcnRGcm9tRHVtbXlIYW5kbGUoKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMucHVzaCh3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgLy9lbHNlIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLldheXBvaW50KSB7XHJcbiAgICAgICAgICAgICAgICAvLyAgICB3cC5OYW1lKGBXZWdwdW5rdCAke21hcFZpZXdNb2RlbC5XYXlwb2ludHMoKS5sZW5ndGggKyAxfWApO1xyXG4gICAgICAgICAgICAgICAgLy99XHJcbiAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcImRyYWdlbmRcIixcclxuICAgICAgICAgICAgICAgICAgICAoZTogTC5MZWFmbGV0TW91c2VFdmVudCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB3cC5TYXZlVG9TZXJ2ZXIoKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfSBlbHNlIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkhhcmJvdXIpIHtcclxuICAgICAgICAgICAgICAgIHdwLm1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwibW91c2VvdmVyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cigpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuU2hvd1JvdXRlKHdwKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIHdwLm1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwiY2xpY2tcIiwgKCkgPT4gbWFwVmlld01vZGVsLlNlbGVjdGVkSGFyYm91cih3cCBhcyBDbGllbnRNb2RlbC5IYXJib3VyKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgQ3JlYXRlSGFyYm91cihuYW1lOiBzdHJpbmcsIGxhdExuZzogTC5MYXRMbmcpIHtcclxuICAgICAgICBjb25zdCBoID0gbmV3IEhhcmJvdXIobmFtZSwgbGF0TG5nLCB0aGlzLk1hcCBhcyBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIHRoaXMuSW5pdGlhbGl6ZVdheXBvaW50KGgsIE1hcmtlclR5cGUuSGFyYm91cik7XHJcbiAgICAgICAgcmV0dXJuIGg7XHJcbiAgICB9XHJcblxyXG4gICAgU2F2ZUhhcmJvdXIoKSB7XHJcbiAgICAgICAgY29uc3QgaGFyYm91cjogQ2xpZW50TW9kZWwuSGFyYm91ciA9IHRoaXMgYXMgYW55O1xyXG4gICAgICAgIGlmIChoYXJib3VyLklkKCkgPT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuSGFyYm91cnMucHVzaChoYXJib3VyKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgaGFyYm91ci5TYXZlVG9TZXJ2ZXIoKVxyXG4gICAgICAgICAgICAuZG9uZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRWRpdGluZ0hhcmJvdXIodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgRGVsZXRlSGFyYm91cigpIHtcclxuICAgICAgICB2YXIgaCA9IG1hcFZpZXdNb2RlbC5EZWxldGluZ0hhcmJvdXIoKTtcclxuICAgICAgICBTZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuRGlzY29ubmVjdChoLklkKCkpXHJcbiAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgIGguRGVsZXRlT25TZXJ2ZXIoKVxyXG4gICAgICAgICAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaC5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5IYXJib3Vycy5yZW1vdmUoaCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EZWxldGluZ0hhcmJvdXIodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgU2F2ZVdheXBvaW50KCkge1xyXG4gICAgICAgIGNvbnN0IHdheXBvaW50OiBDbGllbnRNb2RlbC5XYXlwb2ludCA9IHRoaXMgYXMgYW55O1xyXG4gICAgICAgIHdheXBvaW50LlNhdmVUb1NlcnZlcigpXHJcbiAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5FZGl0aW5nV2F5cG9pbnQodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgRGVsZXRlV2F5cG9pbnQoKSB7XHJcbiAgICAgICAgdmFyIHdwID0gbWFwVmlld01vZGVsLkRlbGV0aW5nV2F5cG9pbnQoKTtcclxuICAgICAgICBTZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAuRGlzY29ubmVjdCh3cC5JZCgpKVxyXG4gICAgICAgICAgICAuZG9uZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICB3cC5EZWxldGVPblNlcnZlcigpXHJcbiAgICAgICAgICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB3cC5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMucmVtb3ZlKHdwKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRlbGV0aW5nV2F5cG9pbnQodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9O1xyXG5cclxuXHJcbiAgICBTYXZlSm9iKCkge1xyXG4gICAgICAgIGNvbnN0IGpvYjogQ2xpZW50TW9kZWwuSm9iID0gdGhpcyBhcyBhbnk7XHJcbiAgICAgICAgY29uc3QgbmV3Sm9iID0gam9iLklkKCkgPT09IHVuZGVmaW5lZDtcclxuICAgICAgICBqb2IuU2F2ZVRvU2VydmVyKClcclxuICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKG5ld0pvYikge1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Kb2JzLnB1c2gobWFwVmlld01vZGVsLkVkaXRpbmdKb2IoKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5FZGl0aW5nSm9iKCkuU3VwZXJKb2JJZCgpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5HZXRKb2JCeUlkKG1hcFZpZXdNb2RlbC5FZGl0aW5nSm9iKCkuU3VwZXJKb2JJZCgpKS5TdWJKb2JzLnB1c2gobWFwVmlld01vZGVsLkVkaXRpbmdKb2IoKSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRWRpdGluZ0pvYih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgIH1cclxuXHJcbiAgICBEZWxldGVKb2IoKSB7XHJcbiAgICAgICAgY29uc3Qgam9iID0gbWFwVmlld01vZGVsLkRlbGV0aW5nSm9iKCk7XHJcbiAgICAgICAgam9iLkRlbGV0ZU9uU2VydmVyKClcclxuICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkpvYnMucmVtb3ZlKGpvYik7XHJcbiAgICAgICAgICAgICAgICBpZiAoam9iLlN1cGVySm9iSWQoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5HZXRKb2JCeUlkKGpvYi5TdXBlckpvYklkKCkpLlN1YkpvYnMucmVtb3ZlKGpvYik7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRGVsZXRpbmdKb2IodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG5cclxuICAgIERyYXdpbmdQb2x5bGluZTogTC5Qb2x5bGluZTtcclxuICAgIFBvbHlsaW5lcyA9IG5ldyBBcnJheTxMLlBvbHlsaW5lPigpO1xyXG4gICAgRWRpdGluZ0hhcmJvdXIgPSBrby5vYnNlcnZhYmxlPEhhcmJvdXI+KCk7XHJcbiAgICBEZWxldGluZ0hhcmJvdXIgPSBrby5vYnNlcnZhYmxlPEhhcmJvdXI+KCk7XHJcbiAgICBFZGl0aW5nV2F5cG9pbnQgPSBrby5vYnNlcnZhYmxlPFdheXBvaW50PigpO1xyXG4gICAgRGVsZXRpbmdXYXlwb2ludCA9IGtvLm9ic2VydmFibGU8V2F5cG9pbnQ+KCk7XHJcbiAgICBEZWxldGluZ0pvYiA9IGtvLm9ic2VydmFibGU8Sm9iPigpO1xyXG4gICAgRWRpdGluZ0pvYiA9IGtvLm9ic2VydmFibGU8Sm9iPigpO1xyXG4gICAgV2F5cG9pbnRNYXJrZXJzID0gbmV3IEFycmF5KCk7XHJcbiAgICBIb3ZlcmVkUG9seWluZTogTC5Qb2x5bGluZTtcclxuXHJcbn1cclxuXHJcbnZhciBtYXBWaWV3TW9kZWwgPSBuZXcgTWFwVmlld01vZGVsKE1hcE1vZGUuVmlldyk7XHJcbmtvLmFwcGx5QmluZGluZ3MobWFwVmlld01vZGVsKTtcclxudmFyIGRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcgPSBmYWxzZTtcclxuXHJcbnZhciBkcm9wem9uZU1vZGFsID0gJChcIiNkcm9wem9uZU1vZGFsXCIpO1xyXG52YXIgZWRpdGluZ0hhcmJvdXJNb2RhbCA9ICQoXCIjZWRpdGluZ0hhcmJvdXJNb2RhbFwiKTtcclxudmFyIGRlbGV0aW5nSGFyYm91ck1vZGFsID0gJChcIiNkZWxldGluZ0hhcmJvdXJNb2RhbFwiKTtcclxudmFyIGVkaXRpbmdXYXlwb2ludE1vZGFsID0gJChcIiNlZGl0aW5nV2F5cG9pbnRNb2RhbFwiKTtcclxudmFyIGRlbGV0aW5nV2F5cG9pbnRNb2RhbCA9ICQoXCIjZGVsZXRpbmdXYXlwb2ludE1vZGFsXCIpO1xyXG52YXIgZGVsZXRpbmdKb2JNb2RhbCA9ICQoXCIjZGVsZXRpbmdKb2JNb2RhbFwiKTtcclxudmFyIGVkaXRpbmdKb2JNb2RhbCA9ICQoXCIjZWRpdGluZ0pvYk1vZGFsXCIpO1xyXG52YXIgam9iT3ZlcnZpZXdNb2RhbCA9ICQoXCIjam9iT3ZlcnZpZXdNb2RhbFwiKTtcclxudmFyIGRyb3B6b25lOiBEcm9wem9uZTtcclxudmFyIGhhc0RyYWcgPSBmYWxzZTtcclxudmFyIHVwbG9hZE1vZGFsVmlzaWJsZSA9IGZhbHNlO1xyXG52YXIgcHN3cCA9ICQoXCIucHN3cFwiKVswXTtcclxuXHJcbnZhciBsZWZ0U2lkZWJhciA9IG5ldyBTaWRlYmFyKCQoXCIjbGVmdFNpZGViYXJcIikpO1xyXG52YXIgcmlnaHRTaWRlYmFyID0gbmV3IFNpZGViYXIoJChcIiNyaWdodFNpZGViYXJcIikpO1xyXG52YXIgYm90dG9tU2lkZWJhciA9IG5ldyBTaWRlYmFyKCQoXCIjYm90dG9tU2lkZWJhclwiKSk7XHJcbnZhciBoYXJib3VySW5mbyA9ICQoXCIjaGFyYm91ckluZm9cIik7XHJcbkRyb3B6b25lLm9wdGlvbnMuZHJvcHpvbmUgPVxyXG4gICAge1xyXG4gICAgICAgIGFjY2VwdGVkRmlsZXM6IFwiaW1hZ2UvanBlZyxpbWFnZS9wbmdcIixcclxuICAgICAgICBkaWN0SW52YWxpZEZpbGVUeXBlOiBcIkRpZXNlciBEYXRlaXR5cCB3aXJkIG5pY2h0IHVudGVyc3TDvHR6dFwiLFxyXG4gICAgICAgIGRpY3REZWZhdWx0TWVzc2FnZTogXCJEYXRlaWVuIGhpZXIgYWJsZWdlblwiLFxyXG4gICAgICAgIGluaXQoKSB7XHJcbiAgICAgICAgICAgIGRyb3B6b25lID0gdGhpcztcclxuICAgICAgICAgICAgZHJvcHpvbmUub24oXCJzdWNjZXNzXCIsXHJcbiAgICAgICAgICAgICAgICAoZSwgZGF0YTogU2VydmVyTW9kZWwuSW1hZ2UpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICB2YXIgaW1hZ2UgPSBuZXcgQ2xpZW50TW9kZWwuSW1hZ2UoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShkYXRhKTtcclxuICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSW1hZ2VzLnB1c2goaW1hZ2UpO1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5HZXRBbGJ1bUJ5SWQoaW1hZ2UuUGFyZW50QWxidW1JZCgpKS5JbWFnZXMucHVzaChpbWFnZSk7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgZHJvcHpvbmUub24oXCJxdWV1ZWNvbXBsZXRlXCIsXHJcbiAgICAgICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKGRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGRyb3B6b25lTW9kYWwubW9kYWwoXCJoaWRlXCIpO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIGRyb3B6b25lLm9uKFwiZHJhZ292ZXJcIixcclxuICAgICAgICAgICAgICAgICgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBoYXNEcmFnID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuICAgIH07XHJcblxyXG5kb2N1bWVudC5vbmRyYWdlbnRlciA9XHJcbiAgICAoZTogRHJhZ0V2ZW50KSA9PiB7XHJcbiAgICAgICAgaWYgKCF1cGxvYWRNb2RhbFZpc2libGUgJiZcclxuICAgICAgICAgICAgIWhhc0RyYWcgJiZcclxuICAgICAgICAgICAgIWRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcgJiZcclxuICAgICAgICAgICAgZHJvcHpvbmVNb2RhbC5pcyhcIjpub3QoLmluKVwiKSAmJlxyXG4gICAgICAgICAgICBlLmRhdGFUcmFuc2Zlci50eXBlc1swXSA9PT0gXCJGaWxlc1wiICYmXHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5TZWxlY3RlZEhhcmJvdXIoKSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIGRyb3B6b25lTW9kYWwubW9kYWwoXCJzaG93XCIpO1xyXG4gICAgICAgICAgICB1cGxvYWRNb2RhbFZpc2libGUgPSB0cnVlO1xyXG4gICAgICAgICAgICBkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnID0gdHJ1ZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGhhc0RyYWcgPSB0cnVlO1xyXG4gICAgICAgIGUucHJldmVudERlZmF1bHQoKTtcclxuICAgICAgICBlLnN0b3BQcm9wYWdhdGlvbigpO1xyXG4gICAgfTtcclxuZG9jdW1lbnQub25kcmFnb3ZlciA9XHJcbiAgICAoZTogRHJhZ0V2ZW50KSA9PiB7XHJcbiAgICAgICAgaGFzRHJhZyA9IHRydWU7XHJcbiAgICB9O1xyXG5kb2N1bWVudC5vbmRyYWdsZWF2ZSA9XHJcbiAgICAoZTogRHJhZ0V2ZW50KSA9PiB7XHJcbiAgICAgICAgaWYgKHVwbG9hZE1vZGFsVmlzaWJsZSAmJiBoYXNEcmFnICYmIGRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcgJiYgZHJvcHpvbmUuZ2V0UXVldWVkRmlsZXMoKS5sZW5ndGggPT09IDAgfHxcclxuICAgICAgICAgICAgZHJvcHpvbmUuZ2V0VXBsb2FkaW5nRmlsZXMoKS5sZW5ndGggPT09IDApIHtcclxuICAgICAgICAgICAgaGFzRHJhZyA9IGZhbHNlO1xyXG4gICAgICAgICAgICB3aW5kb3cuc2V0VGltZW91dCgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAoIWhhc0RyYWcpIHtcclxuICAgICAgICAgICAgICAgICAgICBkcm9wem9uZU1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgICAgICAgICB1cGxvYWRNb2RhbFZpc2libGUgPSBmYWxzZTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgIDEwMDApO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZS5wcmV2ZW50RGVmYXVsdCgpO1xyXG4gICAgICAgIGUuc3RvcFByb3BhZ2F0aW9uKCk7XHJcbiAgICB9O1xyXG5kcm9wem9uZU1vZGFsLm9uKFwiaGlkZS5icy5tb2RhbFwiLFxyXG4gICAgZSA9PiB7XHJcbiAgICAgICAgaWYgKGRyb3B6b25lLmdldFF1ZXVlZEZpbGVzKCkubGVuZ3RoID4gMCB8fCBkcm9wem9uZS5nZXRVcGxvYWRpbmdGaWxlcygpLmxlbmd0aCA+IDApIHtcclxuICAgICAgICAgICAgZS5wcmV2ZW50RGVmYXVsdCgpO1xyXG4gICAgICAgICAgICBlLnN0b3BJbW1lZGlhdGVQcm9wYWdhdGlvbigpO1xyXG4gICAgICAgICAgICBhbGVydChcIkRhcyBGZW5zdGVyIGthbm4gbmljaHQgZ2VzY2hsb3NzZW4gd2VyZGVuLCB3w6RocmVuZCBEYXRlaWVuIGhvY2hnZWxhZGVuIHdlcmRlbi5cIik7XHJcbiAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICBkcm9wem9uZS5yZW1vdmVBbGxGaWxlcygpO1xyXG4gICAgICAgICAgICBkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnID0gZmFsc2U7XHJcbiAgICAgICAgfVxyXG4gICAgfSk7XHJcbnZhciBnYWxsZXJ5OiBQaG90b1N3aXBlPFBob3RvU3dpcGUuT3B0aW9ucz47XHJcblxyXG4kKFwiLm1vZGFsXCIpLm9uKFwiaGlkZGVuLmJzLm1vZGFsXCIsIGZ1bmN0aW9uIChldmVudCkge1xyXG4gICAgJCh0aGlzKS5yZW1vdmVDbGFzcyhcImZ2LW1vZGFsLXN0YWNrXCIpO1xyXG4gICAgJChcImJvZHlcIikuZGF0YShcImZ2X29wZW5fbW9kYWxzXCIsICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiKSAtIDEpO1xyXG59KTtcclxuXHJcblxyXG4kKFwiLm1vZGFsXCIpLm9uKFwic2hvd24uYnMubW9kYWxcIiwgZnVuY3Rpb24gKGV2ZW50KSB7XHJcblxyXG4gICAgLy8ga2VlcCB0cmFjayBvZiB0aGUgbnVtYmVyIG9mIG9wZW4gbW9kYWxzXHJcblxyXG4gICAgaWYgKHR5cGVvZiAoJChcImJvZHlcIikuZGF0YShcImZ2X29wZW5fbW9kYWxzXCIpKSA9PSBcInVuZGVmaW5lZFwiKSB7XHJcbiAgICAgICAgJChcImJvZHlcIikuZGF0YShcImZ2X29wZW5fbW9kYWxzXCIsIDApO1xyXG4gICAgfVxyXG5cclxuXHJcbiAgICAvLyBpZiB0aGUgei1pbmRleCBvZiB0aGlzIG1vZGFsIGhhcyBiZWVuIHNldCwgaWdub3JlLlxyXG5cclxuICAgIGlmICgkKHRoaXMpLmhhc0NsYXNzKFwiZnYtbW9kYWwtc3RhY2tcIikpIHtcclxuICAgICAgICByZXR1cm47XHJcbiAgICB9XHJcblxyXG4gICAgJCh0aGlzKS5hZGRDbGFzcyhcImZ2LW1vZGFsLXN0YWNrXCIpO1xyXG5cclxuICAgICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiLCAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikgKyAxKTtcclxuXHJcbiAgICAkKHRoaXMpLmNzcyhcInotaW5kZXhcIiwgMTA0MCArICgxMCAqICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiKSkpO1xyXG5cclxuICAgICQoXCIubW9kYWwtYmFja2Ryb3BcIikubm90KFwiLmZ2LW1vZGFsLXN0YWNrXCIpXHJcbiAgICAgICAgLmNzcyhcInotaW5kZXhcIiwgMTAzOSArICgxMCAqICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiKSkpO1xyXG5cclxuXHJcbiAgICAkKFwiLm1vZGFsLWJhY2tkcm9wXCIpLm5vdChcImZ2LW1vZGFsLXN0YWNrXCIpXHJcbiAgICAgICAgLmFkZENsYXNzKFwiZnYtbW9kYWwtc3RhY2tcIik7XHJcblxyXG59KTtcclxuXHJcblxyXG4iXSwic291cmNlUm9vdCI6Ii9zb3VyY2UvIn0=
