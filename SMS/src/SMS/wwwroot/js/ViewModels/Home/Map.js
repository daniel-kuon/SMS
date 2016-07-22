/// <reference path="../../clientmodel.ts" />
/// <reference path="../../servermodel.ts" />
/// <reference path="../../serverapi.ts" />
/// <reference path="../../../../typings/browser/definitions/moment/index.d.ts" />
var Waypoint = ClientModel.Waypoint;
var Harbour = ClientModel.Harbour;
var WaypointDistance = ClientModel.WaypointDistance;
var ctrlPressed = false;
if (/Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent)) {
    $("body").addClass("mobile");
}
function renderTime(startDate, endDate) {
    if (startDate instanceof Date)
        return renderTime(endDate.getTime() - startDate.getTime());
    var duration = startDate;
    var time = Math.floor(duration / 60000);
    var mins = (time % 60).toString();
    if (mins.length === 1)
        mins = "0" + mins;
    time = Math.floor(time / 60);
    return time.toString() + ":" + mins;
}
function getMiddle(pol) {
    var start = pol.getLatLngs()[0];
    var end = pol.getLatLngs()[1];
    //if (end === undefined)
    //    return start;
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
        this.HasDetailView = false;
        this.Detail = ko.observable();
        this.Editing = ko.observable();
        this.Deleting = ko.observable();
        this.Delete = function () {
            _this.Deleting()
                .DeleteOnServer()
                .done(function () {
                //this.Dataset.remove(this.Deleting());
                _this.Deleting(undefined);
                if (_this.Detail() !== undefined)
                    _this.Detail(undefined);
                if (_this.Editing() !== undefined)
                    _this.Editing(undefined);
            });
        };
        this.Save = function () {
            if (_this.Parsley !== undefined)
                _this.Parsley.whenValidate()
                    .done(function () {
                    var isNew = _this.Editing().Id() === undefined;
                    _this.Editing()
                        .SaveToServer()
                        .done(function () {
                        //if (isNew)
                        //    this.Dataset.push(this.Editing());
                        _this.Editing(undefined);
                    });
                });
            else {
                var isNew = _this.Editing().Id() === undefined;
                _this.Editing()
                    .SaveToServer()
                    .done(function () {
                    //if (isNew)
                    //    this.Dataset.push(this.Editing());
                    _this.Editing(undefined);
                });
            }
        };
        this.EditingModal = $("#" + editingModalId);
        this.DeletingModal = $("#" + deletingModalId);
        if ($("form:first").length === 1)
            this.Parsley = $("form:first", this.EditingModal).parsley(window.ParsleyConfig);
        this.EditingModal.on("show.bs.modal", function () {
            _this.EditingModalOpen = true;
            if (_this.Editing() === undefined)
                _this.Editing(_this.Factory());
            mapViewModel.AlbumStack.unshift(_this.Editing().Album());
        });
        this.EditingModal.on("shown.bs.modal", function () {
            window.setTimeout(function () { return $("input, select, textarea", _this.EditingModal).first().focus(); }, 200);
        });
        this.EditingModal.on("hidden.bs.modal", function () {
            if (_this.Editing() !== undefined)
                _this.Editing(undefined);
            _this.EditingModalOpen = false;
            mapViewModel.AlbumStack.shift();
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
            if (_this.Editing() !== undefined) {
                _this.Editing().RevertState(true);
            }
        }, this, "beforeChange");
        this.DeletingModal.on("show.bs.modal", function () {
            _this.DeletingModalOpen = true;
            mapViewModel.AlbumStack.unshift(undefined);
        });
        this.DeletingModal.on("hidden.bs.modal", function () {
            if (_this.Deleting() !== undefined)
                _this.Deleting(undefined);
            mapViewModel.AlbumStack.shift();
            _this.DeletingModalOpen = false;
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
            this.HasDetailView = true;
            if (detailModalId instanceof Sidebar) {
                this.DetailSidebar = detailModalId;
                this.Detail.subscribe(function (entity) {
                    if (entity === undefined && _this.DetailSidebar.IsActiv()) {
                        mapViewModel.AlbumStack.shift();
                        _this.DetailSidebar.Hide();
                    }
                    else if (entity !== undefined && !_this.DetailSidebar.IsActiv()) {
                        _this.DetailSidebar.Show();
                        mapViewModel.AlbumStack.unshift(entity.Album());
                    }
                });
            }
            else {
                this.DetailModal = $("#" + detailModalId);
                this.Detail.subscribe(function (entity) {
                    if (entity === undefined && _this.DetailModalOpen) {
                        _this.DetailModal.modal("hide");
                    }
                    else if (entity !== undefined && !_this.DetailModalOpen) {
                        _this.DetailModal.modal("show");
                    }
                });
                this.DetailModal.on("show.bs.modal", function () {
                    _this.DetailModalOpen = true;
                    mapViewModel.AlbumStack.unshift(_this.Detail().Album());
                });
                this.DetailModal.on("hide.bs.modal", function () {
                    _this.DetailModalOpen = false;
                    mapViewModel.AlbumStack.shift();
                });
            }
        }
    }
    return EditingHelper;
}());
var MapViewModel = (function () {
    function MapViewModel(mapMode) {
        var _this = this;
        this.IsLoggedIn = ko.observable(false);
        this.routePolyline = ko.observable();
        this.IsLastTakInRoute = ko.computed({
            read: function () {
                var trip = mapViewModel.TripHelper.Editing();
                var h = mapViewModel.HarbourHelper.Detail();
                return trip !== undefined && h !== undefined && trip.Tacks()[trip.Tacks().length - 1].Start() === h;
            },
            deferEvaluation: true
        });
        this.GetRouteDistance = ko.computed({
            read: function () {
                var distance = 0;
                for (var _i = 0, _a = mapViewModel.TripHelper.Editing().Tacks(); _i < _a.length; _i++) {
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
        this.WifisLoaded = false;
        this.ContentPagesLoaded = false;
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
        this.Wifis = ko.observableArray();
        this.ContentPages = ko.observableArray();
        this.WaypointHelper = new EditingHelper("editingWaypointModal", "deletingWaypointModal", function () { return _this.CreateWaypoint(MarkerType.Waypoint); }, this.Waypoints);
        this.HarbourHelper = new EditingHelper("editingHarbourModal", "deletingHarbourModal", function () { return _this.CreateHarbour(); }, this.Harbours, rightSidebar);
        this.PersonHelper = new EditingHelper("editingPersonModal", "deletingPersonModal", function () { return new ClientModel.Person(); }, this.Persons);
        this.JobHelper = new EditingHelper("editingJobModal", "deletingJobModal", function () { return new ClientModel.Job(); }, this.Jobs);
        this.TripHelper = new EditingHelper("editingTripModal", "deletingTripModal", function () { return new ClientModel.Trip(); }, this.Trips);
        this.AddressHelper = new EditingHelper("editingAddressModal", "deletingAddressModal", function () { return new ClientModel.Address(); }, this.Addresses);
        this.ImageHelper = new EditingHelper("editingImageModal", "deletingImageModal", function () { return new ClientModel.Image(); }, this.Images);
        this.TackHelper = new EditingHelper("editingTackModal", "deletingTackModal", function () { return new ClientModel.Tack(); }, this.Tacks);
        this.LocationHelper = new EditingHelper("editingLocationModal", "deletingLocationModal", function () { return new ClientModel.Location(); }, this.Locations);
        this.SupermarketHelper = new EditingHelper("editingSupermarketModal", "deletingSupermarketModal", function () { return new ClientModel.Supermarket(); }, this.Supermarkets);
        this.RestaurantHelper = new EditingHelper("editingRestaurantModal", "deletingRestaurantModal", function () { return new ClientModel.Restaurant(); }, this.Restaurants);
        this.LogBookEntryHelper = new EditingHelper("editingLogBookEntryModal", "deletingLogBookEntryModal", function () {
            var logBookEntry = new ClientModel.LogBookEntry();
            if (_this.LogBookEntries().length > 0) {
                var lastEntry = _this.LogBookEntries()[0];
                for (var _i = 0, _a = _this.LogBookEntries(); _i < _a.length; _i++) {
                    var entry = _a[_i];
                    if (new Date(entry.EndDate()) > new Date(lastEntry.EndDate()))
                        lastEntry = entry;
                }
                logBookEntry.Start(lastEntry.End());
                logBookEntry.MotorHoursStart(lastEntry.MotorHoursEnd());
                logBookEntry.LogStart(lastEntry.LogEnd());
                if (lastEntry.End().Name() !== "Lippe")
                    logBookEntry.Persons(lastEntry.Persons().slice());
            }
            return logBookEntry;
        }, this.LogBookEntries, "detailedLogBookEntryModal");
        this.ContentPageHelper = new EditingHelper("editingContentPageModal", "deletingContentPageModal", function () { return new ClientModel.ContentPage(); }, this.ContentPages, "detailedContentPageModal");
        this.WifiHelper = new EditingHelper("editingWifiModal", "deletingWifiModal", function () {
            var w = new ClientModel.Wifi();
            w.HarbourId(mapViewModel.HarbourHelper.Detail().Id());
            return w;
        }, this.Wifis, "detailWifiModal");
        this.HarboursByName = ko.computed(function () { return _this.Harbours.sort(function (h1, h2) { return h1.Name() > h2.Name() ? 1 : -1; })(); });
        this.HarboursByDistance = ko.computed(function () { return _this.Harbours.sort(function (h1, h2) { return h1.Distance() - h2.Distance(); })(); });
        this.LogBookEntriesByStartDate = ko
            .computed(function () { return _this.LogBookEntries
            .sort(function (l1, l2) { return Date.parse(l1.StartDate()) - Date.parse(l2.StartDate()); })(); });
        this.RemoveHarbour = function () {
            mapViewModel.HarbourHelper.Detail().DeleteOnServer();
        };
        this.RemoveWaypoint = function () {
            mapViewModel.WaypointHelper.Detail().DeleteOnServer();
        };
        this.MapMode = ko.observable();
        this.RemovePolyline = function (polyline) {
            _this.Map.removeLayer(polyline);
            _this.DrawingPolyline = undefined;
        };
        this.routeFixed = false;
        this.noRevertToPreviousBounds = false;
        this.Polylines = new Array();
        this.WaypointMarkers = new Array();
        this.HarboursToSelect = ko.computed(function () {
            return _this.HarboursByName().concat([{ Name: "Neuer Hafen...", IsDummy: true }]);
        });
        this.ProcessHarbourSelectOptions = function (option, item) {
            if (item !== undefined && item !== null && item.IsDummy === true) {
                option.value = "filled";
                var context_1 = ko.contextFor(option);
                var select = $(option).parent();
                if (select.data("new-change-handler") === undefined)
                    select.data("new-change-handler", select.change(function () {
                        if ($(option).is(":selected")) {
                            var harbour_1 = _this.CreateHarbour();
                            _this.HarbourHelper.Editing(harbour_1);
                            var subscription_1 = _this.HarbourHelper.Editing.subscribe(function () {
                                if (harbour_1.Id() !== undefined) {
                                    _this.Harbours.push(harbour_1);
                                    context_1.$data.Harbour(harbour_1);
                                }
                                else {
                                    harbour_1.RemoveFromMap();
                                    context_1.$data.Harbour(undefined);
                                }
                                subscription_1.dispose();
                            });
                        }
                    }));
            }
        };
        this.PersonsToSelect = ko.computed(function () {
            return _this.Persons().sort(function (p1, p2) { return p1.FullName() > p2.FullName() ? 1 : -1; })
                .concat([{ FullName: "Neue Person...", IsDummy: true }]);
        });
        this.ProcessPersonSelectOptions = function (option, item) {
            if (item !== undefined && item !== null && item.IsDummy === true) {
                option.value = "filled";
                var context_2 = ko.contextFor(option);
                var select = $(option).parent();
                if (select.data("new-change-handler") === undefined)
                    select.data("new-change-handler", select.change(function () {
                        if ($(option).is(":selected")) {
                            var person_1 = new Person();
                            _this.PersonHelper.Editing(person_1);
                            var subscription_2 = _this.PersonHelper.Editing.subscribe(function () {
                                if (person_1.Id() !== undefined) {
                                    _this.Persons.push(person_1);
                                    context_2.$data.Person(person_1);
                                }
                                else {
                                    context_2.$data.Person(undefined);
                                }
                                subscription_2.dispose();
                            });
                        }
                    }));
            }
        };
        this.AlbumStack = ko.observableArray();
        this.GetPositionForWaypoint = function (waypoint) {
            navigator.geolocation.getCurrentPosition(function (location) {
                waypoint.Latitude(location.coords.latitude);
                waypoint.Longitude(location.coords.longitude);
            }, function () { console.log(arguments); alert("Die Position konnte nicht abgerufen werden"); });
        };
        this.LogBookPager = new Pager(this.LogBookEntries, {
            Columns: [
                new PagerColumn("Start", function (h) { return h.Start().Name; }, { Sorter: PagerColumn.StringSorter(), Visible: false }),
                new PagerColumn("Ziel", function (h) { return h.End().Name; }, { Sorter: PagerColumn.StringSorter(), Width: 200 }),
                new PagerColumn("Datum", function (h) { return h.StartDate; }, {
                    Sorter: PagerColumn.DateSorter(),
                    Renderer: PagerColumn.DateRenderer(),
                    Width: 150,
                    SortMode: SortModes.Descending
                }),
                new PagerColumn("Dauer", function (h) { return h.SaillingTime; }, { Sorter: PagerColumn.StringSorter() }),
                new PagerColumn("Crew", function (h) { return h.Persons; }, {
                    Renderer: PagerColumn.ArrayRenderer("<br />", function (p) { return p.FullName(); }),
                    Width: 150
                }),
                new PagerColumn("Besondere Vorkomnisse", function (h) { return h.SpecialOccurences; })
            ],
            UseResponsiveTable: true,
            UseStripedTable: true,
            EditingHelper: this.LogBookEntryHelper,
            ShowEditDeleteControls: true,
            IdPrefix: "logBookOverview_",
            SpecialActions: [new PagerSpecialAction("Neuer Eintrag", function () { return $("#editingLogBookEntryModal").modal("show"); }, undefined, this.IsLoggedIn)]
        });
        this.HarbourDistancePager = new Pager(ko.computed(function () { return _this.Harbours().slice().filter(function (h) { return h.Distance() > 0; }); }), {
            Columns: [
                new PagerColumn("Name", function (h) { return h.Name; }, { Sorter: PagerColumn.StringSorter() }),
                new PagerColumn("Entfernung", function (h) { return h.Distance; }, {
                    Sorter: PagerColumn.NumberSorter(),
                    Renderer: function (d) { return d + " sm"; },
                    SortMode: SortModes.Ascending
                })
            ],
            EditingHelper: this.HarbourHelper,
            UseResponsiveTable: true,
            UseStripedTable: true,
            UseSmallColumnControls: true,
            ShowColumnSelector: false,
            IdPrefix: "harbourDistance_",
            SpecialColumnActions: [new PagerSpecialColumnAction("Route zeigen", function (h) {
                    _this.ShowRoute(h);
                    _this.FixRoute();
                })]
        });
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
                        mapViewModel.HarbourHelper.Editing(mapViewModel.CreateHarbour("", e.latlng));
                    }
                }
            ]
        };
        this.Map = L.mapbox.map("map", "mapbox.streets", mapOptions);
        this.Map.setView([54.40774166820069, 10.523529052734373], 9);
        L.tileLayer("http://t1.openseamap.org/seamark/{z}/{x}/{y}.png").addTo(this.Map);
        this.LoadData();
        $.get("/Account/IsLoggedIn").done(function (data) { return _this.IsLoggedIn(data); });
        this.ContentPages.subscribe(function (data) {
            var nav = $("#leftNav");
            $(".contentPageLink", nav).remove();
            var _loop_1 = function(cP) {
                $("<li role=\"presentation\" class=\"contentPageLink\"><a href=\"#\">" + cP.Title() + "</a></li>")
                    .click(function () {
                    mapViewModel.ContentPageHelper.Detail(cP);
                    return false;
                })
                    .appendTo(nav);
            };
            for (var _i = 0, data_1 = data; _i < data_1.length; _i++) {
                var cP = data_1[_i];
                _loop_1(cP);
            }
        });
        this.HarbourHelper.Detail.subscribe(function (newHarbour) {
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
        this.HarbourHelper.Editing.subscribe(function (harbour) {
            if (harbour !== undefined && harbour.Id() === undefined)
                mapViewModel.Map.removeLayer(harbour.marker);
        }, this, "beforeChange");
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
            if (mapViewModel
                .HoveredPolyine !==
                undefined &&
                mapViewModel.HoveredPolyine.DummyHandle !== undefined) {
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
        var harbour = mapViewModel.HarbourHelper.Detail();
        tack.Start(harbour);
        trip.Tacks.push(tack);
        mapViewModel.TripHelper.Editing(trip);
        mapViewModel.routePolyline(L.polyline([], {
            color: "#009900"
        }));
        mapViewModel.routePolyline().addTo(mapViewModel.Map);
    };
    MapViewModel.prototype.AddToRoute = function () {
        var trip = mapViewModel.TripHelper.Editing();
        var targetHarbour = mapViewModel.HarbourHelper.Editing();
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
        for (var _i = 0, _a = mapViewModel.TripHelper.Editing().Tacks(); _i < _a.length; _i++) {
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
        var tacks = mapViewModel.TripHelper.Editing().Tacks;
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
        var tacks = mapViewModel.TripHelper.Editing().Tacks;
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
        var tacks = mapViewModel.TripHelper.Editing().Tacks;
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
                var c = d_11[_i];
                _this.Crews.push(c);
            }
            _this.CrewsLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.Wifis.Get()
            .done(function (d) {
            for (var _i = 0, d_12 = d; _i < d_12.length; _i++) {
                var c = d_12[_i];
                _this.Wifis.push(new ClientModel.Wifi().LoadFromServerEntity(c));
            }
            _this.WifisLoaded = true;
            _this.InitializeModel();
        });
        ServerApi.ContentPages.Get()
            .done(function (d) {
            for (var _i = 0, d_13 = d; _i < d_13.length; _i++) {
                var c = d_13[_i];
                _this.ContentPages.push(new ClientModel.ContentPage().LoadFromServerEntity(c));
            }
            _this.ContentPagesLoaded = true;
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
            for (var _i = 0, d_14 = d; _i < d_14.length; _i++) {
                var sEntity = d_14[_i];
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
            this.LogBookEntriesLoaded &&
            this.AlbumImagesLoaded &&
            this.WifisLoaded &&
            this.ContentPagesLoaded) {
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
            for (var _e = 0, _f = this.Locations(); _e < _f.length; _e++) {
                var entity = _f[_e];
                entity.Address(this.GetAddressById(entity.AddressId()));
                this.GetHarbourById(entity.HarbourId()).Locations.push(entity);
            }
            for (var _g = 0, _h = this.AlbumImages(); _g < _h.length; _g++) {
                var entity = _h[_g];
                this.GetAlbumById(entity.AlbumId).Images.push(this.GetImageById(entity.ImageId));
            }
            for (var _j = 0, _k = mapViewModel.WaypointConnections(); _j < _k.length; _j++) {
                var connection = _k[_j];
                var polyline = mapViewModel.AddPolyline([
                    mapViewModel.GetWayPointById(connection.Waypoint1Id), mapViewModel
                        .GetWayPointById(connection.Waypoint2Id)
                ]);
                addDummyHandle(polyline);
            }
            for (var _l = 0, _m = mapViewModel.LogBookEntries(); _l < _m.length; _l++) {
                var entry = _m[_l];
                entry.Start(mapViewModel.GetHarbourById(entry.StartId()));
                entry.End(mapViewModel.GetHarbourById(entry.EndId()));
                entry.Album(mapViewModel.GetAlbumById(entry.AlbumId()));
            }
            for (var _o = 0, _p = mapViewModel.Crews(); _o < _p.length; _o++) {
                var crew = _p[_o];
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
            for (var _q = 0, _r = mapViewModel.Wifis(); _q < _r.length; _q++) {
                var wifi = _r[_q];
                var h = mapViewModel.GetHarbourById(wifi.HarbourId());
                h.Wifis.push(wifi);
                wifi.Harbour(h);
            }
            ko.applyBindings(mapViewModel);
            $("#loadingOverlay").remove();
        }
    };
    MapViewModel.prototype.InitializeMap = function () {
        mapViewModel.HarbourHelper.Detail(undefined);
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
        for (var _e = 0, _f = mapViewModel.Polylines; _e < _f.length; _e++) {
            var p = _f[_e];
            if (p.DummyHandle.marker !== undefined)
                mapViewModel.Map.removeLayer(p.DummyHandle.marker);
            mapViewModel.CreateMarker(MarkerType.Dummy, p.DummyHandle);
        }
        if (mapViewModel.MapMode() === MapMode.Admin) {
            for (var _g = 0, _h = mapViewModel.Polylines; _g < _h.length; _g++) {
                var p = _h[_g];
                p.addTo(mapViewModel.Map);
            }
            mapViewModel.Map.contextmenu.enable();
        }
        else {
            for (var _j = 0, _k = mapViewModel.Polylines; _j < _k.length; _j++) {
                var p = _k[_j];
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
        return undefined;
    };
    MapViewModel.prototype.GetHarbourById = function (id) {
        for (var _i = 0, _a = this.Harbours(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Harbour with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetPersonById = function (id) {
        for (var _i = 0, _a = this.Persons(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Person with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetJobById = function (id) {
        for (var _i = 0, _a = this.Jobs(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Job with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetTripById = function (id) {
        for (var _i = 0, _a = this.Trips(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Trip with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetAddressById = function (id) {
        for (var _i = 0, _a = this.Addresses(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Address with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetImageById = function (id) {
        for (var _i = 0, _a = this.Images(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Image with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetTackById = function (id) {
        for (var _i = 0, _a = this.Tacks(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Tack with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetLogBookEntryById = function (id) {
        for (var _i = 0, _a = this.LogBookEntries(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Tack with id " + id + " found";
        return undefined;
    };
    MapViewModel.prototype.GetAlbumById = function (id) {
        for (var _i = 0, _a = this.Albums(); _i < _a.length; _i++) {
            var entity = _a[_i];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Tack with id " + id + " found";
        return undefined;
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
        for (var _e = 0, _f = this.Restaurants(); _e < _f.length; _e++) {
            var entity = _f[_e];
            if (entity.Id() === id)
                return entity;
        }
        //throw "No Location with id " + id + " found";
        return undefined;
    };
    //SortedLogBookEntries = ko.computed({
    //    read: () => this.LogBookEntries.sort((l1, l2) => {
    //        var t1 = l1.StartDate().getTime();
    //        var t2 = l2.StartDate().getTime();
    //        return t2 - t1;
    //    }),
    //    deferEvaluation: true
    //});
    MapViewModel.prototype.InitGallery = function (item, event) {
        var items = new Array();
        var albumElem = event.target.parentElement;
        var currImage = this;
        for (var _i = 0, _a = mapViewModel.AlbumStack()[0].Images(); _i < _a.length; _i++) {
            var data = _a[_i];
            items.push({
                h: data.Height(),
                w: data.Width(),
                src: data.Path()
            });
        }
        gallery = new PhotoSwipe(pswp, PhotoSwipeUI_Default, items, {
            index: mapViewModel.AlbumStack()[0].Images.indexOf(currImage),
            getThumbBoundsFn: function (index) {
                var elem = $("img", albumElem)[index];
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
        var polyline = new L.Polyline([]);
        //polyline.bindContextMenu(options);
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
        if (start === void 0) { start = mapViewModel.HarbourHelper.Detail(); }
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
            for (var _e = 0, _f = mapViewModel.Waypoints(); _e < _f.length; _e++) {
                var wp = _f[_e];
                wp.Precessor(undefined);
            }
            for (var _g = 0, _h = mapViewModel.Harbours(); _g < _h.length; _g++) {
                var h = _h[_g];
                h.Precessor(undefined);
            }
        }
        while (calculating.length > 0) {
            var minimalDist = Number.POSITIVE_INFINITY;
            var minimalWp = void 0;
            for (var _j = 0, calculating_1 = calculating; _j < calculating_1.length; _j++) {
                var wp = calculating_1[_j];
                for (var _k = 0, _l = wp.ConnectedWayPoints; _k < _l.length; _k++) {
                    var cWp = _l[_k];
                    if ((calculateRoute ? cWp.RoutePrecessor() : cWp.Precessor()) !== undefined)
                        removeFromArray(wp.ConnectedWayPoints, cWp);
                }
                if (wp.ConnectedWayPoints.length === 0) {
                    removeFromArray(calculating, wp);
                    calculated.push(wp);
                }
                else {
                    var dist = wp.Distance + wp.ConnectedWayPoints[0].LatLng.distanceTo(wp.LatLng) / 1.852;
                    if (dist < minimalDist) {
                        minimalDist = dist;
                        minimalWp = wp;
                    }
                }
            }
            if (minimalWp !== undefined) {
                calculating.push(new WaypointDistance(minimalWp.Waypoint, minimalWp.ConnectedWayPoints.shift(), minimalDist, waypoints, calculateRoute));
            }
        }
        if (calculateRoute)
            for (var _m = 0, calculated_1 = calculated; _m < calculated_1.length; _m++) {
                var wp = calculated_1[_m];
                wp.Waypoint.RouteDistance(Math.round(wp.Distance / 100) / 10);
            }
        else
            for (var _o = 0, calculated_2 = calculated; _o < calculated_2.length; _o++) {
                var wp = calculated_2[_o];
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
                // ReSharper disable SuspiciousThisUsage
                if (markerType === MarkerType.Harbour) {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.HarbourHelper.Editing(this); }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.HarbourHelper.Deleting(this); }
                        }
                    ];
                }
                else {
                    options.contextmenuItems = [
                        {
                            text: "Bearbeiten",
                            context: wp,
                            callback: function () { mapViewModel.WaypointHelper.Editing(this); }
                        },
                        {
                            text: "Löschen",
                            context: wp,
                            callback: function () { mapViewModel.WaypointHelper.Deleting(this); }
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
                        if (e.target.Waypoint.IsDummy())
                            mapViewModel.HoveredPolyine = undefined;
                    });
                marker.addEventListener("drag", function () { wp.SetLatLng(wp.marker.getLatLng()); });
                if (markerType === MarkerType.Waypoint || markerType === MarkerType.Dummy) {
                    this.WaypointMarkers.push(wp.marker);
                    wp.marker.Point = mapViewModel.Map.latLngToContainerPoint(wp.LatLng);
                }
                wp.marker.addEventListener("click", function () {
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
                    wp.marker.addOneTimeEventListener("drag", function () {
                        wp.convertFromDummyHandle();
                        mapViewModel.Waypoints.push(wp);
                    });
                //else if (markerType === MarkerType.Waypoint) {
                //    wp.Name(`Wegpunkt ${mapViewModel.Waypoints().length + 1}`);
                //}
                wp.marker.addEventListener("dragend", function () { wp.SaveToServer(); });
            }
            else if (markerType === MarkerType.Harbour) {
                wp.marker.addEventListener("mouseover", function () {
                    if (mapViewModel.HarbourHelper.Detail() !== undefined)
                        mapViewModel.ShowRoute(wp);
                });
                wp.marker.addEventListener("click", function () { return mapViewModel.HarbourHelper.Detail(wp); });
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
    MapViewModel.prototype.SetOptionKey = function (option, item) {
        ko.applyBindingsToNode(option, { attr: { "data-id": item.Id } }, item);
        ko.applyBindingsToNode(option, { attr: { "value": item.Id } }, item);
    };
    ;
    return MapViewModel;
}());
var dropzoneModalOpenedByDrag = false;
var dropzoneModal = $("#dropzoneModal");
var jobOverviewModal = $("#jobOverviewModal");
var personOverviewModal = $("#personOverviewModal");
var dropzone;
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
            dropzone.on("dragover", function () { hasDrag = true; });
        }
    };
document.ondragenter =
    function (e) {
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
document.ondragover = function () { hasDrag = true; };
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
$(".modal")
    .on("hidden.bs.modal", function () {
    $(this).removeClass("fv-modal-stack");
    $("body").data("fv_open_modals", $("body").data("fv_open_modals") - 1);
});
$(".modal")
    .on("shown.bs.modal", function () {
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
    $(".modal-backdrop")
        .not(".fv-modal-stack")
        .css("z-index", 1039 + (10 * $("body").data("fv_open_modals")));
    $(".modal-backdrop")
        .not("fv-modal-stack")
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
        }, function (start) {
            valueAccessor()(start._d.toJSON());
        });
    },
    update: function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
        $(element).data("daterangepicker").setStartDate(moment(valueAccessor()()));
    }
};
window.Parsley.on("form:validate", function (form) {
    if (form.submitEvent === undefined)
        return false;
});
window.Parsley.on("form:submit", function (form) { return false; });
$(document)
    .on("focusin", function (e) {
    if ($(e.target).closest(".mce-window").length) {
        e.stopImmediatePropagation();
    }
});

//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIlZpZXdNb2RlbHMvSG9tZS9NYXAudHMiXSwibmFtZXMiOltdLCJtYXBwaW5ncyI6IkFBQUEsNkNBQTZDO0FBQzdDLDZDQUE2QztBQUM3QywyQ0FBMkM7QUFDM0Msa0ZBQWtGO0FBQ2xGLElBQU8sUUFBUSxHQUFHLFdBQVcsQ0FBQyxRQUFRLENBQUM7QUFDdkMsSUFBTyxPQUFPLEdBQUcsV0FBVyxDQUFDLE9BQU8sQ0FBQztBQUNyQyxJQUFPLGdCQUFnQixHQUFHLFdBQVcsQ0FBQyxnQkFBZ0IsQ0FBQztBQUV2RCxJQUFJLFdBQVcsR0FBRyxLQUFLLENBQUM7QUFFeEIsRUFBRSxDQUFDLENBQUMsZ0VBQWdFLENBQUMsSUFBSSxDQUFDLFNBQVMsQ0FBQyxTQUFTLENBQUMsQ0FBQyxDQUFDLENBQUM7SUFDN0YsQ0FBQyxDQUFDLE1BQU0sQ0FBQyxDQUFDLFFBQVEsQ0FBQyxRQUFRLENBQUMsQ0FBQztBQUNqQyxDQUFDO0FBSUQsb0JBQW9CLFNBQXdCLEVBQUUsT0FBYztJQUN4RCxFQUFFLENBQUMsQ0FBQyxTQUFTLFlBQVksSUFBSSxDQUFDO1FBQzFCLE1BQU0sQ0FBQyxVQUFVLENBQUMsT0FBTyxDQUFDLE9BQU8sRUFBRSxHQUFHLFNBQVMsQ0FBQyxPQUFPLEVBQUUsQ0FBQyxDQUFDO0lBQy9ELElBQU0sUUFBUSxHQUFHLFNBQW1CLENBQUM7SUFDckMsSUFBSSxJQUFJLEdBQUcsSUFBSSxDQUFDLEtBQUssQ0FBQyxRQUFRLEdBQUcsS0FBSyxDQUFDLENBQUM7SUFDeEMsSUFBSSxJQUFJLEdBQUcsQ0FBQyxJQUFJLEdBQUcsRUFBRSxDQUFDLENBQUMsUUFBUSxFQUFFLENBQUM7SUFDbEMsRUFBRSxDQUFDLENBQUMsSUFBSSxDQUFDLE1BQU0sS0FBSyxDQUFDLENBQUM7UUFDbEIsSUFBSSxHQUFHLE1BQUksSUFBTSxDQUFDO0lBQ3RCLElBQUksR0FBRyxJQUFJLENBQUMsS0FBSyxDQUFDLElBQUksR0FBRyxFQUFFLENBQUMsQ0FBQztJQUM3QixNQUFNLENBQUMs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SSxJQUFJLE9BQUEsS0FBSyxFQUFMLENBQUssQ0FBQyxDQUFDO0FBRWhELENBQUMsQ0FBQyxRQUFRLENBQUM7S0FDTixFQUFFLENBQUMsU0FBUyxFQUNiLFVBQVUsQ0FBQztJQUNQLEVBQUUsQ0FBQyxDQUFDLENBQUMsQ0FBQyxDQUFDLENBQUMsTUFBTSxDQUFDLENBQUMsT0FBTyxDQUFDLGFBQWEsQ0FBQyxDQUFDLE1BQU0sQ0FBQyxDQUFDLENBQUM7UUFDNUMsQ0FBQyxDQUFDLHdCQUF3QixFQUFFLENBQUM7SUFDakMsQ0FBQztBQUNMLENBQUMsQ0FBQyxDQUFDIiwiZmlsZSI6IlZpZXdNb2RlbHMvSG9tZS9NYXAuanMiLCJzb3VyY2VzQ29udGVudCI6WyIvLy8gPHJlZmVyZW5jZSBwYXRoPVwiLi4vLi4vY2xpZW50bW9kZWwudHNcIiAvPlxyXG4vLy8gPHJlZmVyZW5jZSBwYXRoPVwiLi4vLi4vc2VydmVybW9kZWwudHNcIiAvPlxyXG4vLy8gPHJlZmVyZW5jZSBwYXRoPVwiLi4vLi4vc2VydmVyYXBpLnRzXCIgLz5cclxuLy8vIDxyZWZlcmVuY2UgcGF0aD1cIi4uLy4uLy4uLy4uL3R5cGluZ3MvYnJvd3Nlci9kZWZpbml0aW9ucy9tb21lbnQvaW5kZXguZC50c1wiIC8+XHJcbmltcG9ydCBXYXlwb2ludCA9IENsaWVudE1vZGVsLldheXBvaW50O1xyXG5pbXBvcnQgSGFyYm91ciA9IENsaWVudE1vZGVsLkhhcmJvdXI7XHJcbmltcG9ydCBXYXlwb2ludERpc3RhbmNlID0gQ2xpZW50TW9kZWwuV2F5cG9pbnREaXN0YW5jZTtcclxuXHJcbnZhciBjdHJsUHJlc3NlZCA9IGZhbHNlO1xyXG5cclxuaWYgKC9BbmRyb2lkfHdlYk9TfGlQaG9uZXxpUGFkfGlQb2R8QmxhY2tCZXJyeXxJRU1vYmlsZXxPcGVyYSBNaW5pL2kudGVzdChuYXZpZ2F0b3IudXNlckFnZW50KSkge1xyXG4gICAgJChcImJvZHlcIikuYWRkQ2xhc3MoXCJtb2JpbGVcIik7XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHJlbmRlclRpbWUoZHVyYXRpb246IG51bWJlcik7XHJcbmZ1bmN0aW9uIHJlbmRlclRpbWUoc3RhcnREYXRlOiBEYXRlLCBlbmREYXRlOiBEYXRlKTtcclxuZnVuY3Rpb24gcmVuZGVyVGltZShzdGFydERhdGU6IERhdGUgfCBudW1iZXIsIGVuZERhdGU/OiBEYXRlKSB7XHJcbiAgICBpZiAoc3RhcnREYXRlIGluc3RhbmNlb2YgRGF0ZSlcclxuICAgICAgICByZXR1cm4gcmVuZGVyVGltZShlbmREYXRlLmdldFRpbWUoKSAtIHN0YXJ0RGF0ZS5nZXRUaW1lKCkpO1xyXG4gICAgY29uc3QgZHVyYXRpb24gPSBzdGFydERhdGUgYXMgbnVtYmVyO1xyXG4gICAgbGV0IHRpbWUgPSBNYXRoLmZsb29yKGR1cmF0aW9uIC8gNjAwMDApO1xyXG4gICAgbGV0IG1pbnMgPSAodGltZSAlIDYwKS50b1N0cmluZygpO1xyXG4gICAgaWYgKG1pbnMubGVuZ3RoID09PSAxKVxyXG4gICAgICAgIG1pbnMgPSBgMCR7bWluc31gO1xyXG4gICAgdGltZSA9IE1hdGguZmxvb3IodGltZSAvIDYwKTtcclxuICAgIHJldHVybiB0aW1lLnRvU3RyaW5nKCkgKyBcIjpcIiArIG1pbnM7XHJcbn1cclxuXHJcbmZ1bmN0aW9uIGdldE1pZGRsZShwb2w6IEwuUG9seWxpbmUpOiBMLkxhdExuZyB7XHJcbiAgICBjb25zdCBzdGFydCA9IHBvbC5nZXRMYXRMbmdzKClbMF07XHJcbiAgICBjb25zdCBlbmQgPSBwb2wuZ2V0TGF0TG5ncygpWzFdO1xyXG4gICAgLy9pZiAoZW5kID09PSB1bmRlZmluZWQpXHJcbiAgICAvLyAgICByZXR1cm4gc3RhcnQ7XHJcbiAgICByZXR1cm4gbmV3IEwuTGF0TG5nKHN0YXJ0LmxhdCArICgoZW5kLmxhdCAtIHN0YXJ0LmxhdCkgLyAyKSwgc3RhcnQubG5nICsgKChlbmQubG5nIC0gc3RhcnQubG5nKSAvIDIpKTtcclxufVxyXG5cclxuZnVuY3Rpb24gc3BsaXRQb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSkge1xyXG4gICAgaWYgKHBvbHlsaW5lLldheXBvaW50cy5sZW5ndGggPT09IDIgJiYgcG9seWxpbmUuRHVtbXlIYW5kbGUgaW5zdGFuY2VvZiBXYXlwb2ludCkge1xyXG4gICAgICAgIGNvbnN0IHcxID0gcG9seWxpbmUuV2F5cG9pbnRzWzBdO1xyXG4gICAgICAgIGNvbnN0IHcyID0gcG9seWxpbmUuRHVtbXlIYW5kbGU7XHJcbiAgICAgICAgY29uc3QgdzMgPSBwb2x5bGluZS5XYXlwb2ludHNbMV07XHJcbiAgICAgICAgdzIuUmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICB3Mi5BZGRUb1BvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICB3My5SZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgIGFkZER1bW15SGFuZGxlKHBvbHlsaW5lKTtcclxuICAgICAgICBhZGREdW1teUhhbmRsZShtYXBWaWV3TW9kZWwuQWRkUG9seWxpbmUoW3cyLCB3M10pKTtcclxuICAgICAgICByZXR1cm47XHJcbiAgICB9XHJcbiAgICB0aHJvdyBuZXcgRXJyb3IoXCJDYW5ub3Qgc3BsaXQgcG9seWxpbmUuIFBvbHlsaW5lIGhhcyBubyBkdW1teSBoYW5kbGUgb3IgbGVzcyBvciBtb3JlIHRoYW4gMiB3YXlwb2ludHNcIik7XHJcbn1cclxuXHJcblxyXG5mdW5jdGlvbiByZW1vdmVQb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSkge1xyXG4gICAgZm9yIChsZXQgd2F5cG9pbnQgb2YgcG9seWxpbmUuV2F5cG9pbnRzKSB7XHJcbiAgICAgICAgd2F5cG9pbnQuUmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgIH1cclxuICAgIGlmIChwb2x5bGluZS5EdW1teUhhbmRsZSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUuUmVtb3ZlRnJvbVBvbHlsaW5lKHBvbHlsaW5lKTtcclxuICAgICAgICBwb2x5bGluZS5EdW1teUhhbmRsZS5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICB9XHJcbiAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKHBvbHlsaW5lKTtcclxufVxyXG5cclxuZnVuY3Rpb24gYWRkRHVtbXlIYW5kbGUocG9seWxpbmU6IEwuUG9seWxpbmUpIHtcclxuICAgIGlmIChwb2x5bGluZS5EdW1teUhhbmRsZSA9PT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUgPSBtYXBWaWV3TW9kZWwuQ3JlYXRlV2F5cG9pbnQoZ2V0TWlkZGxlKHBvbHlsaW5lKSwgTWFya2VyVHlwZS5EdW1teSk7XHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUuQWRkVG9Qb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICB9XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHJlZHJhd1BvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lKSB7XHJcbiAgICBjb25zdCBtaWRkbGVMYXRMbmcgPSBnZXRNaWRkbGUocG9seWxpbmUpO1xyXG4gICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgYWRkRHVtbXlIYW5kbGUocG9seWxpbmUpO1xyXG4gICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlLkxvbmdpdHVkZSgpICE9PSBtaWRkbGVMYXRMbmcubG5nIHx8IHBvbHlsaW5lLkR1bW15SGFuZGxlLkxhdGl0dWRlKCkgIT09IG1pZGRsZUxhdExuZy5sYXQpXHJcbiAgICAgICAgcG9seWxpbmUuRHVtbXlIYW5kbGUuU2V0TGF0TG5nKG1pZGRsZUxhdExuZyk7XHJcbiAgICBlbHNlXHJcbiAgICAgICAgcG9seWxpbmUucmVkcmF3KCk7XHJcbn1cclxuXHJcbmZ1bmN0aW9uIHJlbW92ZUZyb21Qb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSwgbGF0TG5nOiBMLkxhdExuZykge1xyXG4gICAgcmVtb3ZlRnJvbUFycmF5KHBvbHlsaW5lLmdldExhdExuZ3MoKSwgbGF0TG5nKTtcclxuICAgIHBvbHlsaW5lLnJlZHJhdygpO1xyXG59XHJcblxyXG5mdW5jdGlvbiByZW1vdmVGcm9tQXJyYXk8VD4oYXJyOiBUW10sIG9iajogVCk6IGJvb2xlYW4ge1xyXG4gICAgY29uc3QgdG1wQXJyID0gbmV3IEFycmF5PFQ+KCk7XHJcbiAgICBmb3IgKGxldCBpdGVtIG9mIGFycikge1xyXG4gICAgICAgIGlmIChpdGVtICE9PSBvYmopXHJcbiAgICAgICAgICAgIHRtcEFyci5wdXNoKGl0ZW0pO1xyXG4gICAgfVxyXG4gICAgaWYgKHRtcEFyci5sZW5ndGggPT09IGFyci5sZW5ndGgpXHJcbiAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgd2hpbGUgKGFyci5wb3AoKSkge1xyXG4gICAgfVxyXG4gICAgd2hpbGUgKHRtcEFyci5sZW5ndGggPiAwKSB7XHJcbiAgICAgICAgYXJyLnB1c2godG1wQXJyLnNoaWZ0KCkpO1xyXG4gICAgfVxyXG4gICAgcmV0dXJuIHRydWU7XHJcbn1cclxuXHJcbmVudW0gTWFwTW9kZSB7XHJcbiAgICBBZG1pbixcclxuICAgIFZpZXcsXHJcbiAgICBUcmlwUGxhbm5pbmcsXHJcbiAgICBSb3V0ZURyYXdpbmdcclxufVxyXG5cclxuZGVjbGFyZSBuYW1lc3BhY2UgTCB7XHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIFBvbHlsaW5lIGV4dGVuZHMgUGF0aCB7XHJcblxyXG4gICAgICAgIFdheXBvaW50czogQXJyYXk8V2F5cG9pbnQ+O1xyXG4gICAgICAgIER1bW15SGFuZGxlOiBXYXlwb2ludDtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIExhdExuZyB7XHJcbiAgICAgICAgUG9seWxpbmVzOiBQb2x5bGluZVtdO1xyXG4gICAgICAgIFdheXBvaW50OiBXYXlwb2ludDtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIE1hcmtlciB7XHJcbiAgICAgICAgV2F5cG9pbnQ6IFdheXBvaW50O1xyXG4gICAgICAgIFBvaW50OiBMLlBvaW50O1xyXG4gICAgICAgIF9pY29uO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgQ2lyY2xlTWFya2VyIHtcclxuICAgICAgICBXYXlwb2ludDogV2F5cG9pbnQ7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGludGVyZmFjZSBQYXRoT3B0aW9ucyB7XHJcbiAgICAgICAgRHJhZ2dhYmxlPzogYm9vbGVhbjtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIE1hcmtlck9wdGlvbnMge1xyXG4gICAgICAgIGNvbnRleHRtZW51PzogYm9vbGVhbjtcclxuICAgICAgICBjb250ZXh0bWVudVdpZHRoPzogbnVtYmVyO1xyXG4gICAgICAgIGNvbnRleHRtZW51QW5jaG9yPzogTC5Qb2ludCB8IEwuUG9pbnRbXTtcclxuICAgICAgICBjb250ZXh0bWVudUl0ZW1zPzogY29udGV4dG1lbnVJdGVtW107XHJcbiAgICAgICAgY29udGV4dG1lbnVJbmhlcml0SXRlbXM6IGJvb2xlYW47XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgY29udGV4dG1lbnVJdGVtIHtcclxuICAgICAgICB0ZXh0Pzogc3RyaW5nO1xyXG4gICAgICAgIGljb24/OiBzdHJpbmc7XHJcbiAgICAgICAgaWNvbkNscz86IHN0cmluZztcclxuICAgICAgICBjYWxsYmFjaz86IEZ1bmN0aW9uO1xyXG4gICAgICAgIGNvbnRleHQ/OiBPYmplY3Q7XHJcbiAgICAgICAgZGlzYWJsZWQ/OiBib29sZWFuO1xyXG4gICAgICAgIHNlcGFyYXRvcj86IGJvb2xlYW47XHJcbiAgICAgICAgaGlkZU9uU2VsZWN0PzogYm9vbGVhbjtcclxuICAgICAgICBpbmRleD86IG51bWJlcjtcclxuICAgIH1cclxuXHJcblxyXG59XHJcblxyXG5jbGFzcyBFZGl0aW5nSGVscGVyPFQgZXh0ZW5kcyBDbGllbnRNb2RlbC5FbnRpdHk+IHtcclxuXHJcbiAgICBjb25zdHJ1Y3RvcihlZGl0aW5nTW9kYWxJZDogc3RyaW5nLFxyXG4gICAgICAgIGRlbGV0aW5nTW9kYWxJZDogc3RyaW5nLFxyXG4gICAgICAgIEZhY3Rvcnk6ICgpID0+IFQsXHJcbiAgICAgICAgRGF0YXNldDogS25vY2tvdXRPYnNlcnZhYmxlQXJyYXk8VD4sXHJcbiAgICAgICAgZGV0YWlsTW9kYWxJZDogc3RyaW5nKTtcclxuICAgIGNvbnN0cnVjdG9yKGVkaXRpbmdNb2RhbElkOiBzdHJpbmcsXHJcbiAgICAgICAgZGVsZXRpbmdNb2RhbElkOiBzdHJpbmcsXHJcbiAgICAgICAgRmFjdG9yeTogKCkgPT4gVCxcclxuICAgICAgICBEYXRhc2V0OiBLbm9ja291dE9ic2VydmFibGVBcnJheTxUPixcclxuICAgICAgICBkZXRhaWxlZFNpZGViYXI6IFNpZGViYXIpO1xyXG4gICAgY29uc3RydWN0b3IoZWRpdGluZ01vZGFsSWQ6IHN0cmluZywgZGVsZXRpbmdNb2RhbElkOiBzdHJpbmcsIEZhY3Rvcnk6ICgpID0+IFQsIERhdGFzZXQ6IEtub2Nrb3V0T2JzZXJ2YWJsZUFycmF5PFQ+KTtcclxuICAgIGNvbnN0cnVjdG9yKGVkaXRpbmdNb2RhbElkOiBzdHJpbmcsXHJcbiAgICAgICAgZGVsZXRpbmdNb2RhbElkOiBzdHJpbmcsXHJcbiAgICAgICAgcHJvdGVjdGVkIEZhY3Rvcnk6ICgpID0+IFQsXHJcbiAgICAgICAgcHJvdGVjdGVkIERhdGFzZXQ6IEtub2Nrb3V0T2JzZXJ2YWJsZUFycmF5PFQ+LFxyXG4gICAgICAgIGRldGFpbE1vZGFsSWQ/OiBzdHJpbmcgfCBTaWRlYmFyKSB7XHJcbiAgICAgICAgdGhpcy5FZGl0aW5nTW9kYWwgPSAkKGAjJHtlZGl0aW5nTW9kYWxJZH1gKTtcclxuICAgICAgICB0aGlzLkRlbGV0aW5nTW9kYWwgPSAkKGAjJHtkZWxldGluZ01vZGFsSWR9YCk7XHJcblxyXG4gICAgICAgIGlmICgkKFwiZm9ybTpmaXJzdFwiKS5sZW5ndGggPT09IDEpXHJcbiAgICAgICAgICAgIHRoaXMuUGFyc2xleSA9ICQoXCJmb3JtOmZpcnN0XCIsIHRoaXMuRWRpdGluZ01vZGFsKS5wYXJzbGV5KHdpbmRvdy5QYXJzbGV5Q29uZmlnKTtcclxuICAgICAgICB0aGlzLkVkaXRpbmdNb2RhbC5vbihcInNob3cuYnMubW9kYWxcIixcclxuICAgICAgICAgICAgKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5FZGl0aW5nTW9kYWxPcGVuID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkVkaXRpbmcoKSA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRWRpdGluZyh0aGlzLkZhY3RvcnkoKSk7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuQWxidW1TdGFjay51bnNoaWZ0KHRoaXMuRWRpdGluZygpLkFsYnVtKCkpO1xyXG4gICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgdGhpcy5FZGl0aW5nTW9kYWwub24oXCJzaG93bi5icy5tb2RhbFwiLFxyXG4gICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICB3aW5kb3cuc2V0VGltZW91dCgoKSA9PiAkKFwiaW5wdXQsIHNlbGVjdCwgdGV4dGFyZWFcIiwgdGhpcy5FZGl0aW5nTW9kYWwpLmZpcnN0KCkuZm9jdXMoKSwgMjAwKTtcclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIHRoaXMuRWRpdGluZ01vZGFsLm9uKFwiaGlkZGVuLmJzLm1vZGFsXCIsXHJcbiAgICAgICAgICAgICgpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkVkaXRpbmcoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRWRpdGluZyh1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5FZGl0aW5nTW9kYWxPcGVuID0gZmFsc2U7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuQWxidW1TdGFjay5zaGlmdCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkVkaXRpbmcuc3Vic2NyaWJlKChlbnRpdHkpID0+IHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eSA9PT0gdW5kZWZpbmVkICYmIHRoaXMuRWRpdGluZ01vZGFsT3Blbikge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5FZGl0aW5nTW9kYWwubW9kYWwoXCJoaWRlXCIpO1xyXG4gICAgICAgICAgICB9IGVsc2UgaWYgKCF0aGlzLkVkaXRpbmdNb2RhbE9wZW4pIHtcclxuICAgICAgICAgICAgICAgIGVudGl0eS5TYXZlU3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIHRoaXMuRWRpdGluZ01vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG4gICAgICAgIHRoaXMuRWRpdGluZy5zdWJzY3JpYmUoKCkgPT4ge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5FZGl0aW5nKCkgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5FZGl0aW5nKCkuUmV2ZXJ0U3RhdGUodHJ1ZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9LFxyXG4gICAgICAgICAgICB0aGlzLFxyXG4gICAgICAgICAgICBcImJlZm9yZUNoYW5nZVwiKTtcclxuXHJcbiAgICAgICAgdGhpcy5EZWxldGluZ01vZGFsLm9uKFwic2hvdy5icy5tb2RhbFwiLFxyXG4gICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkRlbGV0aW5nTW9kYWxPcGVuID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5BbGJ1bVN0YWNrLnVuc2hpZnQodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIHRoaXMuRGVsZXRpbmdNb2RhbC5vbihcImhpZGRlbi5icy5tb2RhbFwiLFxyXG4gICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAodGhpcy5EZWxldGluZygpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EZWxldGluZyh1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkFsYnVtU3RhY2suc2hpZnQoKTtcclxuICAgICAgICAgICAgICAgIHRoaXMuRGVsZXRpbmdNb2RhbE9wZW4gPSBmYWxzZTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5EZWxldGluZy5zdWJzY3JpYmUoKGVudGl0eSkgPT4ge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5ID09PSB1bmRlZmluZWQgJiYgdGhpcy5EZWxldGluZ01vZGFsT3Blbikge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5EZWxldGluZ01vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgfSBlbHNlIGlmICghdGhpcy5EZWxldGluZ01vZGFsT3Blbikge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5EZWxldGluZ01vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBpZiAoZGV0YWlsTW9kYWxJZCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIHRoaXMuSGFzRGV0YWlsVmlldyA9IHRydWU7XHJcbiAgICAgICAgICAgIGlmIChkZXRhaWxNb2RhbElkIGluc3RhbmNlb2YgU2lkZWJhcikge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5EZXRhaWxTaWRlYmFyID0gZGV0YWlsTW9kYWxJZDtcclxuICAgICAgICAgICAgICAgIHRoaXMuRGV0YWlsLnN1YnNjcmliZSgoZW50aXR5KSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKGVudGl0eSA9PT0gdW5kZWZpbmVkICYmIHRoaXMuRGV0YWlsU2lkZWJhci5Jc0FjdGl2KCkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkFsYnVtU3RhY2suc2hpZnQoKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5EZXRhaWxTaWRlYmFyLkhpZGUoKTtcclxuICAgICAgICAgICAgICAgICAgICB9IGVsc2UgaWYgKGVudGl0eSAhPT0gdW5kZWZpbmVkICYmICF0aGlzLkRldGFpbFNpZGViYXIuSXNBY3RpdigpKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuRGV0YWlsU2lkZWJhci5TaG93KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5BbGJ1bVN0YWNrLnVuc2hpZnQoZW50aXR5LkFsYnVtKCkpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5EZXRhaWxNb2RhbCA9ICQoYCMke2RldGFpbE1vZGFsSWR9YCk7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkRldGFpbC5zdWJzY3JpYmUoKGVudGl0eSkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChlbnRpdHkgPT09IHVuZGVmaW5lZCAmJiB0aGlzLkRldGFpbE1vZGFsT3Blbikge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkRldGFpbE1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgICAgICAgICB9IGVsc2UgaWYgKGVudGl0eSAhPT0gdW5kZWZpbmVkICYmICF0aGlzLkRldGFpbE1vZGFsT3Blbikge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkRldGFpbE1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgICAgICAgICB0aGlzLkRldGFpbE1vZGFsLm9uKFwic2hvdy5icy5tb2RhbFwiLFxyXG4gICAgICAgICAgICAgICAgICAgICgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5EZXRhaWxNb2RhbE9wZW4gPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuQWxidW1TdGFjay51bnNoaWZ0KHRoaXMuRGV0YWlsKCkuQWxidW0oKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcblxyXG4gICAgICAgICAgICAgICAgdGhpcy5EZXRhaWxNb2RhbC5vbihcImhpZGUuYnMubW9kYWxcIixcclxuICAgICAgICAgICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuRGV0YWlsTW9kYWxPcGVuID0gZmFsc2U7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5BbGJ1bVN0YWNrLnNoaWZ0KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIHByb3RlY3RlZCBFZGl0aW5nTW9kYWw6IEpRdWVyeTtcclxuICAgIHByb3RlY3RlZCBEZWxldGluZ01vZGFsOiBKUXVlcnk7XHJcbiAgICBwcm90ZWN0ZWQgRGV0YWlsTW9kYWw6IEpRdWVyeTtcclxuICAgIHByb3RlY3RlZCBEZXRhaWxTaWRlYmFyOiBTaWRlYmFyO1xyXG4gICAgcHJvdGVjdGVkIEVkaXRpbmdNb2RhbE9wZW4gPSBmYWxzZTtcclxuICAgIHByb3RlY3RlZCBEZWxldGluZ01vZGFsT3BlbiA9IGZhbHNlO1xyXG4gICAgcHJvdGVjdGVkIERldGFpbE1vZGFsT3BlbiA9IGZhbHNlO1xyXG4gICAgcHJvdGVjdGVkIFBhcnNsZXk6IGFueTtcclxuXHJcbiAgICBIYXNEZXRhaWxWaWV3ID0gZmFsc2U7XHJcbiAgICBEZXRhaWwgPSBrby5vYnNlcnZhYmxlPFQ+KCk7XHJcbiAgICBFZGl0aW5nID0ga28ub2JzZXJ2YWJsZTxUPigpO1xyXG4gICAgRGVsZXRpbmcgPSBrby5vYnNlcnZhYmxlPFQ+KCk7XHJcblxyXG4gICAgRGVsZXRlID0gKCkgPT4ge1xyXG4gICAgICAgIHRoaXMuRGVsZXRpbmcoKVxyXG4gICAgICAgICAgICAuRGVsZXRlT25TZXJ2ZXIoKVxyXG4gICAgICAgICAgICAuZG9uZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAvL3RoaXMuRGF0YXNldC5yZW1vdmUodGhpcy5EZWxldGluZygpKTtcclxuICAgICAgICAgICAgICAgIHRoaXMuRGVsZXRpbmcodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkRldGFpbCgpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EZXRhaWwodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkVkaXRpbmcoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRWRpdGluZyh1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgIH07XHJcblxyXG4gICAgU2F2ZSA9ICgpID0+IHtcclxuICAgICAgICBpZiAodGhpcy5QYXJzbGV5ICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgIHRoaXMuUGFyc2xleS53aGVuVmFsaWRhdGUoKVxyXG4gICAgICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIHZhciBpc05ldyA9IHRoaXMuRWRpdGluZygpLklkKCkgPT09IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkVkaXRpbmcoKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAuU2F2ZVRvU2VydmVyKClcclxuICAgICAgICAgICAgICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLy9pZiAoaXNOZXcpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAvLyAgICB0aGlzLkRhdGFzZXQucHVzaCh0aGlzLkVkaXRpbmcoKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkVkaXRpbmcodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICBlbHNlIHtcclxuICAgICAgICAgICAgY29uc3QgaXNOZXcgPSB0aGlzLkVkaXRpbmcoKS5JZCgpID09PSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgIHRoaXMuRWRpdGluZygpXHJcbiAgICAgICAgICAgICAgICAuU2F2ZVRvU2VydmVyKClcclxuICAgICAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAvL2lmIChpc05ldylcclxuICAgICAgICAgICAgICAgICAgICAvLyAgICB0aGlzLkRhdGFzZXQucHVzaCh0aGlzLkVkaXRpbmcoKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5FZGl0aW5nKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG59XHJcblxyXG5jbGFzcyBNYXBWaWV3TW9kZWwge1xyXG4gICAgY29uc3RydWN0b3IobWFwTW9kZTogTWFwTW9kZSkge1xyXG4gICAgICAgIEwubWFwYm94XHJcbiAgICAgICAgICAgIC5hY2Nlc3NUb2tlbiA9XHJcbiAgICAgICAgICAgIFwicGsuZXlKMUlqb2laR0Z1YVdWc0xXdDFiMjRpTENKaElqb2lZMmxsZG5WdFkyOWlNREJpT0hReGJUQnZaekJxWldsNmNDSjkuVUVjMllxSDU5cEIxWVRwdjIydmc4QVwiO1xyXG4gICAgICAgIHRoaXMuTWFwTW9kZShtYXBNb2RlKTtcclxuICAgICAgICB0aGlzLk1hcE1vZGUuc3Vic2NyaWJlKCgpID0+IHtcclxuICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTWFwKCk7XHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgY29uc3QgbWFwT3B0aW9uczogTC5tYXBib3guTWFwT3B0aW9ucyA9IHtcclxuICAgICAgICAgICAgY29udGV4dG1lbnU6IG1hcE1vZGUgPT09IE1hcE1vZGUuQWRtaW4sXHJcbiAgICAgICAgICAgIGNvbnRleHRtZW51SXRlbXM6IFtcclxuICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICB0ZXh0OiBcIk5ldWVyIEhhZmVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgY2FsbGJhY2s6IGZ1bmN0aW9uIChlKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGUpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSGFyYm91ckhlbHBlci5FZGl0aW5nKG1hcFZpZXdNb2RlbC5DcmVhdGVIYXJib3VyKFwiXCIsIGUubGF0bG5nKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBdXHJcbiAgICAgICAgfTtcclxuICAgICAgICB0aGlzLk1hcCA9IEwubWFwYm94Lm1hcChcIm1hcFwiLFxyXG4gICAgICAgICAgICBcIm1hcGJveC5zdHJlZXRzXCIsXHJcbiAgICAgICAgICAgIG1hcE9wdGlvbnMpO1xyXG4gICAgICAgIHRoaXMuTWFwLnNldFZpZXcoWzU0LjQwNzc0MTY2ODIwMDY5LCAxMC41MjM1MjkwNTI3MzQzNzNdLCA5KTtcclxuICAgICAgICBMLnRpbGVMYXllcihcImh0dHA6Ly90MS5vcGVuc2VhbWFwLm9yZy9zZWFtYXJrL3t6fS97eH0ve3l9LnBuZ1wiKS5hZGRUbyh0aGlzLk1hcCk7XHJcbiAgICAgICAgdGhpcy5Mb2FkRGF0YSgpO1xyXG4gICAgICAgICQuZ2V0KFwiL0FjY291bnQvSXNMb2dnZWRJblwiKS5kb25lKChkYXRhKSA9PiB0aGlzLklzTG9nZ2VkSW4oZGF0YSkpO1xyXG4gICAgICAgIHRoaXMuQ29udGVudFBhZ2VzLnN1YnNjcmliZSgoZGF0YSkgPT4ge1xyXG4gICAgICAgICAgICB2YXIgbmF2ID0gJChcIiNsZWZ0TmF2XCIpO1xyXG4gICAgICAgICAgICAkKFwiLmNvbnRlbnRQYWdlTGlua1wiLCBuYXYpLnJlbW92ZSgpO1xyXG4gICAgICAgICAgICBmb3IgKGxldCBjUCBvZiBkYXRhKSB7XHJcbiAgICAgICAgICAgICAgICAkKGA8bGkgcm9sZT1cInByZXNlbnRhdGlvblwiIGNsYXNzPVwiY29udGVudFBhZ2VMaW5rXCI+PGEgaHJlZj1cIiNcIj4ke2NQLlRpdGxlKCl9PC9hPjwvbGk+YClcclxuICAgICAgICAgICAgICAgICAgICAuY2xpY2soKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuQ29udGVudFBhZ2VIZWxwZXIuRGV0YWlsKGNQKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgICAgICAgICAgICAgIH0pXHJcbiAgICAgICAgICAgICAgICAgICAgLmFwcGVuZFRvKG5hdik7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgdGhpcy5IYXJib3VySGVscGVyLkRldGFpbC5zdWJzY3JpYmUoKG5ld0hhcmJvdXIpID0+IHtcclxuICAgICAgICAgICAgaWYgKG5ld0hhcmJvdXIgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkNhbGN1bGF0ZURpc3RhbmNlcyhuZXdIYXJib3VyKTtcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5IYXJib3Vycy5zb3J0KChoMSwgaDIpID0+IGgxLkRpc3RhbmNlKCkgLSBoMi5EaXN0YW5jZSgpKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGggb2YgbWFwVmlld01vZGVsLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAgICAgICAgICAgICBoLkRpc3RhbmNlKDApO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkID0gZmFsc2U7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5IaWRlUm91dGUoKTtcclxuICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLkhhcmJvdXJIZWxwZXIuRWRpdGluZy5zdWJzY3JpYmUoKGhhcmJvdXIpID0+IHtcclxuICAgICAgICAgICAgaWYgKGhhcmJvdXIgIT09IHVuZGVmaW5lZCAmJiBoYXJib3VyLklkKCkgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5NYXAucmVtb3ZlTGF5ZXIoaGFyYm91ci5tYXJrZXIpO1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgICAgIHRoaXMsXHJcbiAgICAgICAgICAgIFwiYmVmb3JlQ2hhbmdlXCIpO1xyXG5cclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwibW91c2Vtb3ZlXCIsXHJcbiAgICAgICAgICAgIChlOiBMLkxlYWZsZXRNb3VzZUV2ZW50KSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAodGhpcy5HZXRNYXBNb2RlKCkgPT09IE1hcE1vZGUuUm91dGVEcmF3aW5nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nTGF0TG5nLmxhdCA9IGUubGF0bG5nLmxhdDtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdMYXRMbmcubG5nID0gZS5sYXRsbmcubG5nO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ1BvbHlsaW5lLnJlZHJhdygpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKVxyXG4gICAgICAgICAgICAgICAgICAgIGZvciAobGV0IG1hcmtlciBvZiB0aGlzLldheXBvaW50TWFya2Vycykge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAobWFya2VyLlBvaW50LmRpc3RhbmNlVG8oZS5jb250YWluZXJQb2ludCkgPCAxNTApXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXJrZXIuc2V0T3BhY2l0eShtYXJrZXIuV2F5cG9pbnQuSXNEdW1teSgpID8gMC4wIDogMSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcmtlci5zZXRPcGFjaXR5KG1hcmtlci5XYXlwb2ludC5Jc0R1bW15KCkgPyAwLjAgOiAwLjgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWxcclxuICAgICAgICAgICAgICAgICAgICAuSG92ZXJlZFBvbHlpbmUgIT09XHJcbiAgICAgICAgICAgICAgICAgICAgdW5kZWZpbmVkICYmXHJcbiAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lLkR1bW15SGFuZGxlICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBwb2x5bGluZSA9IG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZTtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBwMSA9IG1hcFZpZXdNb2RlbC5NYXAubGF0TG5nVG9Db250YWluZXJQb2ludChwb2x5bGluZS5nZXRMYXRMbmdzKClbMF0pO1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IHAyID0gbWFwVmlld01vZGVsLk1hcC5sYXRMbmdUb0NvbnRhaW5lclBvaW50KHBvbHlsaW5lLmdldExhdExuZ3MoKVsxXSk7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHAxLmRpc3RhbmNlVG8oZS5jb250YWluZXJQb2ludCkgPCAyMCB8fCBwMi5kaXN0YW5jZVRvKGUuY29udGFpbmVyUG9pbnQpIDwgMjApIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZS5EdW1teUhhbmRsZS5tYXJrZXIuc2V0T3BhY2l0eSgwLjgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuSG92ZXJlZFBvbHlpbmUuRHVtbXlIYW5kbGVcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5TZXRMYXRMbmcobWFwVmlld01vZGVsLk1hcFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5jb250YWluZXJQb2ludFRvTGF0TG5nKEwuTGluZVV0aWxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgLmNsb3Nlc3RQb2ludE9uU2VnbWVudChlLmNvbnRhaW5lclBvaW50LFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBwMSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgcDIpKSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGZhbHNlKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9XHJcblxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwiY2xpY2tcIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkdldE1hcE1vZGUoKSA9PT0gTWFwTW9kZS5Sb3V0ZURyYXdpbmcpIHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCB3YXlwb2ludCA9IG1hcFZpZXdNb2RlbC5DcmVhdGVXYXlwb2ludChlLmxhdGxuZywgTWFya2VyVHlwZS5XYXlwb2ludCk7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3Qgc3RhcnRJZCA9IHRoaXMuRHJhd2luZ1BvbHlsaW5lLldheXBvaW50c1swXS5JZCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIHdheXBvaW50LlNhdmVUb1NlcnZlcigpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5kb25lKHcgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbnNcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAuQ29ubmVjdCh3LklkLCBzdGFydElkKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICAgICAgd2F5cG9pbnQuQWRkVG9Qb2x5bGluZSh0aGlzLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgYWRkRHVtbXlIYW5kbGUodGhpcy5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21Qb2x5bGluZSh0aGlzLkRyYXdpbmdQb2x5bGluZSwgdGhpcy5EcmF3aW5nTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdQb2x5bGluZSA9IHRoaXMuQWRkUG9seWxpbmUod2F5cG9pbnQpO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuRHJhd2luZ0xhdExuZyA9IG5ldyBMLkxhdExuZyhlLmxhdGxuZy5sYXQsIGUubGF0bG5nLmxuZyk7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nUG9seWxpbmUuYWRkTGF0TG5nKHRoaXMuRHJhd2luZ0xhdExuZyk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0pO1xyXG5cclxuICAgICAgICB0aGlzLk1hcC5hZGRFdmVudExpc3RlbmVyKFwiZGJsY2xpY2tcIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkdldE1hcE1vZGUoKSA9PT0gTWFwTW9kZS5Sb3V0ZURyYXdpbmcpIHtcclxuICAgICAgICAgICAgICAgICAgICBlLm9yaWdpbmFsRXZlbnQuY2FuY2VsQnViYmxlID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgICAgICBlLm9yaWdpbmFsRXZlbnQucHJldmVudERlZmF1bHQoKTtcclxuICAgICAgICAgICAgICAgICAgICBlLm9yaWdpbmFsRXZlbnQuc3RvcFByb3BhZ2F0aW9uKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5EcmF3aW5nUG9seWxpbmUuYWRkTGF0TG5nKGUubGF0bG5nKTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkRyYXdpbmdMYXRMbmcgPSBlLmxhdGxuZztcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgJChkb2N1bWVudClcclxuICAgICAgICAgICAgLmtleXVwKChlOiBKUXVlcnlLZXlFdmVudE9iamVjdCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuR2V0TWFwTW9kZSgpID09PSBNYXBNb2RlLlJvdXRlRHJhd2luZykge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChlLmtleUNvZGUgPT09IDI3KSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuUmVtb3ZlUG9seWxpbmUodGhpcy5EcmF3aW5nUG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5NYXAuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdmVcIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IG1hcmtlciBvZiB0aGlzLldheXBvaW50TWFya2Vycykge1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcmtlci5Qb2ludCA9IHRoaXMuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQobWFya2VyLmdldExhdExuZygpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgdGhpcy5NYXAuYWRkRXZlbnRMaXN0ZW5lcihcInpvb21cIixcclxuICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IG1hcmtlciBvZiB0aGlzLldheXBvaW50TWFya2Vycykge1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcmtlci5Qb2ludCA9IHRoaXMuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQobWFya2VyLmdldExhdExuZygpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICB9XHJcblxyXG4gICAgSXNMb2dnZWRJbiA9IGtvLm9ic2VydmFibGUoZmFsc2UpO1xyXG5cclxuICAgIHByaXZhdGUgcm91dGVQb2x5bGluZSA9IGtvLm9ic2VydmFibGU8TC5Qb2x5bGluZT4oKTtcclxuXHJcbiAgICBTdGFydFJvdXRlKCkge1xyXG4gICAgICAgIGNvbnN0IHRyaXAgPSBuZXcgQ2xpZW50TW9kZWwuVHJpcCgpO1xyXG4gICAgICAgIGNvbnN0IHRhY2sgPSBuZXcgQ2xpZW50TW9kZWwuVGFjaygpO1xyXG4gICAgICAgIGNvbnN0IGhhcmJvdXIgPSBtYXBWaWV3TW9kZWwuSGFyYm91ckhlbHBlci5EZXRhaWwoKTtcclxuICAgICAgICB0YWNrLlN0YXJ0KGhhcmJvdXIpO1xyXG4gICAgICAgIHRyaXAuVGFja3MucHVzaCh0YWNrKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuVHJpcEhlbHBlci5FZGl0aW5nKHRyaXApO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZVBvbHlsaW5lKEwucG9seWxpbmUoW10sXHJcbiAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgIGNvbG9yOiBcIiMwMDk5MDBcIlxyXG4gICAgICAgICAgICB9KSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRUbyhtYXBWaWV3TW9kZWwuTWFwKTtcclxuICAgIH1cclxuXHJcbiAgICBJc0xhc3RUYWtJblJvdXRlID0ga28uY29tcHV0ZWQoe1xyXG4gICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgdmFyIHRyaXAgPSBtYXBWaWV3TW9kZWwuVHJpcEhlbHBlci5FZGl0aW5nKCk7XHJcbiAgICAgICAgICAgIHZhciBoID0gbWFwVmlld01vZGVsLkhhcmJvdXJIZWxwZXIuRGV0YWlsKCk7XHJcbiAgICAgICAgICAgIHJldHVybiB0cmlwICE9PSB1bmRlZmluZWQgJiYgaCAhPT0gdW5kZWZpbmVkICYmIHRyaXAuVGFja3MoKVt0cmlwLlRhY2tzKCkubGVuZ3RoIC0gMV0uU3RhcnQoKSA9PT0gaDtcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG4gICAgR2V0Um91dGVEaXN0YW5jZSA9IGtvLmNvbXB1dGVkKHtcclxuICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgIHZhciBkaXN0YW5jZSA9IDA7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHRhY2sgb2YgbWFwVmlld01vZGVsLlRyaXBIZWxwZXIuRWRpdGluZygpLlRhY2tzKCkpIHtcclxuICAgICAgICAgICAgICAgIGlmICghaXNOYU4odGFjay5EaXN0YW5jZSgpKSlcclxuICAgICAgICAgICAgICAgICAgICBkaXN0YW5jZSArPSB0YWNrLkRpc3RhbmNlKCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIGRpc3RhbmNlO1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgZGVmZXJFdmFsdWF0aW9uOiB0cnVlXHJcbiAgICB9KTtcclxuXHJcbiAgICBUb3BKb2JzID0ga28uY29tcHV0ZWQoe1xyXG4gICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5Kb2JzKCkuZmlsdGVyKChqKSA9PiBqLlN1cGVySm9iSWQoKSA9PT0gdW5kZWZpbmVkKTtcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG4gICAgQWRkVG9Sb3V0ZSgpIHtcclxuICAgICAgICBjb25zdCB0cmlwID0gbWFwVmlld01vZGVsLlRyaXBIZWxwZXIuRWRpdGluZygpO1xyXG4gICAgICAgIGNvbnN0IHRhcmdldEhhcmJvdXIgPSBtYXBWaWV3TW9kZWwuSGFyYm91ckhlbHBlci5FZGl0aW5nKCk7XHJcbiAgICAgICAgY29uc3QgdGFjayA9IG5ldyBDbGllbnRNb2RlbC5UYWNrKCk7XHJcbiAgICAgICAgY29uc3QgbGFzdFRhY2sgPSB0cmlwLlRhY2tzKClbdHJpcC5UYWNrcygpLmxlbmd0aCAtIDFdO1xyXG4gICAgICAgIGNvbnN0IHN0YXJ0SGFyYm91ciA9IGxhc3RUYWNrLlN0YXJ0KCk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkNhbGN1bGF0ZURpc3RhbmNlcyh0YXJnZXRIYXJib3VyLCBzdGFydEhhcmJvdXIpO1xyXG4gICAgICAgIGxhc3RUYWNrLkRpc3RhbmNlKHN0YXJ0SGFyYm91ci5Sb3V0ZURpc3RhbmNlKCkpO1xyXG4gICAgICAgIGxldCB3cDogQ2xpZW50TW9kZWwuV2F5cG9pbnQgPSBzdGFydEhhcmJvdXI7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRMYXRMbmcod3AuTGF0TG5nKTtcclxuICAgICAgICB3aGlsZSAod3AuUm91dGVQcmVjZXNzb3IoKSAhPT0gdW5kZWZpbmVkIC8qJiYgd3AuUm91dGVQcmVjZXNzb3IoKSAhPT0gc3RhcnRIYXJib3VyKi8pIHtcclxuICAgICAgICAgICAgd3AgPSB3cC5Sb3V0ZVByZWNlc3NvcigpO1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpLmFkZExhdExuZyh3cC5MYXRMbmcpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgbGFzdFRhY2suRW5kKHRhcmdldEhhcmJvdXIpO1xyXG4gICAgICAgIHRhY2suU3RhcnQodGFyZ2V0SGFyYm91cik7XHJcbiAgICAgICAgdHJpcC5UYWNrcy5wdXNoKHRhY2spO1xyXG4gICAgfVxyXG5cclxuICAgIFJlZHJhd1RyaXAoKSB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLk1hcC5yZW1vdmVMYXllcihtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZShMLnBvbHlsaW5lKFtdLFxyXG4gICAgICAgICAgICB7XHJcbiAgICAgICAgICAgICAgICBjb2xvcjogXCIjMDA5OTAwXCJcclxuICAgICAgICAgICAgfSkpO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZVBvbHlsaW5lKCkuYWRkVG8obWFwVmlld01vZGVsLk1hcCk7XHJcbiAgICAgICAgZm9yIChsZXQgdGFjayBvZiBtYXBWaWV3TW9kZWwuVHJpcEhlbHBlci5FZGl0aW5nKCkuVGFja3MoKSkge1xyXG4gICAgICAgICAgICBjb25zdCB0YXJnZXRIYXJib3VyID0gdGFjay5FbmQoKTtcclxuICAgICAgICAgICAgY29uc3Qgc3RhcnRIYXJib3VyID0gdGFjay5TdGFydCgpO1xyXG4gICAgICAgICAgICBpZiAodGFyZ2V0SGFyYm91ciA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgY29udGludWU7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5DYWxjdWxhdGVEaXN0YW5jZXModGFyZ2V0SGFyYm91ciwgc3RhcnRIYXJib3VyKTtcclxuICAgICAgICAgICAgdGFjay5EaXN0YW5jZShzdGFydEhhcmJvdXIuUm91dGVEaXN0YW5jZSgpKTtcclxuICAgICAgICAgICAgbGV0IHdwOiBDbGllbnRNb2RlbC5XYXlwb2ludCA9IHN0YXJ0SGFyYm91cjtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlUG9seWxpbmUoKS5hZGRMYXRMbmcod3AuTGF0TG5nKTtcclxuICAgICAgICAgICAgd2hpbGUgKHdwLlJvdXRlUHJlY2Vzc29yKCkgIT09IHVuZGVmaW5lZCAvKiYmIHdwLlJvdXRlUHJlY2Vzc29yKCkgIT09IHN0YXJ0SGFyYm91ciovKSB7XHJcbiAgICAgICAgICAgICAgICB3cCA9IHdwLlJvdXRlUHJlY2Vzc29yKCk7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwucm91dGVQb2x5bGluZSgpLmFkZExhdExuZyh3cC5MYXRMbmcpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIFB1bGxUYWNrKCkge1xyXG4gICAgICAgIGNvbnN0IHRhY2s6IENsaWVudE1vZGVsLlRhY2sgPSB0aGlzIGFzIGFueTtcclxuICAgICAgICBjb25zdCB0YWNrcyA9IG1hcFZpZXdNb2RlbC5UcmlwSGVscGVyLkVkaXRpbmcoKS5UYWNrcztcclxuICAgICAgICBjb25zdCBpbmRleCA9IHRhY2tzLmluZGV4T2YodGFjayk7XHJcbiAgICAgICAgY29uc3QgcHJldlRhY2sgPSB0YWNrcygpW2luZGV4IC0gMV07XHJcbiAgICAgICAgY29uc3QgdG1wRW5kID0gdGFjay5FbmQoKTtcclxuICAgICAgICB0YWNrLkVuZChwcmV2VGFjay5TdGFydCgpKTtcclxuICAgICAgICBwcmV2VGFjay5FbmQodG1wRW5kKTtcclxuICAgICAgICBpZiAoaW5kZXggPiAxKSB7XHJcbiAgICAgICAgICAgIHRhY2tzKClbaW5kZXggLSAyXS5FbmQodGFjay5TdGFydCgpKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgdGFja3Muc3BsaWNlKGluZGV4IC0gMSwgMiwgdGFjaywgcHJldlRhY2spO1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5SZWRyYXdUcmlwKCk7XHJcbiAgICB9XHJcblxyXG4gICAgUHVzaFRhY2soKSB7XHJcbiAgICAgICAgY29uc3QgdGFjazogQ2xpZW50TW9kZWwuVGFjayA9IHRoaXMgYXMgYW55O1xyXG4gICAgICAgIGNvbnN0IHRhY2tzID0gbWFwVmlld01vZGVsLlRyaXBIZWxwZXIuRWRpdGluZygpLlRhY2tzO1xyXG4gICAgICAgIGNvbnN0IGluZGV4ID0gdGFja3MuaW5kZXhPZih0YWNrKTtcclxuICAgICAgICBjb25zdCBuZXh0VGFjayA9IHRhY2tzKClbaW5kZXggKyAxXTtcclxuICAgICAgICB0YWNrLkVuZChuZXh0VGFjay5FbmQoKSk7XHJcbiAgICAgICAgbmV4dFRhY2suRW5kKHRhY2suU3RhcnQoKSk7XHJcbiAgICAgICAgaWYgKGluZGV4ID4gMCkge1xyXG4gICAgICAgICAgICB0YWNrcygpW2luZGV4IC0gMV0uRW5kKG5leHRUYWNrLlN0YXJ0KCkpO1xyXG4gICAgICAgIH1cclxuICAgICAgICB0YWNrcy5zcGxpY2UoaW5kZXgsIDIsIG5leHRUYWNrLCB0YWNrKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuUmVkcmF3VHJpcCgpO1xyXG4gICAgfVxyXG5cclxuICAgIFJlbW92ZVRhY2soKSB7XHJcbiAgICAgICAgY29uc3QgdGFjazogQ2xpZW50TW9kZWwuVGFjayA9IHRoaXMgYXMgYW55O1xyXG4gICAgICAgIGNvbnN0IHRhY2tzID0gbWFwVmlld01vZGVsLlRyaXBIZWxwZXIuRWRpdGluZygpLlRhY2tzO1xyXG4gICAgICAgIGNvbnN0IGluZGV4ID0gdGFja3MuaW5kZXhPZih0YWNrKTtcclxuICAgICAgICBjb25zdCBwcmV2VGFjayA9IHRhY2tzKClbaW5kZXggLSAxXTtcclxuICAgICAgICBpZiAocHJldlRhY2sgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgcHJldlRhY2suRW5kKHRhY2suRW5kKCkpO1xyXG4gICAgICAgIHRhY2tzLnJlbW92ZSh0YWNrKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuUmVkcmF3VHJpcCgpO1xyXG4gICAgfVxyXG5cclxuICAgIElzSW5WaWV3TW9kZSA9IGtvLmNvbXB1dGVkPGJvb2xlYW4+KHtcclxuICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgIHJldHVybiBtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLlZpZXc7XHJcbiAgICAgICAgfSxcclxuICAgICAgICBkZWZlckV2YWx1YXRpb246IHRydWVcclxuICAgIH0pO1xyXG5cclxuICAgIElzSW5BZG1pbk1vZGUgPSBrby5jb21wdXRlZDxib29sZWFuPih7XHJcbiAgICAgICAgcmVhZDogKCkgPT4ge1xyXG4gICAgICAgICAgICByZXR1cm4gbWFwVmlld01vZGVsLk1hcE1vZGUoKSA9PT0gTWFwTW9kZS5BZG1pbjtcclxuICAgICAgICB9LFxyXG4gICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgfSk7XHJcblxyXG5cclxuICAgIExvYWREYXRhKCkge1xyXG4gICAgICAgIFNlcnZlckFwaS5XYXlwb2ludHNcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHNFbnRpdHkuVHlwZSA9PT0gXCJXYXlwb2ludFwiKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLldheXBvaW50cy5wdXNoKG1hcFZpZXdNb2RlbFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLkNyZWF0ZVdheXBvaW50KE1hcmtlclR5cGUuV2F5cG9pbnQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsc2UgaWYgKHNFbnRpdHkuVHlwZSA9PT0gXCJIYXJib3VyXCIpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29uc3QgaGFyYm91ciA9IG1hcFZpZXdNb2RlbFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLkNyZWF0ZUhhcmJvdXIoKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkhhcmJvdXJzLnB1c2goaGFyYm91cik7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5XYXlwb2ludENvbm5lY3Rpb25zXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRDb25uZWN0aW9ucy5wdXNoKHNFbnRpdHkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5XYXlwb2ludENvbm5lY3Rpb25zTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5QZXJzb25zXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuUGVyc29ucy5wdXNoKG5ldyBDbGllbnRNb2RlbC5QZXJzb24oKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLlBlcnNvbnNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLkpvYnNcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Kb2JzLnB1c2gobmV3IENsaWVudE1vZGVsLkpvYigpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuSm9ic0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuVHJpcHNcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Ucmlwcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5UcmlwKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5Ucmlwc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuQWRkcmVzc2VzXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuQWRkcmVzc2VzLnB1c2gobmV3IENsaWVudE1vZGVsLkFkZHJlc3MoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLkFkZHJlc3Nlc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuSW1hZ2VzXHJcbiAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHNFbnRpdHkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuSW1hZ2VzLnB1c2gobmV3IENsaWVudE1vZGVsLkltYWdlKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5JbWFnZXNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLkFsYnVtc1xyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkFsYnVtcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5BbGJ1bSgpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuQWxidW1zTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5Mb2dCb29rRW50cmllc1xyXG4gICAgICAgICAgICAuR2V0KClcclxuICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBzRW50aXR5IG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkxvZ0Jvb2tFbnRyaWVzLnB1c2gobmV3IENsaWVudE1vZGVsLkxvZ0Jvb2tFbnRyeSgpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuTG9nQm9va0VudHJpZXNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLkFsYnVtSW1hZ2VzLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgYWkgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuQWxidW1JbWFnZXMucHVzaChhaSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLkFsYnVtSW1hZ2VzTG9hZGVkID0gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIHRoaXMuSW5pdGlhbGl6ZU1vZGVsKCk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIFNlcnZlckFwaS5DcmV3cy5HZXQoKVxyXG4gICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGMgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuQ3Jld3MucHVzaChjKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHRoaXMuQ3Jld3NMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5Jbml0aWFsaXplTW9kZWwoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgU2VydmVyQXBpLldpZmlzLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgYyBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5XaWZpcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5XaWZpKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoYykpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5XaWZpc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuQ29udGVudFBhZ2VzLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgYyBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Db250ZW50UGFnZXMucHVzaChuZXcgQ2xpZW50TW9kZWwuQ29udGVudFBhZ2UoKS5Mb2FkRnJvbVNlcnZlckVudGl0eShjKSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLkNvbnRlbnRQYWdlc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAvL1NlcnZlckFwaS5XYXlwb2ludFRhY2tzLkdldCgpLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgLy8gICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7IHRoaXMuV2F5cG9pbnRUYWNrcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5XYXlwb2ludFRhY2soKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7IH1cclxuICAgICAgICAvLyAgICB0aGlzLldheXBvaW50VGFja3NMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgIC8vICAgIHRoaXMuSW5pdGlhbG96ZU1vZGVsKCk7XHJcbiAgICAgICAgLy99KTtcclxuICAgICAgICBTZXJ2ZXJBcGkuVGFja3NcclxuICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgIC5kb25lKGQgPT4ge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5UYWNrcy5wdXNoKG5ldyBDbGllbnRNb2RlbC5UYWNrKCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgdGhpcy5UYWNrc0xvYWRlZCA9IHRydWU7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAvL1NlcnZlckFwaS5Mb2NhdGlvbnNcclxuICAgICAgICAvLyAgICAuR2V0KClcclxuICAgICAgICAvLyAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAvLyAgICAgICAgZm9yIChsZXQgc0VudGl0eSBvZiBkKSB7XHJcbiAgICAgICAgLy8gICAgICAgICAgICBpZiAoc0VudGl0eS5UeXBlID09PSBcIkxvY2F0aW9uXCIpXHJcbiAgICAgICAgLy8gICAgICAgICAgICAgICAgdGhpcy5Mb2NhdGlvbnMucHVzaChuZXcgQ2xpZW50TW9kZWwuTG9jYXRpb24oKS5Mb2FkRnJvbVNlcnZlckVudGl0eShzRW50aXR5KSk7XHJcbiAgICAgICAgLy8gICAgICAgICAgICBlbHNlIGlmIChzRW50aXR5LlR5cGUgPT09IFwiUmVzdGF1cmFudFwiKVxyXG4gICAgICAgIC8vICAgICAgICAgICAgICAgIHRoaXMuUmVzdGF1cmFudHMucHVzaChuZXcgQ2xpZW50TW9kZWwuUmVzdGF1cmFudCgpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNFbnRpdHkpKTtcclxuICAgICAgICAvLyAgICAgICAgICAgIGVsc2UgaWYgKHNFbnRpdHkuVHlwZSA9PT0gXCJTdXBlcm1hcmtldFwiKVxyXG4gICAgICAgIC8vICAgICAgICAgICAgICAgIHRoaXMuU3VwZXJtYXJrZXRzLnB1c2gobmV3IENsaWVudE1vZGVsLlN1cGVybWFya2V0KCkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc0VudGl0eSkpO1xyXG4gICAgICAgIC8vICAgICAgICB9XHJcbiAgICAgICAgdGhpcy5Mb2NhdGlvbnNMb2FkZWQgPSB0cnVlO1xyXG4gICAgICAgIC8vICAgICAgICB0aGlzLkluaXRpYWxpemVNb2RlbCgpO1xyXG4gICAgICAgIC8vICAgIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIEluaXRpYWxpemVNb2RlbCgpIHtcclxuICAgICAgICBpZiAodGhpcy5XYXlwb2ludHNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5XYXlwb2ludENvbm5lY3Rpb25zTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuUGVyc29uc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLkpvYnNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5Ucmlwc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLkFkZHJlc3Nlc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLkltYWdlc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLkFsYnVtc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICAvL3RoaXMuV2F5cG9pbnRUYWNrc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLlRhY2tzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuTG9jYXRpb25zTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuQ3Jld3NMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5Mb2dCb29rRW50cmllc0xvYWRlZCAmJlxyXG4gICAgICAgICAgICB0aGlzLkFsYnVtSW1hZ2VzTG9hZGVkICYmXHJcbiAgICAgICAgICAgIHRoaXMuV2lmaXNMb2FkZWQgJiZcclxuICAgICAgICAgICAgdGhpcy5Db250ZW50UGFnZXNMb2FkZWQpIHtcclxuICAgICAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSm9icygpKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAoZW50aXR5LkFzc2lnbmVkVG9JZCgpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgZW50aXR5LkFzc2lnbmVkVG8odGhpcy5HZXRQZXJzb25CeUlkKGVudGl0eS5Bc3NpZ25lZFRvSWQoKSkpO1xyXG4gICAgICAgICAgICAgICAgaWYgKGVudGl0eS5UcmlwSWQoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgIGVudGl0eS5UcmlwKHRoaXMuR2V0VHJpcEJ5SWQoZW50aXR5LlRyaXBJZCgpKSk7XHJcbiAgICAgICAgICAgICAgICBpZiAoZW50aXR5LlN1cGVySm9iSWQoKSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgZW50aXR5LlN1cGVySm9iKHRoaXMuR2V0Sm9iQnlJZChlbnRpdHkuU3VwZXJKb2JJZCgpKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgZW50aXR5LlN1cGVySm9iKCkuU3ViSm9icy5wdXNoKGVudGl0eSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICAgICAgZW50aXR5LkFsYnVtKHRoaXMuR2V0QWxidW1CeUlkKGVudGl0eS5BbGJ1bUlkKCkpKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5Mb2NhdGlvbnMoKSkge1xyXG4gICAgICAgICAgICAgICAgZW50aXR5LkFkZHJlc3ModGhpcy5HZXRBZGRyZXNzQnlJZChlbnRpdHkuQWRkcmVzc0lkKCkpKTtcclxuICAgICAgICAgICAgICAgIHRoaXMuR2V0SGFyYm91ckJ5SWQoZW50aXR5LkhhcmJvdXJJZCgpKS5Mb2NhdGlvbnMucHVzaChlbnRpdHkpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkFsYnVtSW1hZ2VzKCkpIHtcclxuICAgICAgICAgICAgICAgIHRoaXMuR2V0QWxidW1CeUlkKGVudGl0eS5BbGJ1bUlkKS5JbWFnZXMucHVzaCh0aGlzLkdldEltYWdlQnlJZChlbnRpdHkuSW1hZ2VJZCkpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGZvciAobGV0IGNvbm5lY3Rpb24gb2YgbWFwVmlld01vZGVsLldheXBvaW50Q29ubmVjdGlvbnMoKSkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgcG9seWxpbmUgPSBtYXBWaWV3TW9kZWwuQWRkUG9seWxpbmUoW1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5HZXRXYXlQb2ludEJ5SWQoY29ubmVjdGlvbi5XYXlwb2ludDFJZCksIG1hcFZpZXdNb2RlbFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAuR2V0V2F5UG9pbnRCeUlkKGNvbm5lY3Rpb24uV2F5cG9pbnQySWQpXHJcbiAgICAgICAgICAgICAgICBdKTtcclxuICAgICAgICAgICAgICAgIGFkZER1bW15SGFuZGxlKHBvbHlsaW5lKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBlbnRyeSBvZiBtYXBWaWV3TW9kZWwuTG9nQm9va0VudHJpZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgZW50cnkuU3RhcnQobWFwVmlld01vZGVsLkdldEhhcmJvdXJCeUlkKGVudHJ5LlN0YXJ0SWQoKSkpO1xyXG4gICAgICAgICAgICAgICAgZW50cnkuRW5kKG1hcFZpZXdNb2RlbC5HZXRIYXJib3VyQnlJZChlbnRyeS5FbmRJZCgpKSk7XHJcbiAgICAgICAgICAgICAgICBlbnRyeS5BbGJ1bShtYXBWaWV3TW9kZWwuR2V0QWxidW1CeUlkKGVudHJ5LkFsYnVtSWQoKSkpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGZvciAobGV0IGNyZXcgb2YgbWFwVmlld01vZGVsLkNyZXdzKCkpIHtcclxuICAgICAgICAgICAgICAgIGNvbnN0IGxCRSA9IG1hcFZpZXdNb2RlbC5HZXRMb2dCb29rRW50cnlCeUlkKGNyZXcuVGFja0lkKTtcclxuICAgICAgICAgICAgICAgIGNvbnN0IHRhY2sgPSBtYXBWaWV3TW9kZWwuR2V0VGFja0J5SWQoY3Jldy5UYWNrSWQpO1xyXG4gICAgICAgICAgICAgICAgY29uc3QgdHJpcCA9IG1hcFZpZXdNb2RlbC5HZXRUcmlwQnlJZChjcmV3LlRhY2tJZCk7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwID0gbWFwVmlld01vZGVsLkdldFBlcnNvbkJ5SWQoY3Jldy5QZXJzb25JZCk7XHJcbiAgICAgICAgICAgICAgICBpZiAobEJFICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgbEJFLlBlcnNvbnMucHVzaChwKTtcclxuICAgICAgICAgICAgICAgIGVsc2UgaWYgKHRhY2sgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICB0YWNrLlBlcnNvbnMucHVzaChwKTtcclxuICAgICAgICAgICAgICAgIGVsc2UgaWYgKHRyaXAgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICB0cmlwLlBlcnNvbnMucHVzaChwKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCB3aWZpIG9mIG1hcFZpZXdNb2RlbC5XaWZpcygpKSB7XHJcbiAgICAgICAgICAgICAgICB2YXIgaCA9IG1hcFZpZXdNb2RlbC5HZXRIYXJib3VyQnlJZCh3aWZpLkhhcmJvdXJJZCgpKTtcclxuICAgICAgICAgICAgICAgIGguV2lmaXMucHVzaCh3aWZpKTtcclxuICAgICAgICAgICAgICAgIHdpZmkuSGFyYm91cihoKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBrby5hcHBseUJpbmRpbmdzKG1hcFZpZXdNb2RlbCk7XHJcbiAgICAgICAgICAgICQoXCIjbG9hZGluZ092ZXJsYXlcIikucmVtb3ZlKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIEluaXRpYWxpemVNYXAoKSB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJIZWxwZXIuRGV0YWlsKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgZm9yIChsZXQgd3Agb2YgbWFwVmlld01vZGVsLldheXBvaW50cygpKSB7XHJcbiAgICAgICAgICAgIGlmICh3cC5tYXJrZXIgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5NYXAucmVtb3ZlTGF5ZXIod3AubWFya2VyKTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkNyZWF0ZU1hcmtlcihNYXJrZXJUeXBlLldheXBvaW50LCB3cCk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGZvciAobGV0IGggb2YgbWFwVmlld01vZGVsLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGgubWFya2VyICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKGgubWFya2VyKTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkNyZWF0ZU1hcmtlcihNYXJrZXJUeXBlLkhhcmJvdXIsIGgpO1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCBwIG9mIG1hcFZpZXdNb2RlbC5Qb2x5bGluZXMpIHtcclxuICAgICAgICAgICAgaWYgKHAuRHVtbXlIYW5kbGUubWFya2VyICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKHAuRHVtbXlIYW5kbGUubWFya2VyKTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkNyZWF0ZU1hcmtlcihNYXJrZXJUeXBlLkR1bW15LCBwLkR1bW15SGFuZGxlKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pIHtcclxuICAgICAgICAgICAgZm9yIChsZXQgcCBvZiBtYXBWaWV3TW9kZWwuUG9seWxpbmVzKSB7XHJcbiAgICAgICAgICAgICAgICBwLmFkZFRvKG1hcFZpZXdNb2RlbC5NYXApO1xyXG4gICAgICAgICAgICAgICAgLy9wLmNvbnRleHRtZW51LmVuYWJsZSgpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5NYXAuY29udGV4dG1lbnUuZW5hYmxlKCk7XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgZm9yIChsZXQgcCBvZiBtYXBWaWV3TW9kZWwuUG9seWxpbmVzKSB7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLnJlbW92ZUxheWVyKHApO1xyXG4gICAgICAgICAgICAgICAgLy9wLmNvbnRleHRtZW51LmRpc2FibGUoKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLmNvbnRleHRtZW51LmRpc2FibGUoKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgTWFwOiBMLm1hcGJveC5NYXA7XHJcblxyXG4gICAgR2V0V2F5cG9pbnRCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5XYXlwb2ludCB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gV2F5cG9pbnQgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgICAgICByZXR1cm4gdW5kZWZpbmVkO1xyXG4gICAgfVxyXG5cclxuICAgIEdldEhhcmJvdXJCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5IYXJib3VyIHtcclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBIYXJib3VyIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBHZXRQZXJzb25CeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5QZXJzb24ge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLlBlcnNvbnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gUGVyc29uIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBHZXRKb2JCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5Kb2Ige1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkpvYnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSm9iIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBHZXRUcmlwQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuVHJpcCB7XHJcbiAgICAgICAgZm9yIChsZXQgZW50aXR5IG9mIHRoaXMuVHJpcHMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gVHJpcCB3aXRoIGlkIFwiICsgaWQgKyBcIiBmb3VuZFwiO1xyXG4gICAgICAgIHJldHVybiB1bmRlZmluZWQ7XHJcbiAgICB9XHJcblxyXG4gICAgR2V0QWRkcmVzc0J5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLkFkZHJlc3Mge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkFkZHJlc3NlcygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBBZGRyZXNzIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBHZXRJbWFnZUJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLkltYWdlIHtcclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5JbWFnZXMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICAvL3Rocm93IFwiTm8gSW1hZ2Ugd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgICAgICByZXR1cm4gdW5kZWZpbmVkO1xyXG4gICAgfVxyXG5cclxuICAgIEdldFRhY2tCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5UYWNrIHtcclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5UYWNrcygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBUYWNrIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBHZXRMb2dCb29rRW50cnlCeUlkKGlkOiBudW1iZXIpOiBDbGllbnRNb2RlbC5Mb2dCb29rRW50cnkge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkxvZ0Jvb2tFbnRyaWVzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgLy90aHJvdyBcIk5vIFRhY2sgd2l0aCBpZCBcIiArIGlkICsgXCIgZm91bmRcIjtcclxuICAgICAgICByZXR1cm4gdW5kZWZpbmVkO1xyXG4gICAgfVxyXG5cclxuICAgIEdldEFsYnVtQnlJZChpZDogbnVtYmVyKTogQ2xpZW50TW9kZWwuQWxidW0ge1xyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLkFsYnVtcygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIC8vdGhyb3cgXCJObyBUYWNrIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBHZXRMb2NhdGlvbkJ5SWQoaWQ6IG51bWJlcik6IENsaWVudE1vZGVsLkxvY2F0aW9uIHtcclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5Mb2NhdGlvbnMoKSkge1xyXG4gICAgICAgICAgICBpZiAoZW50aXR5LklkKCkgPT09IGlkKSByZXR1cm4gZW50aXR5O1xyXG4gICAgICAgIH1cclxuICAgICAgICBmb3IgKGxldCBlbnRpdHkgb2YgdGhpcy5SZXN0YXVyYW50cygpKSB7XHJcbiAgICAgICAgICAgIGlmIChlbnRpdHkuSWQoKSA9PT0gaWQpIHJldHVybiBlbnRpdHk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIGZvciAobGV0IGVudGl0eSBvZiB0aGlzLlJlc3RhdXJhbnRzKCkpIHtcclxuICAgICAgICAgICAgaWYgKGVudGl0eS5JZCgpID09PSBpZCkgcmV0dXJuIGVudGl0eTtcclxuICAgICAgICB9XHJcbiAgICAgICAgLy90aHJvdyBcIk5vIExvY2F0aW9uIHdpdGggaWQgXCIgKyBpZCArIFwiIGZvdW5kXCI7XHJcbiAgICAgICAgcmV0dXJuIHVuZGVmaW5lZDtcclxuICAgIH1cclxuXHJcbiAgICBXYXlwb2ludHNMb2FkZWQgPSBmYWxzZTtcclxuICAgIFdheXBvaW50Q29ubmVjdGlvbnNMb2FkZWQgPSBmYWxzZTtcclxuICAgIFBlcnNvbnNMb2FkZWQgPSBmYWxzZTtcclxuICAgIEpvYnNMb2FkZWQgPSBmYWxzZTtcclxuICAgIFRyaXBzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBBZGRyZXNzZXNMb2FkZWQgPSBmYWxzZTtcclxuICAgIEltYWdlc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgQWxidW1zTG9hZGVkID0gZmFsc2U7XHJcbiAgICBXYXlwb2ludFRhY2tzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBUYWNrc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgTG9jYXRpb25zTG9hZGVkID0gZmFsc2U7XHJcbiAgICBBbGJ1bUltYWdlc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgTG9nQm9va0VudHJpZXNMb2FkZWQgPSBmYWxzZTtcclxuICAgIENyZXdzTG9hZGVkID0gZmFsc2U7XHJcbiAgICBXaWZpc0xvYWRlZCA9IGZhbHNlO1xyXG4gICAgQ29udGVudFBhZ2VzTG9hZGVkID0gZmFsc2U7XHJcblxyXG4gICAgV2F5cG9pbnRzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLldheXBvaW50PigpO1xyXG4gICAgV2F5cG9pbnRDb25uZWN0aW9ucyA9IGtvLm9ic2VydmFibGVBcnJheTxTZXJ2ZXJNb2RlbC5XYXlwb2ludENvbm5lY3Rpb24+KCk7XHJcbiAgICBIYXJib3VycyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5IYXJib3VyPigpO1xyXG4gICAgUGVyc29ucyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5QZXJzb24+KCk7XHJcbiAgICBKb2JzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkpvYj4oKTtcclxuICAgIFRyaXBzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLlRyaXA+KCk7XHJcbiAgICBBZGRyZXNzZXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuQWRkcmVzcz4oKTtcclxuICAgIEltYWdlcyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5JbWFnZT4oKTtcclxuICAgIFRhY2tzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLlRhY2s+KCk7XHJcbiAgICBMb2NhdGlvbnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuTG9jYXRpb24+KCk7XHJcbiAgICBTdXBlcm1hcmtldHMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Q2xpZW50TW9kZWwuU3VwZXJtYXJrZXQ+KCk7XHJcbiAgICBSZXN0YXVyYW50cyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5SZXN0YXVyYW50PigpO1xyXG4gICAgQWxidW1zID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkFsYnVtPigpO1xyXG4gICAgQWxidW1JbWFnZXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8U2VydmVyTW9kZWwuQWxidW1JbWFnZT4oKTtcclxuICAgIExvZ0Jvb2tFbnRyaWVzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkxvZ0Jvb2tFbnRyeT4oKTtcclxuICAgIENyZXdzID0ga28ub2JzZXJ2YWJsZUFycmF5PFNlcnZlck1vZGVsLkNyZXc+KCk7XHJcbiAgICBXaWZpcyA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5XaWZpPigpO1xyXG4gICAgQ29udGVudFBhZ2VzID0ga28ub2JzZXJ2YWJsZUFycmF5PENsaWVudE1vZGVsLkNvbnRlbnRQYWdlPigpO1xyXG5cclxuICAgIFdheXBvaW50SGVscGVyID0gbmV3IEVkaXRpbmdIZWxwZXIoXCJlZGl0aW5nV2F5cG9pbnRNb2RhbFwiLFxyXG4gICAgICAgIFwiZGVsZXRpbmdXYXlwb2ludE1vZGFsXCIsXHJcbiAgICAgICAgKCkgPT4gdGhpcy5DcmVhdGVXYXlwb2ludChNYXJrZXJUeXBlLldheXBvaW50KSxcclxuICAgICAgICB0aGlzLldheXBvaW50cyk7XHJcbiAgICBIYXJib3VySGVscGVyID0gbmV3IEVkaXRpbmdIZWxwZXIoXCJlZGl0aW5nSGFyYm91ck1vZGFsXCIsXHJcbiAgICAgICAgXCJkZWxldGluZ0hhcmJvdXJNb2RhbFwiLFxyXG4gICAgICAgICgpID0+IHRoaXMuQ3JlYXRlSGFyYm91cigpLFxyXG4gICAgICAgIHRoaXMuSGFyYm91cnMsXHJcbiAgICAgICAgcmlnaHRTaWRlYmFyKTtcclxuICAgIFBlcnNvbkhlbHBlciA9IG5ldyBFZGl0aW5nSGVscGVyKFwiZWRpdGluZ1BlcnNvbk1vZGFsXCIsXHJcbiAgICAgICAgXCJkZWxldGluZ1BlcnNvbk1vZGFsXCIsXHJcbiAgICAgICAgKCkgPT4gbmV3IENsaWVudE1vZGVsLlBlcnNvbigpLFxyXG4gICAgICAgIHRoaXMuUGVyc29ucyk7XHJcbiAgICBKb2JIZWxwZXIgPSBuZXcgRWRpdGluZ0hlbHBlcihcImVkaXRpbmdKb2JNb2RhbFwiLCBcImRlbGV0aW5nSm9iTW9kYWxcIiwgKCkgPT4gbmV3IENsaWVudE1vZGVsLkpvYigpLCB0aGlzLkpvYnMpO1xyXG4gICAgVHJpcEhlbHBlciA9IG5ldyBFZGl0aW5nSGVscGVyKFwiZWRpdGluZ1RyaXBNb2RhbFwiLCBcImRlbGV0aW5nVHJpcE1vZGFsXCIsICgpID0+IG5ldyBDbGllbnRNb2RlbC5UcmlwKCksIHRoaXMuVHJpcHMpO1xyXG4gICAgQWRkcmVzc0hlbHBlciA9IG5ldyBFZGl0aW5nSGVscGVyKFwiZWRpdGluZ0FkZHJlc3NNb2RhbFwiLFxyXG4gICAgICAgIFwiZGVsZXRpbmdBZGRyZXNzTW9kYWxcIixcclxuICAgICAgICAoKSA9PiBuZXcgQ2xpZW50TW9kZWwuQWRkcmVzcygpLFxyXG4gICAgICAgIHRoaXMuQWRkcmVzc2VzKTtcclxuICAgIEltYWdlSGVscGVyID0gbmV3IEVkaXRpbmdIZWxwZXIoXCJlZGl0aW5nSW1hZ2VNb2RhbFwiLFxyXG4gICAgICAgIFwiZGVsZXRpbmdJbWFnZU1vZGFsXCIsXHJcbiAgICAgICAgKCkgPT4gbmV3IENsaWVudE1vZGVsLkltYWdlKCksXHJcbiAgICAgICAgdGhpcy5JbWFnZXMpO1xyXG4gICAgVGFja0hlbHBlciA9IG5ldyBFZGl0aW5nSGVscGVyKFwiZWRpdGluZ1RhY2tNb2RhbFwiLCBcImRlbGV0aW5nVGFja01vZGFsXCIsICgpID0+IG5ldyBDbGllbnRNb2RlbC5UYWNrKCksIHRoaXMuVGFja3MpO1xyXG4gICAgTG9jYXRpb25IZWxwZXIgPSBuZXcgRWRpdGluZ0hlbHBlcihcImVkaXRpbmdMb2NhdGlvbk1vZGFsXCIsXHJcbiAgICAgICAgXCJkZWxldGluZ0xvY2F0aW9uTW9kYWxcIixcclxuICAgICAgICAoKSA9PiBuZXcgQ2xpZW50TW9kZWwuTG9jYXRpb24oKSxcclxuICAgICAgICB0aGlzLkxvY2F0aW9ucyk7XHJcbiAgICBTdXBlcm1hcmtldEhlbHBlciA9IG5ldyBFZGl0aW5nSGVscGVyKFwiZWRpdGluZ1N1cGVybWFya2V0TW9kYWxcIixcclxuICAgICAgICBcImRlbGV0aW5nU3VwZXJtYXJrZXRNb2RhbFwiLFxyXG4gICAgICAgICgpID0+IG5ldyBDbGllbnRNb2RlbC5TdXBlcm1hcmtldCgpLFxyXG4gICAgICAgIHRoaXMuU3VwZXJtYXJrZXRzKTtcclxuICAgIFJlc3RhdXJhbnRIZWxwZXIgPSBuZXcgRWRpdGluZ0hlbHBlcihcImVkaXRpbmdSZXN0YXVyYW50TW9kYWxcIixcclxuICAgICAgICBcImRlbGV0aW5nUmVzdGF1cmFudE1vZGFsXCIsXHJcbiAgICAgICAgKCkgPT4gbmV3IENsaWVudE1vZGVsLlJlc3RhdXJhbnQoKSxcclxuICAgICAgICB0aGlzLlJlc3RhdXJhbnRzKTtcclxuICAgIExvZ0Jvb2tFbnRyeUhlbHBlciA9IG5ldyBFZGl0aW5nSGVscGVyKFwiZWRpdGluZ0xvZ0Jvb2tFbnRyeU1vZGFsXCIsXHJcbiAgICAgICAgXCJkZWxldGluZ0xvZ0Jvb2tFbnRyeU1vZGFsXCIsXHJcbiAgICAgICAgKCkgPT4ge1xyXG4gICAgICAgICAgICBjb25zdCBsb2dCb29rRW50cnkgPSBuZXcgQ2xpZW50TW9kZWwuTG9nQm9va0VudHJ5KCk7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLkxvZ0Jvb2tFbnRyaWVzKCkubGVuZ3RoID4gMCkge1xyXG4gICAgICAgICAgICAgICAgbGV0IGxhc3RFbnRyeSA9IHRoaXMuTG9nQm9va0VudHJpZXMoKVswXTtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGVudHJ5IG9mIHRoaXMuTG9nQm9va0VudHJpZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChuZXcgRGF0ZShlbnRyeS5FbmREYXRlKCkpID4gbmV3IERhdGUobGFzdEVudHJ5LkVuZERhdGUoKSkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGxhc3RFbnRyeSA9IGVudHJ5O1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgbG9nQm9va0VudHJ5LlN0YXJ0KGxhc3RFbnRyeS5FbmQoKSk7XHJcbiAgICAgICAgICAgICAgICBsb2dCb29rRW50cnkuTW90b3JIb3Vyc1N0YXJ0KGxhc3RFbnRyeS5Nb3RvckhvdXJzRW5kKCkpO1xyXG4gICAgICAgICAgICAgICAgbG9nQm9va0VudHJ5LkxvZ1N0YXJ0KGxhc3RFbnRyeS5Mb2dFbmQoKSk7XHJcbiAgICAgICAgICAgICAgICBpZiAobGFzdEVudHJ5LkVuZCgpLk5hbWUoKSAhPT0gXCJMaXBwZVwiKVxyXG4gICAgICAgICAgICAgICAgICAgIGxvZ0Jvb2tFbnRyeS5QZXJzb25zKGxhc3RFbnRyeS5QZXJzb25zKCkuc2xpY2UoKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIGxvZ0Jvb2tFbnRyeTtcclxuICAgICAgICB9LFxyXG4gICAgICAgIHRoaXMuTG9nQm9va0VudHJpZXMsXHJcbiAgICAgICAgXCJkZXRhaWxlZExvZ0Jvb2tFbnRyeU1vZGFsXCIpO1xyXG4gICAgQ29udGVudFBhZ2VIZWxwZXIgPSBuZXcgRWRpdGluZ0hlbHBlcihcImVkaXRpbmdDb250ZW50UGFnZU1vZGFsXCIsXHJcbiAgICAgICAgXCJkZWxldGluZ0NvbnRlbnRQYWdlTW9kYWxcIixcclxuICAgICAgICAoKSA9PiBuZXcgQ2xpZW50TW9kZWwuQ29udGVudFBhZ2UoKSxcclxuICAgICAgICB0aGlzLkNvbnRlbnRQYWdlcyxcclxuICAgICAgICBcImRldGFpbGVkQ29udGVudFBhZ2VNb2RhbFwiKTtcclxuICAgIFdpZmlIZWxwZXIgPSBuZXcgRWRpdGluZ0hlbHBlcihcImVkaXRpbmdXaWZpTW9kYWxcIixcclxuICAgICAgICBcImRlbGV0aW5nV2lmaU1vZGFsXCIsXHJcbiAgICAgICAgKCkgPT4ge1xyXG4gICAgICAgICAgICBjb25zdCB3ID0gbmV3IENsaWVudE1vZGVsLldpZmkoKTtcclxuICAgICAgICAgICAgdy5IYXJib3VySWQobWFwVmlld01vZGVsLkhhcmJvdXJIZWxwZXIuRGV0YWlsKCkuSWQoKSk7XHJcbiAgICAgICAgICAgIHJldHVybiB3O1xyXG4gICAgICAgIH0sXHJcbiAgICAgICAgdGhpcy5XaWZpcyxcclxuICAgICAgICBcImRldGFpbFdpZmlNb2RhbFwiKTtcclxuXHJcbiAgICBIYXJib3Vyc0J5TmFtZSA9IGtvLmNvbXB1dGVkPEhhcmJvdXJbXT4oKCkgPT4gdGhpcy5IYXJib3Vycy5zb3J0KChoMSwgaDIpID0+IGgxLk5hbWUoKSA+IGgyLk5hbWUoKSA/IDEgOiAtMSkoKSk7XHJcbiAgICBIYXJib3Vyc0J5RGlzdGFuY2UgPSBrby5jb21wdXRlZDxIYXJib3VyW10+KCgpID0+IHRoaXMuSGFyYm91cnMuc29ydCgoaDEsIGgyKSA9PiBoMS5EaXN0YW5jZSgpIC0gaDIuRGlzdGFuY2UoKSkoKSk7XHJcbiAgICBMb2dCb29rRW50cmllc0J5U3RhcnREYXRlID0ga29cclxuICAgICAgICAuY29tcHV0ZWQ8Q2xpZW50TW9kZWwuTG9nQm9va0VudHJ5W10+KCgpID0+IHRoaXMuTG9nQm9va0VudHJpZXNcclxuICAgICAgICAgICAgLnNvcnQoKGwxLCBsMikgPT4gRGF0ZS5wYXJzZShsMS5TdGFydERhdGUoKSkgLSBEYXRlLnBhcnNlKGwyLlN0YXJ0RGF0ZSgpKSkoKSk7XHJcblxyXG5cclxuICAgIC8vU29ydGVkTG9nQm9va0VudHJpZXMgPSBrby5jb21wdXRlZCh7XHJcbiAgICAvLyAgICByZWFkOiAoKSA9PiB0aGlzLkxvZ0Jvb2tFbnRyaWVzLnNvcnQoKGwxLCBsMikgPT4ge1xyXG4gICAgLy8gICAgICAgIHZhciB0MSA9IGwxLlN0YXJ0RGF0ZSgpLmdldFRpbWUoKTtcclxuICAgIC8vICAgICAgICB2YXIgdDIgPSBsMi5TdGFydERhdGUoKS5nZXRUaW1lKCk7XHJcbiAgICAvLyAgICAgICAgcmV0dXJuIHQyIC0gdDE7XHJcbiAgICAvLyAgICB9KSxcclxuICAgIC8vICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgLy99KTtcclxuXHJcbiAgICBJbml0R2FsbGVyeShpdGVtOiBDbGllbnRNb2RlbC5JbWFnZSwgZXZlbnQ6IEpRdWVyeUV2ZW50T2JqZWN0KSB7XHJcbiAgICAgICAgY29uc3QgaXRlbXMgPSBuZXcgQXJyYXk8UGhvdG9Td2lwZS5JdGVtPigpO1xyXG4gICAgICAgIGNvbnN0IGFsYnVtRWxlbSA9IGV2ZW50LnRhcmdldC5wYXJlbnRFbGVtZW50O1xyXG4gICAgICAgIGNvbnN0IGN1cnJJbWFnZTogQ2xpZW50TW9kZWwuSW1hZ2UgPSB0aGlzIGFzIGFueTtcclxuICAgICAgICBmb3IgKGxldCBkYXRhIG9mIG1hcFZpZXdNb2RlbC5BbGJ1bVN0YWNrKClbMF0uSW1hZ2VzKCkpIHtcclxuICAgICAgICAgICAgaXRlbXMucHVzaCgoe1xyXG4gICAgICAgICAgICAgICAgaDogZGF0YS5IZWlnaHQoKSxcclxuICAgICAgICAgICAgICAgIHc6IGRhdGEuV2lkdGgoKSxcclxuICAgICAgICAgICAgICAgIHNyYzogZGF0YS5QYXRoKClcclxuICAgICAgICAgICAgfSBhcyBhbnkpKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgZ2FsbGVyeSA9IG5ldyBQaG90b1N3aXBlKHBzd3AsXHJcbiAgICAgICAgICAgIFBob3RvU3dpcGVVSV9EZWZhdWx0LFxyXG4gICAgICAgICAgICBpdGVtcyxcclxuICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgaW5kZXg6IG1hcFZpZXdNb2RlbC5BbGJ1bVN0YWNrKClbMF0uSW1hZ2VzLmluZGV4T2YoY3VyckltYWdlKSBhcyBudW1iZXIsXHJcbiAgICAgICAgICAgICAgICBnZXRUaHVtYkJvdW5kc0ZuOiAoaW5kZXg6IG51bWJlcik6IHsgeDogbnVtYmVyOyB5OiBudW1iZXI7IHc6IG51bWJlciB9ID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCBlbGVtID0gJChcImltZ1wiLCBhbGJ1bUVsZW0pW2luZGV4XTtcclxuICAgICAgICAgICAgICAgICAgICB2YXIgcGFkZGluZyA9IHBhcnNlRmxvYXQod2luZG93LmdldENvbXB1dGVkU3R5bGUoZWxlbSwgbnVsbClcclxuICAgICAgICAgICAgICAgICAgICAgICAgLmdldFByb3BlcnR5VmFsdWUoXCJwYWRkaW5nLWxlZnRcIilcclxuICAgICAgICAgICAgICAgICAgICAgICAgLnJlcGxhY2UoXCJweFwiLCBcIlwiKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgZWxlbS5zY3JvbGxJbnRvVmlldyhmYWxzZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgYm91bmRzID0gZWxlbS5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTtcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB4OiBib3VuZHMubGVmdCArIHBhZGRpbmcsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHk6IGJvdW5kcy50b3AgKyB3aW5kb3cuc2NyZWVuWSArIHBhZGRpbmcsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHc6IGJvdW5kcy53aWR0aCAtICgyICogcGFkZGluZylcclxuICAgICAgICAgICAgICAgICAgICB9O1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICBnYWxsZXJ5LmluaXQoKTtcclxuICAgIH1cclxuXHJcbiAgICBBZGRIYXJib3VyKCk6IHZvaWQge1xyXG4gICAgICAgIGNvbnN0IGhhcmJvdXIgPSBtYXBWaWV3TW9kZWwuQ3JlYXRlSGFyYm91cihgSGFmZW4gJHt0aGlzLkhhcmJvdXJzLmxlbmd0aH1gLCB0aGlzLk1hcC5nZXRDZW50ZXIoKSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkhhcmJvdXJzLnB1c2goaGFyYm91cik7XHJcbiAgICAgICAgaGFyYm91ci5TYXZlVG9TZXJ2ZXIoKTtcclxuICAgIH1cclxuXHJcbiAgICBSZW1vdmVIYXJib3VyID0gKCkgPT4ge1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5IYXJib3VySGVscGVyLkRldGFpbCgpLkRlbGV0ZU9uU2VydmVyKCk7XHJcbiAgICB9O1xyXG4gICAgUmVtb3ZlV2F5cG9pbnQgPSAoKSA9PiB7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLldheXBvaW50SGVscGVyLkRldGFpbCgpLkRlbGV0ZU9uU2VydmVyKCk7XHJcbiAgICB9O1xyXG5cclxuICAgIC8vQ29weUhhcmJvdXIoaDE6IEhhcmJvdXIsIGgyOiBIYXJib3VyKTogdm9pZCB7XHJcbiAgICAvLyAgICB0aGlzLkNvcHlXYXlwb2ludChoMSwgaDIpO1xyXG4gICAgLy99XHJcblxyXG4gICAgLy9Db3B5V2F5cG9pbnQodzE6IFdheXBvaW50LCB3MjogV2F5cG9pbnQpIHtcclxuICAgIC8vICAgIHcyLldheXBvaW50TnVtYmVyKHcxLldheXBvaW50TnVtYmVyKCkpO1xyXG4gICAgLy8gICAgdzIuTGF0aXR1ZGUodzEuTGF0aXR1ZGUoKSk7XHJcbiAgICAvLyAgICB3Mi5Mb25naXR1ZGUodzEuTG9uZ2l0dWRlKCkpO1xyXG4gICAgLy8gICAgdzIuTmFtZSh3MS5OYW1lKCkpO1xyXG4gICAgLy8gICAgdzIuRGVzY3JpcHRpb24odzEuRGVzY3JpcHRpb24oKSk7XHJcbiAgICAvL31cclxuXHJcbiAgICBBZGRQb2x5bGluZSh3YXlwb2ludD86IFdheXBvaW50KTogTC5Qb2x5bGluZTtcclxuICAgIEFkZFBvbHlsaW5lKHdheXBvaW50cz86IFdheXBvaW50W10pOiBMLlBvbHlsaW5lO1xyXG4gICAgQWRkUG9seWxpbmUoYXJnPyk6IEwuUG9seWxpbmUge1xyXG4gICAgICAgIC8vdmFyIG9wdGlvbnMgPSB7XHJcbiAgICAgICAgLy8gICAgY29udGV4dG1lbnU6IHRydWUsXHJcbiAgICAgICAgLy8gICAgY29udGV4dG1lbnVJbmhlcml0SXRlbXM6IGZhbHNlLFxyXG4gICAgICAgIC8vICAgIGNvbnRleHRtZW51SXRlbXM6IFtcclxuICAgICAgICAvLyAgICAgICAge1xyXG4gICAgICAgIC8vICAgICAgICAgICAgdGV4dDogXCJGRkZGRkZGRkZGRkZGRkZGRkZcIixcclxuICAgICAgICAvLyAgICAgICAgICAgIGNhbGxiYWNrOiBmdW5jdGlvbigpIHsgY29uc29sZS5sb2codGhpcyk7XHJcbiAgICAgICAgLy8gICAgICAgICAgICAgICAgY29uc29sZS5sb2coYXJndW1lbnRzKTttYXBWaWV3TW9kZWwuSGFyYm91ckhlbHBlci5FZGl0aW5nKHRoaXMpIH1cclxuICAgICAgICAvLyAgICAgICAgfSxcclxuICAgICAgICAvLyAgICAgICAge1xyXG4gICAgICAgIC8vICAgICAgICAgICAgdGV4dDogXCJMw7ZzY2hlblwiLFxyXG4gICAgICAgIC8vICAgICAgICAgICAgY2FsbGJhY2s6IGZ1bmN0aW9uKCkgeyBtYXBWaWV3TW9kZWwuSGFyYm91ckhlbHBlci5EZWxldGluZyh0aGlzKSB9XHJcbiAgICAgICAgLy8gICAgICAgIH1cclxuICAgICAgICAvLyAgICBdXHJcbiAgICAgICAgLy99O1xyXG5cclxuICAgICAgICBjb25zdCBwb2x5bGluZSA9IG5ldyBMLlBvbHlsaW5lKFtdKTtcclxuXHJcbiAgICAgICAgLy9wb2x5bGluZS5iaW5kQ29udGV4dE1lbnUob3B0aW9ucyk7XHJcblxyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5Qb2x5bGluZXMucHVzaChwb2x5bGluZSk7XHJcbiAgICAgICAgcG9seWxpbmUuYWRkRXZlbnRMaXN0ZW5lcihcImNsaWNrXCIsXHJcbiAgICAgICAgICAgIChlOiBMLkxlYWZsZXRNb3VzZUV2ZW50KSA9PiB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwMSA9IG1hcFZpZXdNb2RlbC5NYXAubGF0TG5nVG9Db250YWluZXJQb2ludChwb2x5bGluZS5nZXRMYXRMbmdzKClbMF0pO1xyXG4gICAgICAgICAgICAgICAgY29uc3QgcDIgPSBtYXBWaWV3TW9kZWwuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQocG9seWxpbmUuZ2V0TGF0TG5ncygpWzFdKTtcclxuICAgICAgICAgICAgICAgIHBvbHlsaW5lLkR1bW15SGFuZGxlXHJcbiAgICAgICAgICAgICAgICAgICAgLlNldExhdExuZyhtYXBWaWV3TW9kZWwuTWFwXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIC5jb250YWluZXJQb2ludFRvTGF0TG5nKEwuTGluZVV0aWxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5jbG9zZXN0UG9pbnRPblNlZ21lbnQoZS5jb250YWluZXJQb2ludCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHAxLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgcDIpKSxcclxuICAgICAgICAgICAgICAgICAgICBmYWxzZSk7XHJcblxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLldheXBvaW50cy5wdXNoKHBvbHlsaW5lLkR1bW15SGFuZGxlKTtcclxuICAgICAgICAgICAgICAgIHBvbHlsaW5lLkR1bW15SGFuZGxlLmNvbnZlcnRGcm9tRHVtbXlIYW5kbGUoKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pXHJcbiAgICAgICAgICAgIHBvbHlsaW5lLmFkZFRvKHRoaXMuTWFwKTtcclxuICAgICAgICBwb2x5bGluZS5XYXlwb2ludHMgPSBuZXcgQXJyYXkoKTtcclxuICAgICAgICBpZiAoYXJnICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgIGlmIChhcmcgaW5zdGFuY2VvZiBXYXlwb2ludClcclxuICAgICAgICAgICAgICAgIChhcmcgYXMgV2F5cG9pbnQpLkFkZFRvUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCB3YXlwb2ludCBvZiBhcmcgYXMgV2F5cG9pbnRbXSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHdheXBvaW50LkFkZFRvUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgIHBvbHlsaW5lLmFkZEV2ZW50TGlzdGVuZXIoXCJtb3VzZW92ZXJcIixcclxuICAgICAgICAgICAgKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkhvdmVyZWRQb2x5aW5lID0gcG9seWxpbmU7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIHJldHVybiBwb2x5bGluZTtcclxuICAgIH1cclxuXHJcblxyXG4gICAgTWFwTW9kZSA9IGtvLm9ic2VydmFibGU8TWFwTW9kZT4oKTtcclxuICAgIERyYXdpbmdMYXRMbmc6IEwuTGF0TG5nO1xyXG4gICAgRHJhd2luZ1NvdXJjZVdheXBvaW50OiBXYXlwb2ludDtcclxuICAgIERyYXdpbmdUYXJnZXRXYXlwb2ludDogV2F5cG9pbnQ7XHJcbiAgICBSZW1vdmVQb2x5bGluZSA9IChwb2x5bGluZTogTC5Qb2x5bGluZSkgPT4ge1xyXG4gICAgICAgIHRoaXMuTWFwLnJlbW92ZUxheWVyKHBvbHlsaW5lKTtcclxuICAgICAgICB0aGlzLkRyYXdpbmdQb2x5bGluZSA9IHVuZGVmaW5lZDtcclxuICAgIH07XHJcblxyXG4gICAgR2V0TWFwTW9kZSgpOiBNYXBNb2RlIHtcclxuICAgICAgICBpZiAodGhpcy5EcmF3aW5nUG9seWxpbmUgIT09IHVuZGVmaW5lZCAmJiB0aGlzLkRyYXdpbmdMYXRMbmcgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgcmV0dXJuIE1hcE1vZGUuUm91dGVEcmF3aW5nO1xyXG4gICAgICAgIHJldHVybiB0aGlzLk1hcE1vZGUoKTtcclxuICAgIH1cclxuXHJcbiAgICBHZXRXYXlQb2ludEJ5SWQoaWQ6IG51bWJlcik6IFdheXBvaW50IHtcclxuICAgICAgICBmb3IgKGxldCB3YXlwb2ludCBvZiB0aGlzLldheXBvaW50cygpKSB7XHJcbiAgICAgICAgICAgIGlmICh3YXlwb2ludC5JZCgpID09PSBpZClcclxuICAgICAgICAgICAgICAgIHJldHVybiB3YXlwb2ludDtcclxuICAgICAgICB9XHJcbiAgICAgICAgZm9yIChsZXQgd2F5cG9pbnQgb2YgdGhpcy5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgIGlmICh3YXlwb2ludC5JZCgpID09PSBpZClcclxuICAgICAgICAgICAgICAgIHJldHVybiB3YXlwb2ludDtcclxuICAgICAgICB9XHJcbiAgICAgICAgdGhyb3cgYE5vIFdheXBvaW50IHdpdGggaWQgJHtpZH0gaW4gbW9kZWxgO1xyXG4gICAgfVxyXG5cclxuICAgIENhbGN1bGF0ZURpc3RhbmNlcyhzdGFydCA9IG1hcFZpZXdNb2RlbC5IYXJib3VySGVscGVyLkRldGFpbCgpLCB0YXJnZXQ/OiBDbGllbnRNb2RlbC5XYXlwb2ludCkge1xyXG5cclxuICAgICAgICBjb25zdCB3YXlwb2ludHM6IEFycmF5PFdheXBvaW50PiA9IFtzdGFydF07XHJcbiAgICAgICAgY29uc3QgY2FsY3VsYXRpbmcgPSBuZXcgQXJyYXk8V2F5cG9pbnREaXN0YW5jZT4oKTtcclxuICAgICAgICBjb25zdCBjYWxjdWxhdGVkID0gbmV3IEFycmF5PFdheXBvaW50RGlzdGFuY2U+KCk7XHJcbiAgICAgICAgY29uc3QgY2FsY3VsYXRlUm91dGUgPSB0YXJnZXQgIT09IHVuZGVmaW5lZDtcclxuICAgICAgICBjYWxjdWxhdGluZy5wdXNoKG5ldyBXYXlwb2ludERpc3RhbmNlKHVuZGVmaW5lZCwgc3RhcnQsIDAsIHdheXBvaW50cywgY2FsY3VsYXRlUm91dGUpKTtcclxuICAgICAgICAvL2ZvciAobGV0IHdheXBvaW50IG9mIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMoKSkge1xyXG4gICAgICAgIC8vICAgIHdheXBvaW50cy5wdXNoKG5ldyBXYXlwb2ludERpc3RhbmNlKG51bGwsIHdheXBvaW50LCBOdW1iZXIuUE9TSVRJVkVfSU5GSU5JVFkpKTtcclxuICAgICAgICAvL31cclxuICAgICAgICAvL2ZvciAobGV0IGhhcmJvdXIgb2YgbWFwVmlld01vZGVsLkhhcmJvdXJzKCkpIHtcclxuICAgICAgICAvLyAgICBpZiAoaGFyYm91ciAhPT0gc3RhcnQpIHtcclxuICAgICAgICAvLyAgICAgICAgd2F5cG9pbnRzLnB1c2gobmV3IFdheXBvaW50RGlzdGFuY2UobnVsbCwgaGFyYm91ciwgTnVtYmVyLlBPU0lUSVZFX0lORklOSVRZKSk7XHJcbiAgICAgICAgLy8gICAgfVxyXG4gICAgICAgIC8vfVxyXG4gICAgICAgIGlmIChjYWxjdWxhdGVSb3V0ZSkge1xyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgICAgIHdwLlJvdXRlUHJlY2Vzc29yKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgZm9yIChsZXQgaCBvZiBtYXBWaWV3TW9kZWwuSGFyYm91cnMoKSkge1xyXG4gICAgICAgICAgICAgICAgaC5Sb3V0ZVByZWNlc3Nvcih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgbWFwVmlld01vZGVsLldheXBvaW50cygpKSB7XHJcbiAgICAgICAgICAgICAgICB3cC5QcmVjZXNzb3IodW5kZWZpbmVkKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBmb3IgKGxldCBoIG9mIG1hcFZpZXdNb2RlbC5IYXJib3VycygpKSB7XHJcbiAgICAgICAgICAgICAgICBoLlByZWNlc3Nvcih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgICAgIHdoaWxlIChjYWxjdWxhdGluZy5sZW5ndGggPiAwKSB7XHJcbiAgICAgICAgICAgIGxldCBtaW5pbWFsRGlzdCA9IE51bWJlci5QT1NJVElWRV9JTkZJTklUWTtcclxuICAgICAgICAgICAgbGV0IG1pbmltYWxXcDogV2F5cG9pbnREaXN0YW5jZTtcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgY2FsY3VsYXRpbmcpIHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGNXcCBvZiB3cC5Db25uZWN0ZWRXYXlQb2ludHMpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoKGNhbGN1bGF0ZVJvdXRlID8gY1dwLlJvdXRlUHJlY2Vzc29yKCkgOiBjV3AuUHJlY2Vzc29yKCkpICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheSh3cC5Db25uZWN0ZWRXYXlQb2ludHMsIGNXcCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICBpZiAod3AuQ29ubmVjdGVkV2F5UG9pbnRzLmxlbmd0aCA9PT0gMCkge1xyXG4gICAgICAgICAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheShjYWxjdWxhdGluZywgd3ApO1xyXG4gICAgICAgICAgICAgICAgICAgIGNhbGN1bGF0ZWQucHVzaCh3cCk7XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IGRpc3QgPSB3cC5EaXN0YW5jZSArIHdwLkNvbm5lY3RlZFdheVBvaW50c1swXS5MYXRMbmcuZGlzdGFuY2VUbyh3cC5MYXRMbmcpIC8gMS44NTI7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKGRpc3QgPCBtaW5pbWFsRGlzdCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBtaW5pbWFsRGlzdCA9IGRpc3Q7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1pbmltYWxXcCA9IHdwO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBpZiAobWluaW1hbFdwICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGNhbGN1bGF0aW5nLnB1c2gobmV3IFdheXBvaW50RGlzdGFuY2UobWluaW1hbFdwLldheXBvaW50LFxyXG4gICAgICAgICAgICAgICAgICAgIG1pbmltYWxXcC5Db25uZWN0ZWRXYXlQb2ludHMuc2hpZnQoKSxcclxuICAgICAgICAgICAgICAgICAgICBtaW5pbWFsRGlzdCxcclxuICAgICAgICAgICAgICAgICAgICB3YXlwb2ludHMsXHJcbiAgICAgICAgICAgICAgICAgICAgY2FsY3VsYXRlUm91dGUpKTtcclxuICAgICAgICAgICAgICAgIC8vaWYgKG1pbmltYWxXUC5XYXlwb2ludCA9PT0gdGFyZ2V0KVxyXG4gICAgICAgICAgICAgICAgLy8gICAgYnJlYWs7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICAgICAgaWYgKGNhbGN1bGF0ZVJvdXRlKVxyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBjYWxjdWxhdGVkKSB7XHJcbiAgICAgICAgICAgICAgICB3cC5XYXlwb2ludC5Sb3V0ZURpc3RhbmNlKE1hdGgucm91bmQod3AuRGlzdGFuY2UgLyAxMDApIC8gMTApO1xyXG4gICAgICAgICAgICAgICAgLy93cC5XYXlwb2ludC5QcmVjZXNzb3Iod3AuUHJlY2Vzc29yKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIGVsc2VcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgY2FsY3VsYXRlZCkge1xyXG4gICAgICAgICAgICAgICAgd3AuV2F5cG9pbnQuRGlzdGFuY2UoTWF0aC5yb3VuZCh3cC5EaXN0YW5jZSAvIDEwMCkgLyAxMCk7XHJcbiAgICAgICAgICAgICAgICAvL3dwLldheXBvaW50LlByZWNlc3Nvcih3cC5QcmVjZXNzb3IpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgcHJpdmF0ZSBoaWdobGlnaHRlZFJvdXRlOiBMLlBvbHlsaW5lO1xyXG4gICAgcHJpdmF0ZSByb3V0ZUZpeGVkID0gZmFsc2U7XHJcbiAgICBwcml2YXRlIHByZXZpb3VzQm91bmRzOiBMLkxhdExuZ0JvdW5kcztcclxuICAgIHByaXZhdGUgbm9SZXZlcnRUb1ByZXZpb3VzQm91bmRzID0gZmFsc2U7XHJcblxyXG4gICAgU2hvd1JvdXRlKGg6IENsaWVudE1vZGVsLldheXBvaW50KSB7XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLnJvdXRlRml4ZWQgPSBmYWxzZTtcclxuICAgICAgICAgICAgbWFwVmlld01vZGVsLkhpZGVSb3V0ZSgpO1xyXG4gICAgICAgIH1cclxuICAgICAgICBpZiAoaCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICBoID0gKHRoaXMgYXMgYW55KTtcclxuICAgICAgICBpZiAoIShoIGluc3RhbmNlb2YgQ2xpZW50TW9kZWwuSGFyYm91cikpXHJcbiAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICBjb25zdCBsYXRMbmdzID0gW2guTGF0TG5nXTtcclxuICAgICAgICBsZXQgZGlzdCA9IGguRGlzdGFuY2UoKTtcclxuICAgICAgICBpZiAoZGlzdCA9PT0gdW5kZWZpbmVkIHx8IGRpc3QgPT09IG51bGwpXHJcbiAgICAgICAgICAgIGRpc3QgPSAwO1xyXG4gICAgICAgIHdoaWxlIChoLlByZWNlc3NvcigpICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgaCA9IGguUHJlY2Vzc29yKCk7XHJcbiAgICAgICAgICAgIGxhdExuZ3MucHVzaChoLkxhdExuZyk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5oaWdobGlnaHRlZFJvdXRlID0gTC5wb2x5bGluZShsYXRMbmdzKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZS5hZGRUbyhtYXBWaWV3TW9kZWwuTWFwKTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZS5iaW5kTGFiZWwoZGlzdC50b1N0cmluZygpICsgXCIgc21cIiwgeyBub0hpZGU6IHRydWUgfSk7XHJcbiAgICAgICAgbWFwVmlld01vZGVsLkZpdEJvdW5kcyhtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZS5nZXRCb3VuZHMoKSk7XHJcbiAgICB9XHJcblxyXG4gICAgRml0Qm91bmRzKGJvdW5kczogTC5MYXRMbmdCb3VuZHMpIHtcclxuICAgICAgICBjb25zdCBtYXAgPSBtYXBWaWV3TW9kZWwuTWFwO1xyXG4gICAgICAgIGNvbnN0IGN1cnJlbnRCb3VuZHMgPSBtYXAuZ2V0Qm91bmRzKCk7XHJcbiAgICAgICAgaWYgKCFjdXJyZW50Qm91bmRzLmNvbnRhaW5zKGJvdW5kcykpIHtcclxuICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzID0gY3VycmVudEJvdW5kcztcclxuICAgICAgICAgICAgbWFwLmZpdEJvdW5kcyhib3VuZHMpO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBIaWRlUm91dGUoZm9yY2UgPSBmYWxzZSkge1xyXG4gICAgICAgIGlmICgoIW1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkIHx8IGZvcmNlKSAmJiBtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZSAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkID0gZmFsc2U7XHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5NYXAucmVtb3ZlTGF5ZXIobWFwVmlld01vZGVsLmhpZ2hsaWdodGVkUm91dGUpO1xyXG4gICAgICAgICAgICBtYXBWaWV3TW9kZWwuaGlnaGxpZ2h0ZWRSb3V0ZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgaWYgKCFtYXBWaWV3TW9kZWwubm9SZXZlcnRUb1ByZXZpb3VzQm91bmRzICYmIG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCB0bXBCb3VuZHMgPSBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHM7XHJcbiAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICB3aW5kb3cuc2V0VGltZW91dCgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5wcmV2aW91c0JvdW5kcyA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuTWFwLmZpdEJvdW5kcyh0bXBCb3VuZHMpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLnByZXZpb3VzQm91bmRzID0gdG1wQm91bmRzO1xyXG4gICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgICAgICAxMDApO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIEZpeFJvdXRlKCkge1xyXG4gICAgICAgIG1hcFZpZXdNb2RlbC5yb3V0ZUZpeGVkID0gdHJ1ZTtcclxuICAgICAgICBtYXBWaWV3TW9kZWwucHJldmlvdXNCb3VuZHMgPSB1bmRlZmluZWQ7XHJcbiAgICB9XHJcblxyXG4gICAgQ3JlYXRlV2F5cG9pbnQobWFya2VyVHlwZTogTWFya2VyVHlwZSk6IFdheXBvaW50O1xyXG4gICAgQ3JlYXRlV2F5cG9pbnQobGF0TG5nOiBMLkxhdExuZywgbWFya2VyVHlwZTogTWFya2VyVHlwZSk6IFdheXBvaW50O1xyXG4gICAgQ3JlYXRlV2F5cG9pbnQobGF0TG5nPzogTC5MYXRMbmcgfCBNYXJrZXJUeXBlLCBtYXJrZXJUeXBlPzogTWFya2VyVHlwZSk6IFdheXBvaW50IHtcclxuICAgICAgICBsZXQgd3A6IFdheXBvaW50O1xyXG4gICAgICAgIGlmIChtYXJrZXJUeXBlICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgIHdwID0gbmV3IFdheXBvaW50KGxhdExuZyBhcyBMLkxhdExuZywgbWFya2VyVHlwZSwgbWFwVmlld01vZGVsLk1hcCBhcyBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIGVsc2VcclxuICAgICAgICAgICAgd3AgPSBuZXcgV2F5cG9pbnQobWFya2VyVHlwZSwgbWFwVmlld01vZGVsLk1hcCBhcyBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIHRoaXMuSW5pdGlhbGl6ZVdheXBvaW50KHdwLCBtYXJrZXJUeXBlKTtcclxuICAgICAgICByZXR1cm4gd3A7XHJcbiAgICB9XHJcblxyXG4gICAgSW5pdGlhbGl6ZVdheXBvaW50KHdwOiBXYXlwb2ludCwgbWFya2VyVHlwZTogTWFya2VyVHlwZSkge1xyXG4gICAgICAgIHRoaXMuQ3JlYXRlTWFya2VyKG1hcmtlclR5cGUsIHdwKTtcclxuICAgIH1cclxuXHJcbiAgICBDcmVhdGVNYXJrZXIobWFya2VyVHlwZTogTWFya2VyVHlwZSwgd3A6IENsaWVudE1vZGVsLldheXBvaW50KSB7XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4gfHwgbWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5IYXJib3VyKSB7XHJcbiAgICAgICAgICAgIGNvbnN0IG9wdGlvbnM6IEwuTWFya2VyT3B0aW9ucyA9IHtcclxuICAgICAgICAgICAgICAgIGRyYWdnYWJsZTogKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pXHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgICAgIGlmIChtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15KSB7XHJcbiAgICAgICAgICAgICAgICBvcHRpb25zLm9wYWNpdHkgPSAwO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluICYmXHJcbiAgICAgICAgICAgICAgICAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5XYXlwb2ludCB8fCBtYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15KSkge1xyXG4gICAgICAgICAgICAgICAgb3B0aW9ucy5pY29uID0gbmV3IEwuSWNvbih7XHJcbiAgICAgICAgICAgICAgICAgICAgaWNvblVybDogXCIvaW1hZ2VzL3dheXBvaW50aGFuZGxlLnBuZ1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIGljb25TaXplOiBuZXcgTC5Qb2ludCgxMCwgMTAsIHRydWUpLFxyXG4gICAgICAgICAgICAgICAgICAgIGNsYXNzTmFtZTogXCJ3YXlwb2ludFwiXHJcbiAgICAgICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5NYXBNb2RlKCkgPT09IE1hcE1vZGUuQWRtaW4pIHtcclxuICAgICAgICAgICAgICAgIG9wdGlvbnMuY29udGV4dG1lbnUgPSB0cnVlO1xyXG4gICAgICAgICAgICAgICAgb3B0aW9ucy5jb250ZXh0bWVudUluaGVyaXRJdGVtcyA9IGZhbHNlO1xyXG4gICAgICAgICAgICAgICAgLy8gUmVTaGFycGVyIGRpc2FibGUgU3VzcGljaW91c1RoaXNVc2FnZVxyXG4gICAgICAgICAgICAgICAgaWYgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuSGFyYm91cikge1xyXG4gICAgICAgICAgICAgICAgICAgIG9wdGlvbnMuY29udGV4dG1lbnVJdGVtcyA9IFtcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdGV4dDogXCJCZWFyYmVpdGVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0OiB3cCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrOiBmdW5jdGlvbiAoKSB7IG1hcFZpZXdNb2RlbC5IYXJib3VySGVscGVyLkVkaXRpbmcodGhpcykgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9LFxyXG4gICAgICAgICAgICAgICAgICAgICAgICB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB0ZXh0OiBcIkzDtnNjaGVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0OiB3cCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrOiBmdW5jdGlvbiAoKSB7IG1hcFZpZXdNb2RlbC5IYXJib3VySGVscGVyLkRlbGV0aW5nKHRoaXMpIH1cclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgIF07XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIG9wdGlvbnMuY29udGV4dG1lbnVJdGVtcyA9IFtcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdGV4dDogXCJCZWFyYmVpdGVuXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0OiB3cCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNhbGxiYWNrOiBmdW5jdGlvbiAoKSB7IG1hcFZpZXdNb2RlbC5XYXlwb2ludEhlbHBlci5FZGl0aW5nKHRoaXMpIH1cclxuICAgICAgICAgICAgICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdGV4dDogXCJMw7ZzY2hlblwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY29udGV4dDogd3AsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBjYWxsYmFjazogZnVuY3Rpb24gKCkgeyBtYXBWaWV3TW9kZWwuV2F5cG9pbnRIZWxwZXIuRGVsZXRpbmcodGhpcykgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgXTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIC8vIFJlU2hhcnBlciByZXN0b3JlIFN1c3BpY2lvdXNUaGlzVXNhZ2VcclxuICAgICAgICAgICAgfVxyXG5cclxuICAgICAgICAgICAgY29uc3QgbWFya2VyID0gbmV3IEwuTWFya2VyKHdwLkxhdExuZywgb3B0aW9ucyk7XHJcbiAgICAgICAgICAgIG1hcmtlci5hZGRUbyh0aGlzLk1hcCk7XHJcbiAgICAgICAgICAgIG1hcmtlci5XYXlwb2ludCA9IHdwO1xyXG4gICAgICAgICAgICB3cC5tYXJrZXIgPSBtYXJrZXI7XHJcbiAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuTWFwTW9kZSgpID09PSBNYXBNb2RlLkFkbWluKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSlcclxuICAgICAgICAgICAgICAgICAgICBtYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcIm1vdXNlb3V0XCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIChlKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBpZiAoZS50YXJnZXQuV2F5cG9pbnQuSXNEdW1teSgpKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZSA9IHVuZGVmaW5lZDtcclxuXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgbWFya2VyLmFkZEV2ZW50TGlzdGVuZXIoXCJkcmFnXCIsICgpID0+IHsgd3AuU2V0TGF0TG5nKHdwLm1hcmtlci5nZXRMYXRMbmcoKSk7IH0pO1xyXG4gICAgICAgICAgICAgICAgaWYgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuV2F5cG9pbnQgfHwgbWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5EdW1teSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuV2F5cG9pbnRNYXJrZXJzLnB1c2god3AubWFya2VyKTtcclxuICAgICAgICAgICAgICAgICAgICB3cC5tYXJrZXIuUG9pbnQgPSBtYXBWaWV3TW9kZWwuTWFwLmxhdExuZ1RvQ29udGFpbmVyUG9pbnQod3AuTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIHdwLm1hcmtlci5hZGRFdmVudExpc3RlbmVyKFwiY2xpY2tcIixcclxuICAgICAgICAgICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmICh3cC5Jc0R1bW15KCkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5Ib3ZlcmVkUG9seWluZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHdwLmNvbnZlcnRGcm9tRHVtbXlIYW5kbGUoKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMucHVzaCh3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5HZXRNYXBNb2RlKCkgPT09IE1hcE1vZGUuUm91dGVEcmF3aW5nKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBpZiAoIXdwLklzSW5Qb2x5bGluZShtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lKSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIFNlcnZlckFwaS5XYXlwb2ludENvbm5lY3Rpb25zXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIC5Db25uZWN0KHdwLklkKCksIG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUuV2F5cG9pbnRzWzBdLklkKCkpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIHdwLkFkZFRvUG9seWxpbmUobWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgcmVtb3ZlRnJvbVBvbHlsaW5lKG1hcFZpZXdNb2RlbC5EcmF3aW5nUG9seWxpbmUsIG1hcFZpZXdNb2RlbC5EcmF3aW5nTGF0TG5nKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBhZGREdW1teUhhbmRsZShtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nTGF0TG5nID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICByZW1vdmVQb2x5bGluZShtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuRHJhd2luZ1BvbHlsaW5lID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nTGF0TG5nID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcImRibGNsaWNrXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgKGU6IEwuTGVhZmxldE1vdXNlRXZlbnQpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZSA9IG1hcFZpZXdNb2RlbC5BZGRQb2x5bGluZSh3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5EcmF3aW5nTGF0TG5nID0gbmV3IEwuTGF0TG5nKGUubGF0bG5nLmxhdCwgZS5sYXRsbmcubG5nKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkRyYXdpbmdQb2x5bGluZS5hZGRMYXRMbmcobWFwVmlld01vZGVsLkRyYXdpbmdMYXRMbmcpO1xyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAgICAgaWYgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuRHVtbXkpXHJcbiAgICAgICAgICAgICAgICAgICAgd3AubWFya2VyLmFkZE9uZVRpbWVFdmVudExpc3RlbmVyKFwiZHJhZ1wiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB3cC5jb252ZXJ0RnJvbUR1bW15SGFuZGxlKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBtYXBWaWV3TW9kZWwuV2F5cG9pbnRzLnB1c2god3ApO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgIC8vZWxzZSBpZiAobWFya2VyVHlwZSA9PT0gTWFya2VyVHlwZS5XYXlwb2ludCkge1xyXG4gICAgICAgICAgICAgICAgLy8gICAgd3AuTmFtZShgV2VncHVua3QgJHttYXBWaWV3TW9kZWwuV2F5cG9pbnRzKCkubGVuZ3RoICsgMX1gKTtcclxuICAgICAgICAgICAgICAgIC8vfVxyXG4gICAgICAgICAgICAgICAgd3AubWFya2VyLmFkZEV2ZW50TGlzdGVuZXIoXCJkcmFnZW5kXCIsICgpID0+IHsgd3AuU2F2ZVRvU2VydmVyKCk7IH0pO1xyXG4gICAgICAgICAgICB9IGVsc2UgaWYgKG1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuSGFyYm91cikge1xyXG4gICAgICAgICAgICAgICAgd3AubWFya2VyLmFkZEV2ZW50TGlzdGVuZXIoXCJtb3VzZW92ZXJcIixcclxuICAgICAgICAgICAgICAgICAgICAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuSGFyYm91ckhlbHBlci5EZXRhaWwoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLlNob3dSb3V0ZSh3cCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICB3cC5tYXJrZXIuYWRkRXZlbnRMaXN0ZW5lcihcImNsaWNrXCIsICgpID0+IG1hcFZpZXdNb2RlbC5IYXJib3VySGVscGVyLkRldGFpbCh3cCBhcyBDbGllbnRNb2RlbC5IYXJib3VyKSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgQ3JlYXRlSGFyYm91cigpO1xyXG4gICAgQ3JlYXRlSGFyYm91cihuYW1lOiBzdHJpbmcsIGxhdExuZzogTC5MYXRMbmcpO1xyXG4gICAgQ3JlYXRlSGFyYm91cihuYW1lPzogc3RyaW5nLCBsYXRMbmc/OiBMLkxhdExuZykge1xyXG4gICAgICAgIGxldCBoOiBIYXJib3VyO1xyXG4gICAgICAgIGlmIChsYXRMbmcgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgaCA9IG5ldyBIYXJib3VyKGxhdExuZywgdGhpcy5NYXAgYXMgTC5tYXBib3guTWFwKTtcclxuICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgIGggPSBuZXcgSGFyYm91cih0aGlzLk1hcCBhcyBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIGguTmFtZShuYW1lKTtcclxuICAgICAgICB0aGlzLkluaXRpYWxpemVXYXlwb2ludChoLCBNYXJrZXJUeXBlLkhhcmJvdXIpO1xyXG4gICAgICAgIHJldHVybiBoO1xyXG4gICAgfVxyXG5cclxuICAgIERyYXdpbmdQb2x5bGluZTogTC5Qb2x5bGluZTtcclxuICAgIFBvbHlsaW5lcyA9IG5ldyBBcnJheTxMLlBvbHlsaW5lPigpO1xyXG4gICAgV2F5cG9pbnRNYXJrZXJzID0gbmV3IEFycmF5KCk7XHJcbiAgICBIb3ZlcmVkUG9seWluZTogTC5Qb2x5bGluZTtcclxuXHJcbiAgICBTZXRPcHRpb25LZXkob3B0aW9uLCBpdGVtOiBFbnRpdHkpIHtcclxuICAgICAgICBrby5hcHBseUJpbmRpbmdzVG9Ob2RlKG9wdGlvbiwgeyBhdHRyOiB7IFwiZGF0YS1pZFwiOiBpdGVtLklkIH0gfSwgaXRlbSk7XHJcbiAgICAgICAga28uYXBwbHlCaW5kaW5nc1RvTm9kZShvcHRpb24sIHsgYXR0cjogeyBcInZhbHVlXCI6IGl0ZW0uSWQgfSB9LCBpdGVtKTtcclxuICAgIH07XHJcblxyXG4gICAgSGFyYm91cnNUb1NlbGVjdCA9IGtvLmNvbXB1dGVkKCgpID0+IHtcclxuICAgICAgICByZXR1cm4gKHRoaXMuSGFyYm91cnNCeU5hbWUoKSBhcyBhbnlbXSkuY29uY2F0KFt7IE5hbWU6IFwiTmV1ZXIgSGFmZW4uLi5cIiwgSXNEdW1teTogdHJ1ZSB9XSk7XHJcbiAgICB9KTtcclxuXHJcbiAgICBQcm9jZXNzSGFyYm91clNlbGVjdE9wdGlvbnMgPSAob3B0aW9uOiBIVE1MT3B0aW9uRWxlbWVudCwgaXRlbSkgPT4ge1xyXG4gICAgICAgIGlmIChpdGVtICE9PSB1bmRlZmluZWQgJiYgaXRlbSAhPT0gbnVsbCAmJiBpdGVtLklzRHVtbXkgPT09IHRydWUpIHtcclxuICAgICAgICAgICAgb3B0aW9uLnZhbHVlID0gXCJmaWxsZWRcIjtcclxuICAgICAgICAgICAgY29uc3QgY29udGV4dCA9IGtvLmNvbnRleHRGb3Iob3B0aW9uKTtcclxuICAgICAgICAgICAgY29uc3Qgc2VsZWN0ID0gJChvcHRpb24pLnBhcmVudCgpO1xyXG4gICAgICAgICAgICBpZiAoc2VsZWN0LmRhdGEoXCJuZXctY2hhbmdlLWhhbmRsZXJcIikgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHNlbGVjdC5kYXRhKFwibmV3LWNoYW5nZS1oYW5kbGVyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgc2VsZWN0LmNoYW5nZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmICgkKG9wdGlvbikuaXMoXCI6c2VsZWN0ZWRcIikpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnN0IGhhcmJvdXIgPSB0aGlzLkNyZWF0ZUhhcmJvdXIoKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuSGFyYm91ckhlbHBlci5FZGl0aW5nKGhhcmJvdXIpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY29uc3Qgc3Vic2NyaXB0aW9uID0gdGhpcy5IYXJib3VySGVscGVyLkVkaXRpbmcuc3Vic2NyaWJlKCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBpZiAoaGFyYm91ci5JZCgpICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5IYXJib3Vycy5wdXNoKGhhcmJvdXIpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0LiRkYXRhLkhhcmJvdXIoaGFyYm91cik7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgaGFyYm91ci5SZW1vdmVGcm9tTWFwKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnRleHQuJGRhdGEuSGFyYm91cih1bmRlZmluZWQpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBzdWJzY3JpcHRpb24uZGlzcG9zZSgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICB9KSk7XHJcbiAgICAgICAgfVxyXG4gICAgfTtcclxuICAgIFBlcnNvbnNUb1NlbGVjdCA9IGtvLmNvbXB1dGVkKCgpID0+IHtcclxuICAgICAgICByZXR1cm4gKHRoaXMuUGVyc29ucygpLnNvcnQoKHAxLCBwMikgPT4gcDEuRnVsbE5hbWUoKSA+IHAyLkZ1bGxOYW1lKCkgPyAxIDogLTEpIGFzIGFueVtdKVxyXG4gICAgICAgICAgICAuY29uY2F0KFt7IEZ1bGxOYW1lOiBcIk5ldWUgUGVyc29uLi4uXCIsIElzRHVtbXk6IHRydWUgfV0pO1xyXG4gICAgfSk7XHJcblxyXG4gICAgUHJvY2Vzc1BlcnNvblNlbGVjdE9wdGlvbnMgPSAob3B0aW9uOiBIVE1MT3B0aW9uRWxlbWVudCwgaXRlbSkgPT4ge1xyXG4gICAgICAgIGlmIChpdGVtICE9PSB1bmRlZmluZWQgJiYgaXRlbSAhPT0gbnVsbCAmJiBpdGVtLklzRHVtbXkgPT09IHRydWUpIHtcclxuICAgICAgICAgICAgb3B0aW9uLnZhbHVlID0gXCJmaWxsZWRcIjtcclxuICAgICAgICAgICAgY29uc3QgY29udGV4dCA9IGtvLmNvbnRleHRGb3Iob3B0aW9uKTtcclxuICAgICAgICAgICAgY29uc3Qgc2VsZWN0ID0gJChvcHRpb24pLnBhcmVudCgpO1xyXG4gICAgICAgICAgICBpZiAoc2VsZWN0LmRhdGEoXCJuZXctY2hhbmdlLWhhbmRsZXJcIikgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHNlbGVjdC5kYXRhKFwibmV3LWNoYW5nZS1oYW5kbGVyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgc2VsZWN0LmNoYW5nZSgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmICgkKG9wdGlvbikuaXMoXCI6c2VsZWN0ZWRcIikpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnN0IHBlcnNvbiA9IG5ldyBQZXJzb24oKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuUGVyc29uSGVscGVyLkVkaXRpbmcocGVyc29uKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnN0IHN1YnNjcmlwdGlvbiA9IHRoaXMuUGVyc29uSGVscGVyLkVkaXRpbmcuc3Vic2NyaWJlKCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBpZiAocGVyc29uLklkKCkgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICB0aGlzLlBlcnNvbnMucHVzaChwZXJzb24pO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBjb250ZXh0LiRkYXRhLlBlcnNvbihwZXJzb24pO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNvbnRleHQuJGRhdGEuUGVyc29uKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIHN1YnNjcmlwdGlvbi5kaXNwb3NlKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgIH0pKTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG4gICAgQWxidW1TdGFjayA9IGtvLm9ic2VydmFibGVBcnJheTxDbGllbnRNb2RlbC5BbGJ1bT4oKTtcclxuXHJcbiAgICBHZXRQb3NpdGlvbkZvcldheXBvaW50ID0gKHdheXBvaW50OiBDbGllbnRNb2RlbC5XYXlwb2ludCkgPT4ge1xyXG4gICAgICAgIG5hdmlnYXRvci5nZW9sb2NhdGlvbi5nZXRDdXJyZW50UG9zaXRpb24oKGxvY2F0aW9uKSA9PiB7XHJcbiAgICAgICAgICAgIHdheXBvaW50LkxhdGl0dWRlKGxvY2F0aW9uLmNvb3Jkcy5sYXRpdHVkZSk7XHJcbiAgICAgICAgICAgIHdheXBvaW50LkxvbmdpdHVkZShsb2NhdGlvbi5jb29yZHMubG9uZ2l0dWRlKTtcclxuICAgICAgICB9LCBmdW5jdGlvbiAoKSB7IGNvbnNvbGUubG9nKGFyZ3VtZW50cyk7IGFsZXJ0KFwiRGllIFBvc2l0aW9uIGtvbm50ZSBuaWNodCBhYmdlcnVmZW4gd2VyZGVuXCIpIH0pO1xyXG4gICAgfVxyXG5cclxuICAgIExvZ0Jvb2tQYWdlciA9IG5ldyBQYWdlcih0aGlzLkxvZ0Jvb2tFbnRyaWVzLFxyXG4gICAgICAgIHtcclxuICAgICAgICAgICAgQ29sdW1uczogW1xyXG4gICAgICAgICAgICAgICAgbmV3IFBhZ2VyQ29sdW1uPENsaWVudE1vZGVsLkxvZ0Jvb2tFbnRyeSwgc3RyaW5nPihcIlN0YXJ0XCIsXHJcbiAgICAgICAgICAgICAgICAgICAgKGgpID0+IGguU3RhcnQoKS5OYW1lLFxyXG4gICAgICAgICAgICAgICAgICAgIHsgU29ydGVyOiBQYWdlckNvbHVtbi5TdHJpbmdTb3J0ZXIoKSwgVmlzaWJsZTogZmFsc2UgfSksXHJcbiAgICAgICAgICAgICAgICBuZXcgUGFnZXJDb2x1bW48Q2xpZW50TW9kZWwuTG9nQm9va0VudHJ5LCBzdHJpbmc+KFwiWmllbFwiLFxyXG4gICAgICAgICAgICAgICAgICAgIChoKSA9PiBoLkVuZCgpLk5hbWUsXHJcbiAgICAgICAgICAgICAgICAgICAgeyBTb3J0ZXI6IFBhZ2VyQ29sdW1uLlN0cmluZ1NvcnRlcigpLCBXaWR0aDogMjAwIH0pLFxyXG4gICAgICAgICAgICAgICAgbmV3IFBhZ2VyQ29sdW1uPENsaWVudE1vZGVsLkxvZ0Jvb2tFbnRyeSwgc3RyaW5nPihcIkRhdHVtXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgKGgpID0+IGguU3RhcnREYXRlLFxyXG4gICAgICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgU29ydGVyOiBQYWdlckNvbHVtbi5EYXRlU29ydGVyKCksXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFJlbmRlcmVyOiBQYWdlckNvbHVtbi5EYXRlUmVuZGVyZXIoKSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgV2lkdGg6IDE1MCxcclxuICAgICAgICAgICAgICAgICAgICAgICAgU29ydE1vZGU6IFNvcnRNb2Rlcy5EZXNjZW5kaW5nXHJcbiAgICAgICAgICAgICAgICAgICAgfSksXHJcbiAgICAgICAgICAgICAgICBuZXcgUGFnZXJDb2x1bW48Q2xpZW50TW9kZWwuXHJcbiAgICAgICAgICAgICAgICAgICAgTG9nQm9va0VudHJ5LFxyXG4gICAgICAgICAgICAgICAgICAgIHN0cmluZz4oXCJEYXVlclwiLCAoaCkgPT4gaC5TYWlsbGluZ1RpbWUsIHsgU29ydGVyOiBQYWdlckNvbHVtbi5TdHJpbmdTb3J0ZXIoKSB9KSxcclxuICAgICAgICAgICAgICAgIG5ldyBQYWdlckNvbHVtbjxDbGllbnRNb2RlbC5Mb2dCb29rRW50cnksXHJcbiAgICAgICAgICAgICAgICAgICAgUGVyc29uW1xyXG4gICAgICAgICAgICAgICAgICAgIF0+KFwiQ3Jld1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIChoKSA9PiBoLlBlcnNvbnMsXHJcbiAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBSZW5kZXJlcjogUGFnZXJDb2x1bW4uQXJyYXlSZW5kZXJlcjxQZXJzb24+KFwiPGJyIC8+XCIsIChwKSA9PiBwLkZ1bGxOYW1lKCkpLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICBXaWR0aDogMTUwXHJcbiAgICAgICAgICAgICAgICAgICAgfSksXHJcbiAgICAgICAgICAgICAgICBuZXcgUGFnZXJDb2x1bW48Q2xpZW50TW9kZWwuTG9nQm9va0VudHJ5LCBzdHJpbmc+KFwiQmVzb25kZXJlIFZvcmtvbW5pc3NlXCIsIChoKSA9PiBoLlNwZWNpYWxPY2N1cmVuY2VzKVxyXG4gICAgICAgICAgICBdIGFzIGFueSxcclxuICAgICAgICAgICAgVXNlUmVzcG9uc2l2ZVRhYmxlOiB0cnVlLFxyXG4gICAgICAgICAgICBVc2VTdHJpcGVkVGFibGU6IHRydWUsXHJcbiAgICAgICAgICAgIEVkaXRpbmdIZWxwZXI6IHRoaXMuTG9nQm9va0VudHJ5SGVscGVyLFxyXG4gICAgICAgICAgICBTaG93RWRpdERlbGV0ZUNvbnRyb2xzOiB0cnVlLFxyXG4gICAgICAgICAgICBJZFByZWZpeDogXCJsb2dCb29rT3ZlcnZpZXdfXCIsXHJcbiAgICAgICAgICAgIFNwZWNpYWxBY3Rpb25zOiBbbmV3IFBhZ2VyU3BlY2lhbEFjdGlvbihcIk5ldWVyIEVpbnRyYWdcIiwgKCkgPT4gJChcIiNlZGl0aW5nTG9nQm9va0VudHJ5TW9kYWxcIikubW9kYWwoXCJzaG93XCIpLCB1bmRlZmluZWQsIHRoaXMuSXNMb2dnZWRJbildXHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgSGFyYm91ckRpc3RhbmNlUGFnZXIgPSBuZXcgUGFnZXIoa28uY29tcHV0ZWQoKCk9PnRoaXMuSGFyYm91cnMoKS5zbGljZSgpLmZpbHRlcigoaCk9PmguRGlzdGFuY2UoKT4wKSksXHJcbiAgICAgICAge1xyXG4gICAgICAgICAgICBDb2x1bW5zOiBbXHJcbiAgICAgICAgICAgICAgICBuZXcgUGFnZXJDb2x1bW48Q2xpZW50TW9kZWwuSGFyYm91ciwgc3RyaW5nPihcIk5hbWVcIixcclxuICAgICAgICAgICAgICAgICAgICAoaCkgPT4gaC5OYW1lLFxyXG4gICAgICAgICAgICAgICAgICAgIHsgU29ydGVyOiBQYWdlckNvbHVtbi5TdHJpbmdTb3J0ZXIoKSB9KSxcclxuICAgICAgICAgICAgICAgIG5ldyBQYWdlckNvbHVtbjxDbGllbnRNb2RlbC5IYXJib3VyLCBudW1iZXI+KFwiRW50ZmVybnVuZ1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIChoKSA9PiBoLkRpc3RhbmNlLFxyXG4gICAgICAgICAgICAgICAgICAgIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgU29ydGVyOiBQYWdlckNvbHVtbi5OdW1iZXJTb3J0ZXIoKSxcclxuICAgICAgICAgICAgICAgICAgICAgICAgUmVuZGVyZXI6IChkKSA9PiBkICsgXCIgc21cIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgU29ydE1vZGU6IFNvcnRNb2Rlcy5Bc2NlbmRpbmdcclxuICAgICAgICAgICAgICAgICAgICB9KVxyXG4gICAgICAgICAgICBdIGFzIGFueSxcclxuICAgICAgICAgICAgRWRpdGluZ0hlbHBlcjogdGhpcy5IYXJib3VySGVscGVyLFxyXG4gICAgICAgICAgICBVc2VSZXNwb25zaXZlVGFibGU6IHRydWUsXHJcbiAgICAgICAgICAgIFVzZVN0cmlwZWRUYWJsZTogdHJ1ZSxcclxuICAgICAgICAgICAgVXNlU21hbGxDb2x1bW5Db250cm9sczogdHJ1ZSxcclxuICAgICAgICAgICAgU2hvd0NvbHVtblNlbGVjdG9yOiBmYWxzZSxcclxuICAgICAgICAgICAgSWRQcmVmaXg6IFwiaGFyYm91ckRpc3RhbmNlX1wiLFxyXG4gICAgICAgICAgICBTcGVjaWFsQ29sdW1uQWN0aW9uczogW25ldyBQYWdlclNwZWNpYWxDb2x1bW5BY3Rpb248Q2xpZW50TW9kZWwuSGFyYm91cj4oXCJSb3V0ZSB6ZWlnZW5cIiwgKGgpID0+IHtcclxuICAgICAgICAgICAgICAgIHRoaXMuU2hvd1JvdXRlKGgpO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5GaXhSb3V0ZSgpO1xyXG4gICAgICAgICAgICB9KV1cclxuICAgICAgICB9KTtcclxufVxyXG5cclxudmFyIGRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcgPSBmYWxzZTtcclxudmFyIGRyb3B6b25lTW9kYWwgPSAkKFwiI2Ryb3B6b25lTW9kYWxcIik7XHJcbnZhciBqb2JPdmVydmlld01vZGFsID0gJChcIiNqb2JPdmVydmlld01vZGFsXCIpO1xyXG52YXIgcGVyc29uT3ZlcnZpZXdNb2RhbCA9ICQoXCIjcGVyc29uT3ZlcnZpZXdNb2RhbFwiKTtcclxudmFyIGRyb3B6b25lOiBEcm9wem9uZTtcclxudmFyIGhhc0RyYWcgPSBmYWxzZTtcclxudmFyIHVwbG9hZE1vZGFsVmlzaWJsZSA9IGZhbHNlO1xyXG52YXIgcHN3cCA9ICQoXCIucHN3cFwiKVswXTtcclxudmFyIHBlcnNvbkRlYWlscyA9ICQoXCIjcGVyc29uRGV0YWlsc1wiKTtcclxudmFyIGRlbGV0ZVBlcnNvbiA9ICQoXCIjZGVsZXRlUGVyc29uXCIpO1xyXG52YXIgbGVmdFNpZGViYXIgPSBuZXcgU2lkZWJhcigkKFwiI2xlZnRTaWRlYmFyXCIpKTtcclxudmFyIHJpZ2h0U2lkZWJhciA9IG5ldyBTaWRlYmFyKCQoXCIjcmlnaHRTaWRlYmFyXCIpKTtcclxudmFyIGJvdHRvbVNpZGViYXIgPSBuZXcgU2lkZWJhcigkKFwiI2JvdHRvbVNpZGViYXJcIikpO1xyXG52YXIgaGFyYm91ckluZm8gPSAkKFwiI2hhcmJvdXJJbmZvXCIpO1xyXG5cclxudmFyIG1hcFZpZXdNb2RlbCA9IG5ldyBNYXBWaWV3TW9kZWwoTWFwTW9kZS5WaWV3KTtcclxuXHJcbkRyb3B6b25lLm9wdGlvbnMuZHJvcHpvbmUgPVxyXG4gICAge1xyXG4gICAgICAgIGFjY2VwdGVkRmlsZXM6IFwiaW1hZ2UvanBlZyxpbWFnZS9wbmdcIixcclxuICAgICAgICBkaWN0SW52YWxpZEZpbGVUeXBlOiBcIkRpZXNlciBEYXRlaXR5cCB3aXJkIG5pY2h0IHVudGVyc3TDvHR6dFwiLFxyXG4gICAgICAgIGRpY3REZWZhdWx0TWVzc2FnZTogXCJEYXRlaWVuIGhpZXIgYWJsZWdlblwiLFxyXG4gICAgICAgIGluaXQoKSB7XHJcbiAgICAgICAgICAgIGRyb3B6b25lID0gdGhpcztcclxuICAgICAgICAgICAgZHJvcHpvbmUub24oXCJzdWNjZXNzXCIsXHJcbiAgICAgICAgICAgICAgICAoZSwgZGF0YTogU2VydmVyTW9kZWwuQWxidW1JbWFnZSkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIHZhciBpbWFnZSA9IG5ldyBDbGllbnRNb2RlbC5JbWFnZSgpLkxvYWRGcm9tU2VydmVyRW50aXR5KGRhdGEuSW1hZ2UpO1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5JbWFnZXMucHVzaChpbWFnZSk7XHJcbiAgICAgICAgICAgICAgICAgICAgbWFwVmlld01vZGVsLkdldEFsYnVtQnlJZChkYXRhLkFsYnVtSWQpLkltYWdlcy5wdXNoKGltYWdlKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICBkcm9wem9uZS5vbihcInF1ZXVlY29tcGxldGVcIixcclxuICAgICAgICAgICAgICAgICgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoZHJvcHpvbmVNb2RhbE9wZW5lZEJ5RHJhZylcclxuICAgICAgICAgICAgICAgICAgICAgICAgZHJvcHpvbmVNb2RhbC5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgZHJvcHpvbmUub24oXCJkcmFnb3ZlclwiLCAoKSA9PiB7IGhhc0RyYWcgPSB0cnVlOyB9KTtcclxuICAgICAgICB9XHJcbiAgICB9O1xyXG5kb2N1bWVudC5vbmRyYWdlbnRlciA9XHJcbiAgICAoZTogRHJhZ0V2ZW50KSA9PiB7XHJcbiAgICAgICAgaWYgKG1hcFZpZXdNb2RlbC5Jc0xvZ2dlZEluICYmXHJcbiAgICAgICAgICAgICF1cGxvYWRNb2RhbFZpc2libGUgJiZcclxuICAgICAgICAgICAgIWhhc0RyYWcgJiZcclxuICAgICAgICAgICAgIWRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcgJiZcclxuICAgICAgICAgICAgZHJvcHpvbmVNb2RhbC5pcyhcIjpub3QoLmluKVwiKSAmJlxyXG4gICAgICAgICAgICBlLmRhdGFUcmFuc2Zlci50eXBlc1swXSA9PT0gXCJGaWxlc1wiICYmXHJcbiAgICAgICAgICAgIG1hcFZpZXdNb2RlbC5BbGJ1bVN0YWNrKClbMF0gIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICBkcm9wem9uZU1vZGFsLm1vZGFsKFwic2hvd1wiKTtcclxuICAgICAgICAgICAgdXBsb2FkTW9kYWxWaXNpYmxlID0gdHJ1ZTtcclxuICAgICAgICAgICAgZHJvcHpvbmVNb2RhbE9wZW5lZEJ5RHJhZyA9IHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBoYXNEcmFnID0gdHJ1ZTtcclxuICAgICAgICBlLnByZXZlbnREZWZhdWx0KCk7XHJcbiAgICAgICAgZS5zdG9wUHJvcGFnYXRpb24oKTtcclxuICAgIH07XHJcbmRvY3VtZW50Lm9uZHJhZ292ZXIgPSAoKSA9PiB7IGhhc0RyYWcgPSB0cnVlOyB9O1xyXG5kb2N1bWVudC5vbmRyYWdsZWF2ZSA9XHJcbiAgICAoZTogRHJhZ0V2ZW50KSA9PiB7XHJcbiAgICAgICAgaWYgKHVwbG9hZE1vZGFsVmlzaWJsZSAmJiBoYXNEcmFnICYmIGRyb3B6b25lTW9kYWxPcGVuZWRCeURyYWcgJiYgZHJvcHpvbmUuZ2V0UXVldWVkRmlsZXMoKS5sZW5ndGggPT09IDAgfHxcclxuICAgICAgICAgICAgZHJvcHpvbmUuZ2V0VXBsb2FkaW5nRmlsZXMoKS5sZW5ndGggPT09IDApIHtcclxuICAgICAgICAgICAgaGFzRHJhZyA9IGZhbHNlO1xyXG4gICAgICAgICAgICB3aW5kb3cuc2V0VGltZW91dCgoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAoIWhhc0RyYWcpIHtcclxuICAgICAgICAgICAgICAgICAgICBkcm9wem9uZU1vZGFsLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgICAgICAgICB1cGxvYWRNb2RhbFZpc2libGUgPSBmYWxzZTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgICAgIDEwMDApO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgZS5wcmV2ZW50RGVmYXVsdCgpO1xyXG4gICAgICAgIGUuc3RvcFByb3BhZ2F0aW9uKCk7XHJcbiAgICB9O1xyXG5kcm9wem9uZU1vZGFsLm9uKFwiaGlkZS5icy5tb2RhbFwiLFxyXG4gICAgZSA9PiB7XHJcbiAgICAgICAgaWYgKGRyb3B6b25lLmdldFF1ZXVlZEZpbGVzKCkubGVuZ3RoID4gMCB8fCBkcm9wem9uZS5nZXRVcGxvYWRpbmdGaWxlcygpLmxlbmd0aCA+IDApIHtcclxuICAgICAgICAgICAgZS5wcmV2ZW50RGVmYXVsdCgpO1xyXG4gICAgICAgICAgICBlLnN0b3BJbW1lZGlhdGVQcm9wYWdhdGlvbigpO1xyXG4gICAgICAgICAgICBhbGVydChcIkRhcyBGZW5zdGVyIGthbm4gbmljaHQgZ2VzY2hsb3NzZW4gd2VyZGVuLCB3w6RocmVuZCBEYXRlaWVuIGhvY2hnZWxhZGVuIHdlcmRlbi5cIik7XHJcbiAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICBkcm9wem9uZS5yZW1vdmVBbGxGaWxlcygpO1xyXG4gICAgICAgICAgICBkcm9wem9uZU1vZGFsT3BlbmVkQnlEcmFnID0gZmFsc2U7XHJcbiAgICAgICAgfVxyXG4gICAgfSk7XHJcbnZhciBnYWxsZXJ5OiBQaG90b1N3aXBlPFBob3RvU3dpcGUuT3B0aW9ucz47XHJcblxyXG4kKFwiLm1vZGFsXCIpXHJcbiAgICAub24oXCJoaWRkZW4uYnMubW9kYWxcIixcclxuICAgIGZ1bmN0aW9uICgpIHtcclxuICAgICAgICAkKHRoaXMpLnJlbW92ZUNsYXNzKFwiZnYtbW9kYWwtc3RhY2tcIik7XHJcbiAgICAgICAgJChcImJvZHlcIikuZGF0YShcImZ2X29wZW5fbW9kYWxzXCIsICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiKSAtIDEpO1xyXG4gICAgfSk7XHJcblxyXG5cclxuJChcIi5tb2RhbFwiKVxyXG4gICAgLm9uKFwic2hvd24uYnMubW9kYWxcIixcclxuICAgIGZ1bmN0aW9uICgpIHtcclxuXHJcbiAgICAgICAgLy8ga2VlcCB0cmFjayBvZiB0aGUgbnVtYmVyIG9mIG9wZW4gbW9kYWxzXHJcblxyXG4gICAgICAgIGlmICh0eXBlb2YgKCQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiKSkgPT0gXCJ1bmRlZmluZWRcIikge1xyXG4gICAgICAgICAgICAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIiwgMCk7XHJcbiAgICAgICAgfVxyXG5cclxuXHJcbiAgICAgICAgLy8gaWYgdGhlIHotaW5kZXggb2YgdGhpcyBtb2RhbCBoYXMgYmVlbiBzZXQsIGlnbm9yZS5cclxuXHJcbiAgICAgICAgaWYgKCQodGhpcykuaGFzQ2xhc3MoXCJmdi1tb2RhbC1zdGFja1wiKSkge1xyXG4gICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICAkKHRoaXMpLmFkZENsYXNzKFwiZnYtbW9kYWwtc3RhY2tcIik7XHJcblxyXG4gICAgICAgICQoXCJib2R5XCIpLmRhdGEoXCJmdl9vcGVuX21vZGFsc1wiLCAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikgKyAxKTtcclxuXHJcbiAgICAgICAgJCh0aGlzKS5jc3MoXCJ6LWluZGV4XCIsIDEwNDAgKyAoMTAgKiAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikpKTtcclxuXHJcbiAgICAgICAgJChcIi5tb2RhbC1iYWNrZHJvcFwiKVxyXG4gICAgICAgICAgICAubm90KFwiLmZ2LW1vZGFsLXN0YWNrXCIpXHJcbiAgICAgICAgICAgIC5jc3MoXCJ6LWluZGV4XCIsIDEwMzkgKyAoMTAgKiAkKFwiYm9keVwiKS5kYXRhKFwiZnZfb3Blbl9tb2RhbHNcIikpKTtcclxuXHJcblxyXG4gICAgICAgICQoXCIubW9kYWwtYmFja2Ryb3BcIilcclxuICAgICAgICAgICAgLm5vdChcImZ2LW1vZGFsLXN0YWNrXCIpXHJcbiAgICAgICAgICAgIC5hZGRDbGFzcyhcImZ2LW1vZGFsLXN0YWNrXCIpO1xyXG5cclxuICAgIH0pO1xyXG5cclxuaW50ZXJmYWNlIEtub2Nrb3V0QmluZGluZ0hhbmRsZXJzIHtcclxuICAgIGRhdGVyYW5nZT86IEtub2Nrb3V0QmluZGluZ0hhbmRsZXI7XHJcbn1cclxuXHJcbmtvLmJpbmRpbmdIYW5kbGVycy5kYXRlcmFuZ2UgPSB7XHJcbiAgICBpbml0KGVsZW1lbnQ6IGFueSwgdmFsdWVBY2Nlc3NvcjogKCkgPT4gYW55LCBhbGxCaW5kaW5nc0FjY2Vzc29yPzogS25vY2tvdXRBbGxCaW5kaW5nc0FjY2Vzc29yLCB2aWV3TW9kZWw/OiBhbnksXHJcbiAgICAgICAgYmluZGluZ0NvbnRleHQ/OiBLbm9ja291dEJpbmRpbmdDb250ZXh0KSB7XHJcbiAgICAgICAgbGV0IHZhbHVlID0gdmFsdWVBY2Nlc3NvcigpKCk7XHJcbiAgICAgICAgaWYgKHZhbHVlID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgIHZhbHVlQWNjZXNzb3IoKShuZXcgRGF0ZSgpLnRvSlNPTigpKTtcclxuICAgICAgICB2YWx1ZSA9IHZhbHVlQWNjZXNzb3IoKSgpO1xyXG4gICAgICAgICQoZWxlbWVudClcclxuICAgICAgICAgICAgLmRhdGVyYW5nZXBpY2tlcih7XHJcbiAgICAgICAgICAgICAgICBcInNpbmdsZURhdGVQaWNrZXJcIjogdHJ1ZSxcclxuICAgICAgICAgICAgICAgIFwic2hvd0Ryb3Bkb3duc1wiOiB0cnVlLFxyXG4gICAgICAgICAgICAgICAgXCJ0aW1lUGlja2VyXCI6IHRydWUsXHJcbiAgICAgICAgICAgICAgICBcInRpbWVQaWNrZXIyNEhvdXJcIjogdHJ1ZSxcclxuICAgICAgICAgICAgICAgIFwidGltZVBpY2tlckluY3JlbWVudFwiOiAxNSxcclxuICAgICAgICAgICAgICAgIFwibG9jYWxlXCI6IHtcclxuICAgICAgICAgICAgICAgICAgICBcImZvcm1hdFwiOiBcIkRELk1NLllZWVkgSEg6bW1cIixcclxuICAgICAgICAgICAgICAgICAgICBcInNlcGFyYXRvclwiOiBcIiAtIFwiLFxyXG4gICAgICAgICAgICAgICAgICAgIFwiYXBwbHlMYWJlbFwiOiBcIlNwZWljaGVyblwiLFxyXG4gICAgICAgICAgICAgICAgICAgIFwiY2FuY2VsTGFiZWxcIjogXCJBYmJyZWNoZW5cIixcclxuICAgICAgICAgICAgICAgICAgICBcImZyb21MYWJlbFwiOiBcIlZvblwiLFxyXG4gICAgICAgICAgICAgICAgICAgIFwidG9MYWJlbFwiOiBcIkJpc1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIFwiY3VzdG9tUmFuZ2VMYWJlbFwiOiBcIkN1c3RvbVwiLFxyXG4gICAgICAgICAgICAgICAgICAgIFwid2Vla0xhYmVsXCI6IFwiV1wiLFxyXG4gICAgICAgICAgICAgICAgICAgIFwiZGF5c09mV2Vla1wiOiBbXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiUzBcIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgXCJNb1wiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICBcIkRpXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiTWlcIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgXCJEb1wiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICBcIkZyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiU2FcIlxyXG4gICAgICAgICAgICAgICAgICAgIF0sXHJcbiAgICAgICAgICAgICAgICAgICAgXCJtb250aE5hbWVzXCI6IFtcclxuICAgICAgICAgICAgICAgICAgICAgICAgXCJKYW51YXJcIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgXCJGZWJydWFyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiTcOkcnpcIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgXCJBcHJpbFwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICBcIk1haVwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICBcIkp1bmlcIixcclxuICAgICAgICAgICAgICAgICAgICAgICAgXCJKdWxpXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiQXVndXN0XCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiU2VwdGVtYmVyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiT2t0b2JlclwiLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICBcIk5vdmVtYmVyXCIsXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIFwiRGV6ZW1iZXJcIlxyXG4gICAgICAgICAgICAgICAgICAgIF0sXHJcbiAgICAgICAgICAgICAgICAgICAgXCJmaXJzdERheVwiOiAxXHJcbiAgICAgICAgICAgICAgICB9LFxyXG4gICAgICAgICAgICAgICAgXCJhbHdheXNTaG93Q2FsZW5kYXJzXCI6IHRydWUsXHJcbiAgICAgICAgICAgICAgICBcInN0YXJ0RGF0ZVwiOiB2YWx1ZSxcclxuICAgICAgICAgICAgICAgIFwiZW5kRGF0ZVwiOiB2YWx1ZVxyXG4gICAgICAgICAgICB9LFxyXG4gICAgICAgICAgICAoc3RhcnQpID0+IHtcclxuICAgICAgICAgICAgICAgIHZhbHVlQWNjZXNzb3IoKShzdGFydC5fZC50b0pTT04oKSk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgfSxcclxuICAgIHVwZGF0ZShlbGVtZW50OiBhbnksIHZhbHVlQWNjZXNzb3I6ICgpID0+IGFueSwgYWxsQmluZGluZ3NBY2Nlc3Nvcj86IEtub2Nrb3V0QWxsQmluZGluZ3NBY2Nlc3Nvciwgdmlld01vZGVsPzogYW55LFxyXG4gICAgICAgIGJpbmRpbmdDb250ZXh0PzogS25vY2tvdXRCaW5kaW5nQ29udGV4dCkge1xyXG4gICAgICAgICQoZWxlbWVudCkuZGF0YShcImRhdGVyYW5nZXBpY2tlclwiKS5zZXRTdGFydERhdGUobW9tZW50KHZhbHVlQWNjZXNzb3IoKSgpKSk7XHJcbiAgICB9XHJcbn07XHJcblxyXG5cclxud2luZG93LlBhcnNsZXkub24oXCJmb3JtOnZhbGlkYXRlXCIsXHJcbiAgICBmb3JtID0+IHtcclxuICAgICAgICBpZiAoZm9ybS5zdWJtaXRFdmVudCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICB9KTtcclxuXHJcbndpbmRvdy5QYXJzbGV5Lm9uKFwiZm9ybTpzdWJtaXRcIiwgZm9ybSA9PiBmYWxzZSk7XHJcblxyXG4kKGRvY3VtZW50KVxyXG4gICAgLm9uKFwiZm9jdXNpblwiLFxyXG4gICAgZnVuY3Rpb24gKGUpIHtcclxuICAgICAgICBpZiAoJChlLnRhcmdldCkuY2xvc2VzdChcIi5tY2Utd2luZG93XCIpLmxlbmd0aCkge1xyXG4gICAgICAgICAgICBlLnN0b3BJbW1lZGlhdGVQcm9wYWdhdGlvbigpO1xyXG4gICAgICAgIH1cclxuICAgIH0pOyJdLCJzb3VyY2VSb290IjoiL3NvdXJjZS8ifQ==
