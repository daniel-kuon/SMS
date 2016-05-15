var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
var ClientModel;
(function (ClientModel) {
    var Entity = (function () {
        function Entity() {
            this.AlbumId = ko.observable();
            this.CommentListId = ko.observable();
            this.Album = CreateObservable({ AddTransferMode: TransferMode.Include, UpdateTransferMode: TransferMode.Include });
            this.InsertDate = ko.observable(Date);
            this.UpdateDate = ko.observable(Date);
            this.ClientId = ++Entity.clientIdCounter;
            this.ServerApi = ServerApi.GetApi(this);
            this.Id = ko.observable();
            Entity.entityDb[this.ClientId.toString()] = this;
        }
        Entity.prototype.DeleteOnServer = function () {
            return this.ServerApi.Delete(this.Id());
        };
        ;
        Entity.prototype.SaveToServer = function () {
            var _this = this;
            if (this.Id() === undefined)
                return this.ServerApi.Create(this.ConvertToServerEntity())
                    .done(function (data) {
                    _this.savedState = undefined;
                    _this.LoadFromServerEntity(data);
                });
            return this.ServerApi.Update(this.ConvertToServerEntity())
                .done(function (data) {
                _this.savedState = undefined;
                _this.LoadFromServerEntity(data);
            });
            ;
        };
        Entity.prototype.LoadFromServerEntity = function (serverEntity) {
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var prop = _a[_i];
                var sVal = serverEntity[prop];
                if (sVal !== undefined && sVal !== null) {
                    if (sVal instanceof Array) {
                        for (var _b = 0, sVal_1 = sVal; _b < sVal_1.length; _b++) {
                            var obj = sVal_1[_b];
                            var entity = Entity.entityDb[obj.ClientId.toString()];
                            if (entity !== undefined)
                                entity.LoadFromServerEntity(obj);
                        }
                    }
                    else {
                        var cVal = this[prop]();
                        if (cVal instanceof Entity)
                            cVal.LoadFromServerEntity(sVal);
                        else
                            this[prop](sVal);
                    }
                }
            }
            return this;
        };
        Entity.prototype.ConvertToServerEntity = function (idOnly) {
            if (idOnly === void 0) { idOnly = false; }
            var serverEntity = { ClientId: this.ClientId };
            var entity = this;
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var propName = _a[_i];
                var prop = entity[propName];
                var val = prop();
                if (val !== undefined && val !== Date) {
                    if (val instanceof Array) {
                        var arr = new Array();
                        for (var _b = 0, val_1 = val; _b < val_1.length; _b++) {
                            var elem = val_1[_b];
                            arr.push(elem.ConvertToServerEntity());
                        }
                        serverEntity[propName] = arr;
                    }
                    else
                        serverEntity[propName] = val instanceof Entity ? val.ConvertToServerEntity() : val;
                }
            }
            return serverEntity;
        };
        Entity.prototype.CopyTo = function (entity) {
            entity.Id(this.Id());
        };
        Entity.prototype.SaveState = function () {
            var entity = this;
            entity.savedState = new Object();
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var prop = _a[_i];
                var val = ko.unwrap(entity[prop]);
                if (val instanceof Array) {
                    val = val.slice(0);
                    for (var _b = 0, val_2 = val; _b < val_2.length; _b++) {
                        var elem = val_2[_b];
                        if (elem instanceof Entity)
                            elem.SaveState();
                    }
                }
                else if (val instanceof Entity)
                    val.SaveState();
                entity.savedState[prop] = val;
            }
        };
        Entity.prototype.GetObservableNames = function () {
            var out = new Array();
            var entity = this;
            for (var prop in entity)
                if (entity.hasOwnProperty(prop))
                    if (ko.isWriteableObservable(entity[prop]) && !entity[prop].Block)
                        out.push(prop);
            return out;
        };
        Entity.prototype.RevertState = function (ignoreError) {
            if (ignoreError === void 0) { ignoreError = false; }
            if (this.savedState === undefined)
                if (ignoreError)
                    return;
                else
                    throw "No saved state";
            var entity = this;
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var prop = _a[_i];
                var val = entity.savedState[prop];
                //this.savedState[prop] = val;
                entity[prop](val);
                if (val instanceof Entity)
                    val.RevertState();
                else if (val instanceof Array)
                    for (var _b = 0, val_3 = val; _b < val_3.length; _b++) {
                        var elem = val_3[_b];
                        if (elem instanceof Entity)
                            elem.RevertState();
                    }
            }
        };
        Entity.clientIdCounter = 0;
        Entity.entityDb = {};
        return Entity;
    }());
    ClientModel.Entity = Entity;
    var Album = (function (_super) {
        __extends(Album, _super);
        function Album() {
            _super.apply(this, arguments);
            this.Images = ko.observableArray();
        }
        return Album;
    }(Entity));
    ClientModel.Album = Album;
    var Person = (function (_super) {
        __extends(Person, _super);
        function Person() {
            var _this = this;
            _super.apply(this, arguments);
            this.LastName = ko.observable();
            this.FirstName = ko.observable();
            this.FullName = ko.computed(function () { return _this.FirstName() + " " + _this.LastName(); });
        }
        return Person;
    }(Entity));
    ClientModel.Person = Person;
    var Job = (function (_super) {
        __extends(Job, _super);
        function Job() {
            _super.apply(this, arguments);
            this.DueTo = ko.observable(Date);
            this.AssignedTo = ko.observable();
            this.AssignedToId = ko.observable();
            this.Title = ko.observable();
            this.Content = ko.observable();
            this.Done = ko.observable();
            this.SuperJob = ko.observable();
            this.SuperJobId = ko.observable();
            this.Trip = ko.observable();
            this.TripId = ko.observable();
            this.SubJobs = ko.observableArray();
        }
        return Job;
    }(Entity));
    ClientModel.Job = Job;
    var Waypoint = (function (_super) {
        __extends(Waypoint, _super);
        function Waypoint(latLng, markerType, Map) {
            var _this = this;
            _super.call(this);
            this.Map = Map;
            this.WaypointNumber = ko.observable();
            this.polylines = new Array();
            this.Latitude = ko.observable(0);
            this.Longitude = ko.observable(0);
            this.Distance = ko.observable();
            this.Precessor = ko.observable();
            this.RouteDistance = ko.observable();
            this.RoutePrecessor = ko.observable();
            this.Name = ko.observable();
            this.Description = ko.observable();
            if (Map === undefined) {
                if (typeof markerType == "number") {
                    Map = latLng;
                    latLng = markerType;
                    markerType = Map;
                }
                this.Map = (markerType);
                this.LatLng = new L.LatLng(0, 0);
                markerType = latLng;
            }
            else {
                this.Latitude(latLng.lat);
                this.Longitude(latLng.lng);
            }
            this.LatLng = new L.LatLng(this.Latitude(), this.Longitude());
            this.Latitude.subscribe(function (value) {
                if (_this.LatLng.lat !== value) {
                    _this.LatLng.lat = value;
                    _this.Redraw();
                }
            });
            this.Longitude.subscribe(function (value) {
                if (_this.LatLng.lng !== value) {
                    _this.LatLng.lng = value;
                    _this.Redraw();
                }
            });
            this.markerType = markerType;
            this.LatLng.Polylines = new Array();
            this.LatLng.Waypoint = this;
            //this.Latitude.subscribe((value) => {
            //    if (this.LatLng.lat !== value) {
            //        this.Redraw();
            //    }
            //});
            //this.Longitude.subscribe((value) => {
            //    if (this.LatLng.lng !== value) {
            //        this.Redraw();
            //    }
            //});
            this.Precessor.Block = true;
            this.Distance.Block = true;
        }
        Waypoint.prototype.Popup = function (content) {
            if (this.popup !== undefined) {
                this.Map.removeLayer(this.popup);
                this.popup = undefined;
            }
            if (content !== undefined) {
                //this.popup = L.popup();
                this.marker.bindPopup(content); //.openPopup();
            }
        };
        Waypoint.prototype.Show = function (highlight) {
            if (highlight === void 0) { highlight = false; }
            this.marker.setOpacity(this.marker.Waypoint.IsDummy() ? 0.5 : 1);
            if (highlight)
                $(this.marker._icon).addClass("expose");
        };
        Waypoint.prototype.Hide = function () {
            this.marker.setOpacity(0.1);
            $(this.marker._icon).removeClass("expose");
        };
        Waypoint.prototype.Redraw = function (updatePolylines) {
            if (updatePolylines === void 0) { updatePolylines = true; }
            if (this.marker !== undefined)
                this.marker.setLatLng(this.LatLng);
            if (updatePolylines)
                for (var i = 0; i < this.polylines.length; i++)
                    redrawPolyline(this.polylines[i]);
        };
        Waypoint.prototype.GetConnectedWaypoints = function () {
            var _this = this;
            var ret = new Array();
            for (var _i = 0, _a = this.polylines; _i < _a.length; _i++) {
                var line = _a[_i];
                for (var _b = 0, _c = line.Waypoints; _b < _c.length; _b++) {
                    var waypoint = _c[_b];
                    if (waypoint !== this)
                        ret.push(waypoint);
                }
            }
            return ret.sort(function (w1, w2) {
                return w1.LatLng.distanceTo(_this.LatLng) - w2.LatLng.distanceTo(_this.LatLng);
            });
        };
        Waypoint.prototype.convertFromDummyHandle = function () {
            this.marker.setOpacity(1);
            var w1 = this.polylines[0].Waypoints[0];
            var w2 = this.polylines[0].Waypoints[1];
            splitPolyline(this.polylines[0]);
            this.markerType = MarkerType.Waypoint;
            this.SaveToServer()
                .done(function (w) {
                var wCA = ServerApi.WaypointConnections;
                wCA.Disconnect(w1.Id(), w2.Id());
                wCA.Connect(w1.Id(), w.Id);
                wCA.Connect(w2.Id(), w.Id);
            });
        };
        Waypoint.prototype.IsInPolyline = function (polyline) {
            for (var _i = 0, _a = this.polylines; _i < _a.length; _i++) {
                var currentPolyline = _a[_i];
                if (polyline === currentPolyline)
                    return true;
            }
            return false;
        };
        Waypoint.prototype.RemoveFromMap = function () {
            if (this.markerType !== MarkerType.Dummy)
                for (var _i = 0, _a = this.polylines; _i < _a.length; _i++) {
                    var polyline = _a[_i];
                    removePolyline(polyline);
                }
            this.Map.removeLayer(this.marker);
        };
        Waypoint.prototype.AddToPolyline = function (polyline) {
            if (this.IsInPolyline(polyline))
                return false;
            if (polyline.DummyHandle !== this) {
                polyline.Waypoints.push(this);
                polyline.addLatLng(this.LatLng);
                polyline.redraw();
            }
            this.LatLng.Polylines.push(polyline);
            this.polylines.push(polyline);
            //ServerApi.WaypointConnectionApi.GetDefault().
            return true;
        };
        Waypoint.prototype.RemoveFromPolyline = function (polyline) {
            if (!this.IsInPolyline(polyline))
                return false;
            removeFromArray(polyline.Waypoints, this);
            removeFromArray(this.polylines, polyline);
            removeFromArray(this.LatLng.Polylines, polyline);
            removeFromArray(polyline.getLatLngs(), this.LatLng);
            polyline.redraw();
            return true;
        };
        Waypoint.prototype.RemoveIfHasZeroOrOnePolylines = function () {
            return true;
        };
        Waypoint.prototype.SetLatLng = function (latLng, updatePolylines) {
            if (updatePolylines === void 0) { updatePolylines = true; }
            this.LatLng.lat = latLng.lat;
            this.LatLng.lng = latLng.lng;
            this.Latitude(latLng.lat);
            this.Longitude(latLng.lng);
            this.Redraw(updatePolylines);
        };
        Waypoint.prototype.CenterOnMap = function () {
            this.Map.setView(this.LatLng);
        };
        Waypoint.prototype.IsDummy = function () {
            return this.markerType === MarkerType.Dummy;
        };
        return Waypoint;
    }(Entity));
    ClientModel.Waypoint = Waypoint;
    var Harbour = (function (_super) {
        __extends(Harbour, _super);
        function Harbour(latLng, map) {
            var _this = this;
            _super.call(this, latLng, MarkerType.Harbour, map);
            this.Album = ko.observable(new Album());
            this.Locations = ko.observableArray();
            this.Rating = ko.observable();
            this.Content = ko.observable();
            this.Website = ko.observable();
            //if (map)
            //this.Distance.subscribe((d) => {
            //    const label = this.marker.getLabel();
            //    if (d > 0) {
            //        if (label !== undefined) {
            //            this.marker.updateLabelContent(d.toString() + " km");
            //        } else {
            //            this.marker.bindLabel(d.toString() + " km",
            //                <any>{
            //                    direction: "auto"
            //                });
            //        }
            //    } else if (label !== undefined) {
            //        this.marker.unbindLabel();
            //    }
            //});
            this.Name.subscribe(function (d) {
                var label = _this.marker.getLabel();
                if (label !== undefined) {
                    _this.marker.updateLabelContent(d);
                }
                else {
                    _this.marker.bindLabel(d, {
                        direction: "auto"
                    });
                }
            });
        }
        Harbour.prototype.RemoveIfHasZeroOrOnePolylines = function () {
            return false;
        };
        return Harbour;
    }(Waypoint));
    ClientModel.Harbour = Harbour;
    var Address = (function (_super) {
        __extends(Address, _super);
        function Address() {
            _super.apply(this, arguments);
            this.Street = ko.observable();
            this.Zip = ko.observable();
            this.Town = ko.observable();
            this.Comment = ko.observable();
        }
        return Address;
    }(Entity));
    ClientModel.Address = Address;
    var Image = (function (_super) {
        __extends(Image, _super);
        function Image() {
            _super.apply(this, arguments);
            this.Path = ko.observable();
            this.Height = ko.observable();
            this.Width = ko.observable();
        }
        return Image;
    }(Entity));
    ClientModel.Image = Image;
    var TackBase = (function (_super) {
        __extends(TackBase, _super);
        function TackBase() {
            var _this = this;
            _super.apply(this, arguments);
            this.StartDate = ko.observable();
            this.EndDate = ko.observable();
            this.Start = ko.observable();
            this.StartId = ko.observable();
            this.EndId = ko.observable();
            this.End = ko.observable();
            this.Persons = ko.observableArray();
            this.Distance = ko.observable(0);
            this.CrewList = ko.computed({
                read: function () {
                    var persons = _this.Persons();
                    var first = persons[0];
                    if (first === undefined)
                        return "";
                    if (persons.length === 1)
                        return first.FullName();
                    else {
                        var list = first.FullName();
                        for (var i = 1; i < persons.length; i++) {
                            list += ", " + persons[i].FullName();
                        }
                        return list;
                    }
                },
                deferEvaluation: true
            });
            this.SaillingTime = ko.computed(function () {
                var startDate = _this.StartDate();
                var endDate = _this.EndDate();
                if (startDate === undefined || endDate === undefined || renderTime === undefined)
                    return "";
                return renderTime(new Date(startDate), new Date(endDate));
            });
        }
        return TackBase;
    }(Entity));
    ClientModel.TackBase = TackBase;
    var Trip = (function (_super) {
        __extends(Trip, _super);
        function Trip() {
            _super.apply(this, arguments);
            this.Name = ko.observable();
            this.Content = ko.observable();
            this.Tacks = ko.observableArray();
            this.IsDummy = ko.observable();
        }
        return Trip;
    }(TackBase));
    ClientModel.Trip = Trip;
    var LogBookEntry = (function (_super) {
        __extends(LogBookEntry, _super);
        function LogBookEntry() {
            _super.apply(this, arguments);
            this.MotorHoursStart = ko.observable();
            this.MotorHoursEnd = ko.observable();
            this.LogStart = ko.observable();
            this.LogEnd = ko.observable();
            this.SpecialOccurences = ko.observable();
        }
        return LogBookEntry;
    }(TackBase));
    ClientModel.LogBookEntry = LogBookEntry;
    var Tack = (function (_super) {
        __extends(Tack, _super);
        function Tack() {
            var _this = this;
            _super.apply(this, arguments);
            this.Waypoints = ko.observableArray();
            this.CanRemoveTack = ko.computed({
                read: function () {
                    if (mapViewModel.SelectedTrip() === undefined)
                        return false;
                    var tacks = mapViewModel.SelectedTrip().Tacks;
                    var index = tacks.indexOf(_this);
                    var prevTack = tacks()[index - 1];
                    var nextTack = tacks()[index + 1];
                    if (prevTack !== undefined)
                        if (nextTack !== undefined)
                            return prevTack.Start() !== nextTack.Start();
                        else
                            return true;
                    else
                        return nextTack !== undefined;
                },
                deferEvaluation: true
            });
            this.ComputePlaceholder = ko.computed(function () {
                if (_this.StartDate() !== undefined)
                    return moment(_this.StartDate()).format("L");
                return "";
            });
        }
        return Tack;
    }(TackBase));
    ClientModel.Tack = Tack;
    var Comment = (function (_super) {
        __extends(Comment, _super);
        function Comment() {
            _super.apply(this, arguments);
            this.Title = ko.observable();
            this.Content = ko.observable();
            this.Rating = ko.observable();
            this.ParentId = ko.observable();
        }
        return Comment;
    }(Entity));
    ClientModel.Comment = Comment;
    var Location = (function (_super) {
        __extends(Location, _super);
        function Location() {
            _super.apply(this, arguments);
            this.HarbourId = ko.observable();
            this.Website = ko.observable();
            this.Name = ko.observable();
            this.Rating = ko.observable();
            this.Address = ko.observable();
            this.AddressId = ko.observable();
        }
        return Location;
    }(Entity));
    ClientModel.Location = Location;
    var Restaurant = (function (_super) {
        __extends(Restaurant, _super);
        function Restaurant() {
            _super.apply(this, arguments);
        }
        return Restaurant;
    }(Location));
    ClientModel.Restaurant = Restaurant;
    var Supermarket = (function (_super) {
        __extends(Supermarket, _super);
        function Supermarket() {
            _super.apply(this, arguments);
        }
        return Supermarket;
    }(Location));
    ClientModel.Supermarket = Supermarket;
    var WaypointDistance = (function () {
        function WaypointDistance(Precessor, Waypoint, Distance, calculaterdWaypoints, calculateRoute) {
            this.Precessor = Precessor;
            this.Waypoint = Waypoint;
            this.Distance = Distance;
            this.ConnectedWayPoints = new Array();
            this.ConnectedDistances = new Array();
            this.LatLng = Waypoint.LatLng;
            calculaterdWaypoints.push(Waypoint);
            for (var _i = 0, _a = Waypoint.GetConnectedWaypoints(); _i < _a.length; _i++) {
                var wp = _a[_i];
                if (calculaterdWaypoints.indexOf(wp) === -1)
                    this.ConnectedWayPoints.push(wp);
            }
            if (calculateRoute) {
                Waypoint.RoutePrecessor(Precessor);
                Waypoint.RouteDistance(Distance);
            }
            else {
                Waypoint.Precessor(Precessor);
                Waypoint.Distance(Distance);
            }
        }
        return WaypointDistance;
    }());
    ClientModel.WaypointDistance = WaypointDistance;
})(ClientModel || (ClientModel = {}));
var MarkerType;
(function (MarkerType) {
    MarkerType[MarkerType["Harbour"] = 0] = "Harbour";
    MarkerType[MarkerType["Dummy"] = 1] = "Dummy";
    MarkerType[MarkerType["Waypoint"] = 2] = "Waypoint";
    MarkerType[MarkerType["WeatherStation"] = 3] = "WeatherStation";
})(MarkerType || (MarkerType = {}));
