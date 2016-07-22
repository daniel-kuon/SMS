var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
var ClientModel;
(function (ClientModel) {
    var Entity = (function () {
        function Entity() {
            var _this = this;
            this.AlbumId = ko.observable();
            this.CommentListId = ko.observable();
            this.Album = CreateObservable({
                ForeignKey: function (t) { return t.AlbumId; }
            });
            this.InsertDate = ko.observable(Date);
            this.UpdateDate = ko.observable(Date);
            this.ClientId = Entity.clientIdCounter++;
            this.ServerApi = ServerApi.GetApi(this);
            this.savedStates = new Array();
            this.SaveState = function (alreadySavedEntities) {
                if (alreadySavedEntities === void 0) { alreadySavedEntities = new Array(); }
                var savedState = new Object();
                if (alreadySavedEntities.indexOf(_this) !== -1)
                    return;
                alreadySavedEntities.push(_this);
                for (var _i = 0, _a = _this.GetObservableNames(); _i < _a.length; _i++) {
                    var prop = _a[_i];
                    var val = ko.unwrap(_this[prop]);
                    if (val instanceof Array) {
                        val = val.slice(0);
                        for (var _b = 0, val_1 = val; _b < val_1.length; _b++) {
                            var elem = val_1[_b];
                            if (elem instanceof Entity)
                                elem.SaveState(alreadySavedEntities);
                        }
                    }
                    else if (val instanceof Entity)
                        val.SaveState(alreadySavedEntities);
                    savedState[prop] = val;
                }
                _this.savedStates.push(savedState);
            };
            this.RevertState = function (ignoreError, alreadyRevertedEntities) {
                if (ignoreError === void 0) { ignoreError = false; }
                if (alreadyRevertedEntities === void 0) { alreadyRevertedEntities = new Array(); }
                if (alreadyRevertedEntities.indexOf(_this) !== -1)
                    return;
                alreadyRevertedEntities.push(_this);
                if (_this.savedStates.length === 0)
                    if (ignoreError)
                        return;
                    else
                        throw "No saved state";
                for (var _i = 0, _a = _this.GetObservableNames(); _i < _a.length; _i++) {
                    var prop = _a[_i];
                    var val = _this.savedStates[0][prop];
                    //this.savedState[prop] = val;
                    _this[prop](val);
                    if (val instanceof Entity)
                        val.RevertState(ignoreError, alreadyRevertedEntities);
                    else if (val instanceof Array)
                        for (var _b = 0, val_2 = val; _b < val_2.length; _b++) {
                            var elem = val_2[_b];
                            if (elem instanceof Entity)
                                elem.RevertState(ignoreError, alreadyRevertedEntities);
                        }
                }
                _this.savedStates.shift();
            };
            this.Id = ko.observable();
            Entity.entityDb[this.ClientId.toString()] = this;
        }
        Entity.prototype.DeleteOnServer = function () {
            var _this = this;
            return this.ServerApi.Delete(this.Id()).done(function () { _this.removeFromContext(); });
        };
        ;
        Entity.prototype.SaveToServer = function () {
            var _this = this;
            if (this.Id() === undefined)
                return this.ServerApi.Create(this.ConvertToServerEntity())
                    .done(function (data) {
                    _this.savedStates = [];
                    _this.LoadFromServerEntity(data);
                    _this.addToContext();
                });
            return this.ServerApi.Update(this.ConvertToServerEntity())
                .done(function (data) {
                _this.savedStates = [];
                _this.LoadFromServerEntity(data);
            });
            ;
        };
        Entity.prototype.LoadNavigationProperties = function () {
            for (var propName in this.GetObservableNames()) {
                var prop = this[propName];
            }
        };
        Entity.prototype.addToContext = function () {
            if (this.Context().indexOf(this) !== -1)
                return;
            this.Context().push(this);
        };
        Entity.prototype.removeFromContext = function () {
            this.Context().remove(this);
        };
        Entity.prototype.LoadFromServerEntity = function (serverEntity) {
            if (serverEntity.ProcessOnServer === false)
                return this;
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
            if (idOnly) {
                return { Id: this.Id(), ProcessOnServer: false };
            }
            var isUpdate = this.Id() !== undefined;
            var serverEntity = { ClientId: this.ClientId };
            var entity = this;
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var propName = _a[_i];
                var prop = entity[propName];
                var val = prop();
                if (val !== undefined) {
                    if (val instanceof Array) {
                        var arr = new Array();
                        for (var _b = 0, _c = val; _b < _c.length; _b++) {
                            var elem = _c[_b];
                            if (elem.Id() === undefined ||
                                isUpdate && prop.UpdateTransferMode === TransferMode.Include ||
                                !isUpdate && prop.AddTransferMode === TransferMode.Include)
                                arr.push(elem.ConvertToServerEntity());
                        }
                        serverEntity[propName] = arr;
                    }
                    else
                        serverEntity[propName] = val instanceof Entity
                            ? (val.Id() === undefined ||
                                isUpdate && prop.UpdateTransferMode === TransferMode.Include ||
                                !isUpdate && prop.AddTransferMode === TransferMode.Include
                                ? val.ConvertToServerEntity()
                                : undefined)
                            : val;
                }
            }
            return serverEntity;
        };
        Entity.prototype.CopyTo = function (entity) {
            entity.Id(this.Id());
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
        Entity.prototype.OnSaving = function () {
            return true;
        };
        Entity.prototype.OnSaved = function () {
            return true;
        };
        Entity.prototype.OnDeleted = function () {
            return true;
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
        Album.prototype.Context = function () {
            return mapViewModel.Albums;
        };
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
        Person.prototype.Context = function () {
            return mapViewModel.Persons;
        };
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
        Job.prototype.Context = function () {
            return mapViewModel.Jobs;
        };
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
            this.Wifis = ko.observableArray();
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
            if (this.markerType !== MarkerType.Dummy)
                return;
            this.marker.setOpacity(1);
            var w1 = this.polylines[0].Waypoints[0];
            var w2 = this.polylines[0].Waypoints[1];
            this.markerType = MarkerType.Waypoint;
            splitPolyline(this.polylines[0]);
            this.SaveToServer()
                .done(function (w) {
                var wCA = ServerApi.WaypointConnections;
                wCA.Disconnect(w1.Id(), w2.Id());
                wCA.Connect(w1.Id(), w.Id);
                wCA.Connect(w2.Id(), w.Id);
            });
        };
        Waypoint.prototype.IsInPolyline = function (polyline) {
            for (var _i = 0, _a = polyline.Waypoints; _i < _a.length; _i++) {
                var wp = _a[_i];
                if (this === wp)
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
            if (!this.IsDummy() && polyline.DummyHandle !== this) {
                polyline.Waypoints.push(this);
                polyline.addLatLng(this.LatLng);
                polyline.redraw();
            }
            if (this.polylines[0] !== polyline) {
                this.LatLng.Polylines.push(polyline);
                this.polylines.push(polyline);
            }
            //ServerApi.WaypointConnectionApi.GetDefault().
            return true;
        };
        Waypoint.prototype.RemoveFromPolyline = function (polyline) {
            //if (!this.IsInPolyline(polyline))
            //    return false;
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
        Waypoint.prototype.Context = function () {
            return mapViewModel.Waypoints;
        };
        Waypoint.prototype.DeleteOnServer = function () {
            var _this = this;
            return _super.prototype.DeleteOnServer.call(this).done(function () { return _this.RemoveFromMap(); });
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
                if (_this.marker !== undefined) {
                    var label = _this.marker.getLabel();
                    if (label !== undefined) {
                        _this.marker.updateLabelContent(d);
                    }
                    else {
                        _this.marker.bindLabel(d, {
                            direction: "auto"
                        });
                    }
                }
            });
        }
        Harbour.prototype.RemoveIfHasZeroOrOnePolylines = function () {
            return false;
        };
        Harbour.prototype.Context = function () {
            return mapViewModel.Harbours;
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
        Address.prototype.Context = function () {
            return mapViewModel.Addresses;
        };
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
        Image.prototype.Context = function () {
            return mapViewModel.Images;
        };
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
            this.Album = ko.observable(new Album());
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
        TackBase.prototype.ConvertToServerEntity = function (idOnly) {
            this.EndId(this.End().Id());
            this.StartId(this.Start().Id());
            var sEntity = _super.prototype.ConvertToServerEntity.call(this, idOnly);
            var crew = new Array();
            for (var _i = 0, _a = this.Persons(); _i < _a.length; _i++) {
                var person = _a[_i];
                crew.push({ PersonId: person.Id(), TackId: this.Id() });
            }
            sEntity.Crew = crew;
            return sEntity;
        };
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
        Trip.prototype.Context = function () {
            return mapViewModel.Trips;
        };
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
            this.WindSpeed = ko.observable();
            this.WindDirection = ko.observable();
            this.SpecialOccurences = ko.observable();
        }
        LogBookEntry.prototype.Context = function () {
            return mapViewModel.LogBookEntries;
        };
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
                    if (mapViewModel.TripHelper.Editing() === undefined)
                        return false;
                    var tacks = mapViewModel.TripHelper.Editing().Tacks;
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
        Tack.prototype.Context = function () {
            return mapViewModel.Tacks;
        };
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
        Comment.prototype.Context = function () {
            throw "not implemented";
        };
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
        Location.prototype.Context = function () {
            return mapViewModel.Locations;
        };
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
    var Wifi = (function (_super) {
        __extends(Wifi, _super);
        function Wifi() {
            _super.apply(this, arguments);
            this.Name = ko.observable();
            this.Password = ko.observable();
            this.Speed = ko.observable();
            this.Free = ko.observable();
            this.HarbourId = ko.observable();
            this.Harbour = ko.observable();
        }
        Wifi.prototype.Context = function () {
            return mapViewModel.Wifis;
        };
        return Wifi;
    }(Entity));
    ClientModel.Wifi = Wifi;
    var ContentPage = (function (_super) {
        __extends(ContentPage, _super);
        function ContentPage() {
            _super.apply(this, arguments);
            this.Title = ko.observable();
            this.Content = ko.observable();
        }
        ContentPage.prototype.Context = function () {
            return mapViewModel.ContentPages;
        };
        return ContentPage;
    }(Entity));
    ClientModel.ContentPage = ContentPage;
})(ClientModel || (ClientModel = {}));
var MarkerType;
(function (MarkerType) {
    MarkerType[MarkerType["Harbour"] = 0] = "Harbour";
    MarkerType[MarkerType["Dummy"] = 1] = "Dummy";
    MarkerType[MarkerType["Waypoint"] = 2] = "Waypoint";
    MarkerType[MarkerType["WeatherStation"] = 3] = "WeatherStation";
})(MarkerType || (MarkerType = {}));

//# sourceMappingURL=data:application/json;base64,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c291cmNlc0NvbnRlbnQiOlsibW9kdWxlIENsaWVudE1vZGVsIHtcclxuXHJcbiAgICBpbXBvcnQgU0VudGl0eSA9IFNlcnZlck1vZGVsLkVudGl0eVxyXG5cclxuICAgIGV4cG9ydCBpbnRlcmZhY2UgSUVudGl0eSB7XHJcbiAgICAgICAgSWQ6IEtub2Nrb3V0T2JzZXJ2YWJsZTxudW1iZXI+O1xyXG4gICAgICAgIENsaWVudElkOiBudW1iZXI7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGFic3RyYWN0IGNsYXNzIEVudGl0eSBpbXBsZW1lbnRzIElFbnRpdHkge1xyXG4gICAgICAgIGNvbnN0cnVjdG9yKCkge1xyXG4gICAgICAgICAgICBFbnRpdHkuZW50aXR5RGJbdGhpcy5DbGllbnRJZC50b1N0cmluZygpXSA9IHRoaXM7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBBbGJ1bUlkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgQ29tbWVudExpc3RJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIEFsYnVtID0gQ3JlYXRlT2JzZXJ2YWJsZTxBbGJ1bT4oe1xyXG4gICAgICAgICAgICBGb3JlaWduS2V5OiAodDogdGhpcykgPT4gdC5BbGJ1bUlkXHJcbiAgICAgICAgfSk7XHJcbiAgICAgICAgSW5zZXJ0RGF0ZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPihEYXRlIGFzIGFueSk7XHJcbiAgICAgICAgVXBkYXRlRGF0ZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPihEYXRlIGFzIGFueSk7XHJcblxyXG4gICAgICAgIHByaXZhdGUgc3RhdGljIGNsaWVudElkQ291bnRlciA9IDA7XHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZW50aXR5RGIgPSB7fTtcclxuXHJcbiAgICAgICAgQ2xpZW50SWQgPSBFbnRpdHkuY2xpZW50SWRDb3VudGVyKys7XHJcblxyXG4gICAgICAgIHByb3RlY3RlZCBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuR2V0QXBpKHRoaXMpO1xyXG5cclxuICAgICAgICBEZWxldGVPblNlcnZlcigpOiBKUXVlcnlQcm9taXNlPFNFbnRpdHk+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuU2VydmVyQXBpLkRlbGV0ZSh0aGlzLklkKCkpLmRvbmUoKCkgPT4geyB0aGlzLnJlbW92ZUZyb21Db250ZXh0KCk7IH0pO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIFNhdmVUb1NlcnZlcigpOiBKUXVlcnlQcm9taXNlPFNFbnRpdHk+IHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuSWQoKSA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHRoaXMuU2VydmVyQXBpLkNyZWF0ZSh0aGlzLkNvbnZlcnRUb1NlcnZlckVudGl0eSgpKVxyXG4gICAgICAgICAgICAgICAgICAgIC5kb25lKGRhdGEgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLnNhdmVkU3RhdGVzPVtdO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkxvYWRGcm9tU2VydmVyRW50aXR5KGRhdGEpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLmFkZFRvQ29udGV4dCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICByZXR1cm4gdGhpcy5TZXJ2ZXJBcGkuVXBkYXRlKHRoaXMuQ29udmVydFRvU2VydmVyRW50aXR5KCkpXHJcbiAgICAgICAgICAgICAgICAuZG9uZSgoZGF0YSkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuc2F2ZWRTdGF0ZXM9W107XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Mb2FkRnJvbVNlcnZlckVudGl0eShkYXRhKTtcclxuICAgICAgICAgICAgICAgIH0pOztcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHByaXZhdGUgTG9hZE5hdmlnYXRpb25Qcm9wZXJ0aWVzKCk6IHZvaWQge1xyXG4gICAgICAgICAgICBmb3IgKGxldCBwcm9wTmFtZSBpbiB0aGlzLkdldE9ic2VydmFibGVOYW1lcygpKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwcm9wID0gPEtub2Nrb3V0T2JzZXJ2YWJsZTxhbnk+PnRoaXNbcHJvcE5hbWVdO1xyXG4gICAgICAgICAgICAgICAgLy9pZiAocHJvcC4pXHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHByaXZhdGUgYWRkVG9Db250ZXh0KCkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5Db250ZXh0KCkuaW5kZXhPZih0aGlzKSAhPT0gLTEpXHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIHRoaXMuQ29udGV4dCgpLnB1c2godGhpcyk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBwcml2YXRlIHJlbW92ZUZyb21Db250ZXh0KCkge1xyXG4gICAgICAgICAgICB0aGlzLkNvbnRleHQoKS5yZW1vdmUodGhpcyk7XHJcbiAgICAgICAgfVxyXG4gICAgICAgIFxyXG4gICAgICAgIExvYWRGcm9tU2VydmVyRW50aXR5KHNlcnZlckVudGl0eTogU0VudGl0eSk6IHRoaXMge1xyXG4gICAgICAgICAgICBpZiAoc2VydmVyRW50aXR5LlByb2Nlc3NPblNlcnZlciA9PT0gZmFsc2UpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gdGhpcztcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcCBvZiB0aGlzLkdldE9ic2VydmFibGVOYW1lcygpKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBzVmFsID0gc2VydmVyRW50aXR5W3Byb3BdO1xyXG4gICAgICAgICAgICAgICAgaWYgKHNWYWwgIT09IHVuZGVmaW5lZCAmJiBzVmFsICE9PSBudWxsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHNWYWwgaW5zdGFuY2VvZiBBcnJheSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBvYmogb2Ygc1ZhbCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY29uc3QgZW50aXR5ID0gRW50aXR5LmVudGl0eURiW29iai5DbGllbnRJZC50b1N0cmluZygpXTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGlmIChlbnRpdHkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBlbnRpdHkuTG9hZEZyb21TZXJ2ZXJFbnRpdHkob2JqKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGNvbnN0IGNWYWwgPSB0aGlzW3Byb3BdKCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmIChjVmFsIGluc3RhbmNlb2YgRW50aXR5KVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgY1ZhbC5Mb2FkRnJvbVNlcnZlckVudGl0eShzVmFsKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdGhpc1twcm9wXShzVmFsKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIHRoaXM7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBDb252ZXJ0VG9TZXJ2ZXJFbnRpdHkoaWRPbmx5OiBib29sZWFuID0gZmFsc2UpOiBTRW50aXR5IHtcclxuICAgICAgICAgICAgaWYgKGlkT25seSkge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHsgSWQ6IHRoaXMuSWQoKSwgUHJvY2Vzc09uU2VydmVyOiBmYWxzZSB9O1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGNvbnN0IGlzVXBkYXRlID0gdGhpcy5JZCgpICE9PSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgIGNvbnN0IHNlcnZlckVudGl0eSA9IHsgQ2xpZW50SWQ6IHRoaXMuQ2xpZW50SWQgfTtcclxuICAgICAgICAgICAgY29uc3QgZW50aXR5ID0gdGhpcztcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcE5hbWUgb2YgdGhpcy5HZXRPYnNlcnZhYmxlTmFtZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgcHJvcCA9IGVudGl0eVtwcm9wTmFtZV07XHJcbiAgICAgICAgICAgICAgICBjb25zdCB2YWwgPSBwcm9wKCk7XHJcbiAgICAgICAgICAgICAgICBpZiAodmFsICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAodmFsIGluc3RhbmNlb2YgQXJyYXkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29uc3QgYXJyID0gbmV3IEFycmF5KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGZvciAobGV0IGVsZW0gb2YgdmFsIGFzIEVudGl0eVtdKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBpZiAoZWxlbS5JZCgpID09PSB1bmRlZmluZWQgfHxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBpc1VwZGF0ZSAmJiBwcm9wLlVwZGF0ZVRyYW5zZmVyTW9kZSA9PT0gVHJhbnNmZXJNb2RlLkluY2x1ZGUgfHxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAhaXNVcGRhdGUgJiYgcHJvcC5BZGRUcmFuc2Zlck1vZGUgPT09IFRyYW5zZmVyTW9kZS5JbmNsdWRlKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIGFyci5wdXNoKGVsZW0uQ29udmVydFRvU2VydmVyRW50aXR5KCkpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHNlcnZlckVudGl0eVtwcm9wTmFtZV0gPSBhcnI7XHJcbiAgICAgICAgICAgICAgICAgICAgfSBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHNlcnZlckVudGl0eVtwcm9wTmFtZV0gPSB2YWwgaW5zdGFuY2VvZiBFbnRpdHlcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgID8gKHZhbC5JZCgpID09PSB1bmRlZmluZWQgfHxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICBpc1VwZGF0ZSAmJiBwcm9wLlVwZGF0ZVRyYW5zZmVyTW9kZSA9PT0gVHJhbnNmZXJNb2RlLkluY2x1ZGUgfHxcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAhaXNVcGRhdGUgJiYgcHJvcC5BZGRUcmFuc2Zlck1vZGUgPT09IFRyYW5zZmVyTW9kZS5JbmNsdWRlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgPyB2YWwuQ29udmVydFRvU2VydmVyRW50aXR5KClcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA6IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIDogdmFsO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiBzZXJ2ZXJFbnRpdHkgYXMgYW55O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29weVRvKGVudGl0eTogdGhpcykge1xyXG4gICAgICAgICAgICBlbnRpdHkuSWQodGhpcy5JZCgpKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIHByaXZhdGUgc2F2ZWRTdGF0ZXM9bmV3IEFycmF5KCk7XHJcblxyXG4gICAgICAgIFNhdmVTdGF0ZT0gKGFscmVhZHlTYXZlZEVudGl0aWVzID0gbmV3IEFycmF5PEVudGl0eT4oKSkgPT4ge1xyXG4gICAgICAgICAgICBjb25zdCBzYXZlZFN0YXRlID0gbmV3IE9iamVjdCgpO1xyXG4gICAgICAgICAgICBpZiAoYWxyZWFkeVNhdmVkRW50aXRpZXMuaW5kZXhPZih0aGlzKSAhPT0gLTEpXHJcbiAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgIGFscmVhZHlTYXZlZEVudGl0aWVzLnB1c2godGhpcyk7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHByb3Agb2YgdGhpcy5HZXRPYnNlcnZhYmxlTmFtZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgbGV0IHZhbCA9IGtvLnVud3JhcCh0aGlzW3Byb3BdKTtcclxuICAgICAgICAgICAgICAgIGlmICh2YWwgaW5zdGFuY2VvZiBBcnJheSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHZhbCA9IHZhbC5zbGljZSgwKTtcclxuICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBlbGVtIG9mIHZhbCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAoZWxlbSBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGVsZW0uU2F2ZVN0YXRlKGFscmVhZHlTYXZlZEVudGl0aWVzKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9IGVsc2UgaWYgKHZhbCBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICB2YWwuU2F2ZVN0YXRlKGFscmVhZHlTYXZlZEVudGl0aWVzKTtcclxuICAgICAgICAgICAgICAgIHNhdmVkU3RhdGVbcHJvcF0gPSB2YWw7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgdGhpcy5zYXZlZFN0YXRlcy5wdXNoKHNhdmVkU3RhdGUpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgcHJvdGVjdGVkIEdldE9ic2VydmFibGVOYW1lcygpOiBzdHJpbmdbXSB7XHJcbiAgICAgICAgICAgIGNvbnN0IG91dCA9IG5ldyBBcnJheTxzdHJpbmc+KCk7XHJcbiAgICAgICAgICAgIGNvbnN0IGVudGl0eSA9IHRoaXM7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHByb3AgaW4gZW50aXR5KVxyXG4gICAgICAgICAgICAgICAgaWYgKGVudGl0eS5oYXNPd25Qcm9wZXJ0eShwcm9wKSlcclxuICAgICAgICAgICAgICAgICAgICBpZiAoa28uaXNXcml0ZWFibGVPYnNlcnZhYmxlKGVudGl0eVtwcm9wXSkgJiYgIWVudGl0eVtwcm9wXS5CbG9jaylcclxuICAgICAgICAgICAgICAgICAgICAgICAgb3V0LnB1c2gocHJvcCk7XHJcbiAgICAgICAgICAgIHJldHVybiBvdXQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBSZXZlcnRTdGF0ZSA9IChpZ25vcmVFcnJvcjogYm9vbGVhbiA9IGZhbHNlLCBhbHJlYWR5UmV2ZXJ0ZWRFbnRpdGllcyA9IG5ldyBBcnJheTxFbnRpdHk+KCkpID0+IHtcclxuICAgICAgICAgICAgaWYgKGFscmVhZHlSZXZlcnRlZEVudGl0aWVzLmluZGV4T2YodGhpcykgIT09IC0xKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICBhbHJlYWR5UmV2ZXJ0ZWRFbnRpdGllcy5wdXNoKHRoaXMpO1xyXG4gICAgICAgICAgICBpZiAodGhpcy5zYXZlZFN0YXRlcy5sZW5ndGg9PT0gMClcclxuICAgICAgICAgICAgICAgIGlmIChpZ25vcmVFcnJvcilcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm47XHJcbiAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgdGhyb3cgXCJObyBzYXZlZCBzdGF0ZVwiO1xyXG4gICAgICAgICAgICBmb3IgKGxldCBwcm9wIG9mIHRoaXMuR2V0T2JzZXJ2YWJsZU5hbWVzKCkpIHtcclxuICAgICAgICAgICAgICAgIGNvbnN0IHZhbCA9IHRoaXMuc2F2ZWRTdGF0ZXNbMF1bcHJvcF07XHJcbiAgICAgICAgICAgICAgICAvL3RoaXMuc2F2ZWRTdGF0ZVtwcm9wXSA9IHZhbDtcclxuICAgICAgICAgICAgICAgIHRoaXNbcHJvcF0odmFsKTtcclxuICAgICAgICAgICAgICAgIGlmICh2YWwgaW5zdGFuY2VvZiBFbnRpdHkpXHJcbiAgICAgICAgICAgICAgICAgICAgdmFsLlJldmVydFN0YXRlKGlnbm9yZUVycm9yLCBhbHJlYWR5UmV2ZXJ0ZWRFbnRpdGllcyk7XHJcbiAgICAgICAgICAgICAgICBlbHNlIGlmICh2YWwgaW5zdGFuY2VvZiBBcnJheSlcclxuICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBlbGVtIG9mIHZhbCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAoZWxlbSBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGVsZW0uUmV2ZXJ0U3RhdGUoaWdub3JlRXJyb3IsIGFscmVhZHlSZXZlcnRlZEVudGl0aWVzKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgdGhpcy5zYXZlZFN0YXRlcy5zaGlmdCgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgT25TYXZpbmcoKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgT25TYXZlZCgpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBPbkRlbGV0ZWQoKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuXHJcbiAgICAgICAgYWJzdHJhY3QgQ29udGV4dCgpOiBLbm9ja291dE9ic2VydmFibGVBcnJheTx0aGlzPjtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgQWxidW0gZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIEltYWdlcyA9IGtvLm9ic2VydmFibGVBcnJheTxJbWFnZT4oKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5BbGJ1bXMgYXMgYW55O1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgUGVyc29uIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBMYXN0TmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIEZpcnN0TmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIEZ1bGxOYW1lID0ga28uY29tcHV0ZWQoKCkgPT4gdGhpcy5GaXJzdE5hbWUoKSArIFwiIFwiICsgdGhpcy5MYXN0TmFtZSgpKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5QZXJzb25zIGFzIGFueTtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBKb2IgZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIER1ZVRvID0ga28ub2JzZXJ2YWJsZTxEYXRlPihEYXRlIGFzIGFueSk7XHJcbiAgICAgICAgQXNzaWduZWRUbyA9IGtvLm9ic2VydmFibGU8UGVyc29uPigpO1xyXG4gICAgICAgIEFzc2lnbmVkVG9JZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFRpdGxlID0ga28ub2JzZXJ2YWJsZTxTdHJpbmc+KCk7XHJcbiAgICAgICAgQ29udGVudCA9IGtvLm9ic2VydmFibGU8U3RyaW5nPigpO1xyXG4gICAgICAgIERvbmUgPSBrby5vYnNlcnZhYmxlPGJvb2xlYW4+KCk7XHJcbiAgICAgICAgU3VwZXJKb2IgPSBrby5vYnNlcnZhYmxlPEpvYj4oKTtcclxuICAgICAgICBTdXBlckpvYklkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgVHJpcCA9IGtvLm9ic2VydmFibGU8VHJpcD4oKTtcclxuICAgICAgICBUcmlwSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBTdWJKb2JzID0ga28ub2JzZXJ2YWJsZUFycmF5PEpvYj4oKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5Kb2JzIGFzIGFueTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFdheXBvaW50IGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBjb25zdHJ1Y3RvcihsYXRMbmc6IEwuTGF0TG5nLCBtYXJrZXJUeXBlOiBNYXJrZXJUeXBlLCBtYXA6IEwubWFwYm94Lk1hcCk7XHJcbiAgICAgICAgY29uc3RydWN0b3IobWFya2VyVHlwZTogTWFya2VyVHlwZSwgbWFwOiBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIGNvbnN0cnVjdG9yKGxhdExuZzogTC5MYXRMbmcgfCBNYXJrZXJUeXBlLFxyXG4gICAgICAgICAgICBtYXJrZXJUeXBlOiBNYXJrZXJUeXBlIHwgTC5tYXBib3guTWFwLFxyXG4gICAgICAgICAgICBwcm90ZWN0ZWQgTWFwPzogTC5tYXBib3guXHJcbiAgICAgICAgICAgIE1hcCkge1xyXG4gICAgICAgICAgICBzdXBlcigpO1xyXG4gICAgICAgICAgICBpZiAoTWFwID09PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIGlmICh0eXBlb2YgbWFya2VyVHlwZSA9PSBcIm51bWJlclwiKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgTWFwID0gKChsYXRMbmcgYXMgYW55KSBhcyBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgICAgICAgICAgICAgIGxhdExuZyA9IChtYXJrZXJUeXBlIGFzIE1hcmtlclR5cGUpO1xyXG4gICAgICAgICAgICAgICAgICAgIG1hcmtlclR5cGUgPSBNYXA7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB0aGlzLk1hcCA9ICgobWFya2VyVHlwZSkgYXMgTC5tYXBib3guTWFwKTtcclxuICAgICAgICAgICAgICAgIHRoaXMuTGF0TG5nID0gbmV3IEwuTGF0TG5nKDAsIDApO1xyXG4gICAgICAgICAgICAgICAgbWFya2VyVHlwZSA9IChsYXRMbmcgYXMgTWFya2VyVHlwZSk7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkxhdGl0dWRlKChsYXRMbmcgYXMgTC5MYXRMbmcpLmxhdCk7XHJcbiAgICAgICAgICAgICAgICB0aGlzLkxvbmdpdHVkZSgobGF0TG5nIGFzIEwuTGF0TG5nKS5sbmcpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHRoaXMuTGF0TG5nID0gbmV3IEwuTGF0TG5nKHRoaXMuTGF0aXR1ZGUoKSwgdGhpcy5Mb25naXR1ZGUoKSk7XHJcbiAgICAgICAgICAgIHRoaXMuTGF0aXR1ZGUuc3Vic2NyaWJlKCh2YWx1ZSkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuTGF0TG5nLmxhdCAhPT0gdmFsdWUpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkxhdExuZy5sYXQgPSB2YWx1ZTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLlJlZHJhdygpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgdGhpcy5Mb25naXR1ZGUuc3Vic2NyaWJlKCh2YWx1ZSkgPT4ge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMuTGF0TG5nLmxuZyAhPT0gdmFsdWUpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkxhdExuZy5sbmcgPSB2YWx1ZTtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLlJlZHJhdygpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXJUeXBlID0gbWFya2VyVHlwZTtcclxuICAgICAgICAgICAgdGhpcy5MYXRMbmcuUG9seWxpbmVzID0gbmV3IEFycmF5KCk7XHJcbiAgICAgICAgICAgIHRoaXMuTGF0TG5nLldheXBvaW50ID0gdGhpcztcclxuICAgICAgICAgICAgLy90aGlzLkxhdGl0dWRlLnN1YnNjcmliZSgodmFsdWUpID0+IHtcclxuICAgICAgICAgICAgLy8gICAgaWYgKHRoaXMuTGF0TG5nLmxhdCAhPT0gdmFsdWUpIHtcclxuICAgICAgICAgICAgLy8gICAgICAgIHRoaXMuUmVkcmF3KCk7XHJcbiAgICAgICAgICAgIC8vICAgIH1cclxuICAgICAgICAgICAgLy99KTtcclxuICAgICAgICAgICAgLy90aGlzLkxvbmdpdHVkZS5zdWJzY3JpYmUoKHZhbHVlKSA9PiB7XHJcbiAgICAgICAgICAgIC8vICAgIGlmICh0aGlzLkxhdExuZy5sbmcgIT09IHZhbHVlKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICB0aGlzLlJlZHJhdygpO1xyXG4gICAgICAgICAgICAvLyAgICB9XHJcbiAgICAgICAgICAgIC8vfSk7XHJcbiAgICAgICAgICAgIHRoaXMuUHJlY2Vzc29yLkJsb2NrID0gdHJ1ZTtcclxuICAgICAgICAgICAgdGhpcy5EaXN0YW5jZS5CbG9jayA9IHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBQb3B1cChjb250ZW50OiBzdHJpbmcpO1xyXG4gICAgICAgIFBvcHVwKCk7XHJcbiAgICAgICAgUG9wdXAoY29udGVudD86IHN0cmluZykge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5wb3B1cCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICB0aGlzLk1hcC5yZW1vdmVMYXllcih0aGlzLnBvcHVwKTtcclxuICAgICAgICAgICAgICAgIHRoaXMucG9wdXAgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKGNvbnRlbnQgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgLy90aGlzLnBvcHVwID0gTC5wb3B1cCgpO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5tYXJrZXIuYmluZFBvcHVwKGNvbnRlbnQpOyAvLy5vcGVuUG9wdXAoKTtcclxuXHJcblxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuXHJcbiAgICAgICAgU2hvdyhoaWdobGlnaHQ6IGJvb2xlYW4gPSBmYWxzZSk6IHZvaWQge1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlci5zZXRPcGFjaXR5KHRoaXMubWFya2VyLldheXBvaW50LklzRHVtbXkoKSA/IDAuNSA6IDEpO1xyXG4gICAgICAgICAgICBpZiAoaGlnaGxpZ2h0KVxyXG4gICAgICAgICAgICAgICAgJCh0aGlzLm1hcmtlci5faWNvbikuYWRkQ2xhc3MoXCJleHBvc2VcIik7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBIaWRlKCk6IHZvaWQge1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlci5zZXRPcGFjaXR5KDAuMSk7XHJcbiAgICAgICAgICAgICQodGhpcy5tYXJrZXIuX2ljb24pLnJlbW92ZUNsYXNzKFwiZXhwb3NlXCIpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgUmVkcmF3KHVwZGF0ZVBvbHlsaW5lcyA9IHRydWUpOiB2b2lkIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMubWFya2VyICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICB0aGlzLm1hcmtlci5zZXRMYXRMbmcodGhpcy5MYXRMbmcpO1xyXG4gICAgICAgICAgICBpZiAodXBkYXRlUG9seWxpbmVzKVxyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgaSA9IDA7IGkgPCB0aGlzLnBvbHlsaW5lcy5sZW5ndGg7IGkrKylcclxuICAgICAgICAgICAgICAgICAgICByZWRyYXdQb2x5bGluZSh0aGlzLnBvbHlsaW5lc1tpXSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBHZXRDb25uZWN0ZWRXYXlwb2ludHMoKTogV2F5cG9pbnRbXSB7XHJcbiAgICAgICAgICAgIGNvbnN0IHJldCA9IG5ldyBBcnJheTxXYXlwb2ludD4oKTtcclxuICAgICAgICAgICAgZm9yIChsZXQgbGluZSBvZiB0aGlzLnBvbHlsaW5lcykge1xyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgd2F5cG9pbnQgb2YgbGluZS5XYXlwb2ludHMpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAod2F5cG9pbnQgIT09IHRoaXMpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJldC5wdXNoKHdheXBvaW50KTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICByZXR1cm4gcmV0LnNvcnQoKHcxLCB3MikgPT4ge1xyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHcxLkxhdExuZy5kaXN0YW5jZVRvKHRoaXMuTGF0TG5nKSAtIHcyLkxhdExuZy5kaXN0YW5jZVRvKHRoaXMuTGF0TG5nKTtcclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBjb252ZXJ0RnJvbUR1bW15SGFuZGxlKCkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5tYXJrZXJUeXBlICE9PSBNYXJrZXJUeXBlLkR1bW15KVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuO1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlci5zZXRPcGFjaXR5KDEpO1xyXG4gICAgICAgICAgICB2YXIgdzEgPSB0aGlzLnBvbHlsaW5lc1swXS5XYXlwb2ludHNbMF07XHJcbiAgICAgICAgICAgIHZhciB3MiA9IHRoaXMucG9seWxpbmVzWzBdLldheXBvaW50c1sxXTtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXJUeXBlID0gTWFya2VyVHlwZS5XYXlwb2ludDtcclxuICAgICAgICAgICAgc3BsaXRQb2x5bGluZSh0aGlzLnBvbHlsaW5lc1swXSk7XHJcbiAgICAgICAgICAgIHRoaXMuU2F2ZVRvU2VydmVyKClcclxuICAgICAgICAgICAgICAgIC5kb25lKCh3KSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3Qgd0NBID0gU2VydmVyQXBpLldheXBvaW50Q29ubmVjdGlvbnM7XHJcbiAgICAgICAgICAgICAgICAgICAgd0NBLkRpc2Nvbm5lY3QodzEuSWQoKSwgdzIuSWQoKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgd0NBLkNvbm5lY3QodzEuSWQoKSwgdy5JZCk7XHJcbiAgICAgICAgICAgICAgICAgICAgd0NBLkNvbm5lY3QodzIuSWQoKSwgdy5JZCk7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIElzSW5Qb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSk6IGJvb2xlYW4ge1xyXG4gICAgICAgICAgICBmb3IgKGNvbnN0IHdwIG9mIHBvbHlsaW5lLldheXBvaW50cykge1xyXG4gICAgICAgICAgICAgICAgaWYgKHRoaXMgPT09IHdwKVxyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJlbW92ZUZyb21NYXAoKSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLm1hcmtlclR5cGUgIT09IE1hcmtlclR5cGUuRHVtbXkpXHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBwb2x5bGluZSBvZiB0aGlzLnBvbHlsaW5lcylcclxuICAgICAgICAgICAgICAgICAgICByZW1vdmVQb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIHRoaXMuTWFwLnJlbW92ZUxheWVyKHRoaXMubWFya2VyKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEFkZFRvUG9seWxpbmUocG9seWxpbmU6IEwuUG9seWxpbmUpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuSXNJblBvbHlsaW5lKHBvbHlsaW5lKSlcclxuICAgICAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICAgICAgaWYgKCF0aGlzLklzRHVtbXkoKSAmJiBwb2x5bGluZS5EdW1teUhhbmRsZSAhPT0gdGhpcykge1xyXG4gICAgICAgICAgICAgICAgcG9seWxpbmUuV2F5cG9pbnRzLnB1c2godGhpcyk7XHJcbiAgICAgICAgICAgICAgICBwb2x5bGluZS5hZGRMYXRMbmcodGhpcy5MYXRMbmcpO1xyXG4gICAgICAgICAgICAgICAgcG9seWxpbmUucmVkcmF3KCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKHRoaXMucG9seWxpbmVzWzBdICE9PSBwb2x5bGluZSkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5MYXRMbmcuUG9seWxpbmVzLnB1c2gocG9seWxpbmUpO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5wb2x5bGluZXMucHVzaChwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgLy9TZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKS5cclxuICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBSZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmU6IEwuUG9seWxpbmUpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgLy9pZiAoIXRoaXMuSXNJblBvbHlsaW5lKHBvbHlsaW5lKSlcclxuICAgICAgICAgICAgLy8gICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgICAgICByZW1vdmVGcm9tQXJyYXkocG9seWxpbmUuV2F5cG9pbnRzLCB0aGlzKTtcclxuICAgICAgICAgICAgcmVtb3ZlRnJvbUFycmF5KHRoaXMucG9seWxpbmVzLCBwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheSh0aGlzLkxhdExuZy5Qb2x5bGluZXMsIHBvbHlsaW5lKTtcclxuICAgICAgICAgICAgcmVtb3ZlRnJvbUFycmF5KHBvbHlsaW5lLmdldExhdExuZ3MoKSwgdGhpcy5MYXRMbmcpO1xyXG4gICAgICAgICAgICBwb2x5bGluZS5yZWRyYXcoKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBSZW1vdmVJZkhhc1plcm9Pck9uZVBvbHlsaW5lcygpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBXYXlwb2ludE51bWJlciA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIHByaXZhdGUgcG9seWxpbmVzID0gbmV3IEFycmF5PEwuUG9seWxpbmU+KCk7XHJcblxyXG5cclxuICAgICAgICBTZXRMYXRMbmcobGF0TG5nOiBMLkxhdExuZywgdXBkYXRlUG9seWxpbmVzID0gdHJ1ZSk6IHZvaWQge1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5sYXQgPSBsYXRMbmcubGF0O1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5sbmcgPSBsYXRMbmcubG5nO1xyXG4gICAgICAgICAgICB0aGlzLkxhdGl0dWRlKGxhdExuZy5sYXQpO1xyXG4gICAgICAgICAgICB0aGlzLkxvbmdpdHVkZShsYXRMbmcubG5nKTtcclxuICAgICAgICAgICAgdGhpcy5SZWRyYXcodXBkYXRlUG9seWxpbmVzKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIENlbnRlck9uTWFwKCkge1xyXG4gICAgICAgICAgICB0aGlzLk1hcC5zZXRWaWV3KHRoaXMuTGF0TG5nKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIElzRHVtbXkoKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLm1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuRHVtbXk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBMYXRpdHVkZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigwKTtcclxuICAgICAgICBMb25naXR1ZGUgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oMCk7XHJcbiAgICAgICAgRGlzdGFuY2UgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBQcmVjZXNzb3IgPSBrby5vYnNlcnZhYmxlPFdheXBvaW50PigpO1xyXG4gICAgICAgIFJvdXRlRGlzdGFuY2UgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBSb3V0ZVByZWNlc3NvciA9IGtvLm9ic2VydmFibGU8V2F5cG9pbnQ+KCk7XHJcbiAgICAgICAgcHJvdGVjdGVkIHBvcHVwOiBMLlBvcHVwO1xyXG4gICAgICAgIExhdExuZzogTC5MYXRMbmc7XHJcbiAgICAgICAgcHJvdGVjdGVkIG1hcmtlclR5cGU6IE1hcmtlclR5cGUgfCBMLm1hcGJveC5NYXA7XHJcbiAgICAgICAgbWFya2VyOiBMLk1hcmtlcjtcclxuXHJcbiAgICAgICAgTmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIERlc2NyaXB0aW9uID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgV2lmaXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8V2lmaT4oKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5XYXlwb2ludHMgYXMgYW55O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgRGVsZXRlT25TZXJ2ZXIoKTogSlF1ZXJ5UHJvbWlzZTxPYmplY3Q+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHN1cGVyLkRlbGV0ZU9uU2VydmVyKCkuZG9uZSgoKSA9PiB0aGlzLlJlbW92ZUZyb21NYXAoKSk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBIYXJib3VyIGV4dGVuZHMgV2F5cG9pbnQge1xyXG5cclxuICAgICAgICBjb25zdHJ1Y3RvcihtYXA6IEwubWFwYm94Lk1hcCk7XHJcbiAgICAgICAgY29uc3RydWN0b3IobGF0TG5nOiBMLkxhdExuZywgbWFwOiBMLm1hcGJveC5NYXApO1xyXG4gICAgICAgIGNvbnN0cnVjdG9yKGxhdExuZzogTC5MYXRMbmcgfCBMLm1hcGJveC5NYXAsIG1hcD86IEwubWFwYm94Lk1hcCkge1xyXG4gICAgICAgICAgICBzdXBlcihsYXRMbmcgYXMgTC5MYXRMbmcsIE1hcmtlclR5cGUuSGFyYm91ciwgbWFwKTtcclxuICAgICAgICAgICAgLy9pZiAobWFwKVxyXG4gICAgICAgICAgICAvL3RoaXMuRGlzdGFuY2Uuc3Vic2NyaWJlKChkKSA9PiB7XHJcbiAgICAgICAgICAgIC8vICAgIGNvbnN0IGxhYmVsID0gdGhpcy5tYXJrZXIuZ2V0TGFiZWwoKTtcclxuICAgICAgICAgICAgLy8gICAgaWYgKGQgPiAwKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICBpZiAobGFiZWwgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAvLyAgICAgICAgICAgIHRoaXMubWFya2VyLnVwZGF0ZUxhYmVsQ29udGVudChkLnRvU3RyaW5nKCkgKyBcIiBrbVwiKTtcclxuICAgICAgICAgICAgLy8gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgdGhpcy5tYXJrZXIuYmluZExhYmVsKGQudG9TdHJpbmcoKSArIFwiIGttXCIsXHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgICAgIDxhbnk+e1xyXG4gICAgICAgICAgICAvLyAgICAgICAgICAgICAgICAgICAgZGlyZWN0aW9uOiBcImF1dG9cIlxyXG4gICAgICAgICAgICAvLyAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgLy8gICAgICAgIH1cclxuICAgICAgICAgICAgLy8gICAgfSBlbHNlIGlmIChsYWJlbCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICB0aGlzLm1hcmtlci51bmJpbmRMYWJlbCgpO1xyXG4gICAgICAgICAgICAvLyAgICB9XHJcbiAgICAgICAgICAgIC8vfSk7XHJcbiAgICAgICAgICAgIHRoaXMuTmFtZS5zdWJzY3JpYmUoKGQpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLm1hcmtlciAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgbGFiZWwgPSB0aGlzLm1hcmtlci5nZXRMYWJlbCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIGlmIChsYWJlbCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMubWFya2VyLnVwZGF0ZUxhYmVsQ29udGVudChkKTtcclxuICAgICAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLm1hcmtlci5iaW5kTGFiZWwoZCxcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgZGlyZWN0aW9uOiBcImF1dG9cIlxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9IGFzIGFueSk7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEFsYnVtID0ga28ub2JzZXJ2YWJsZShuZXcgQWxidW0oKSk7XHJcblxyXG4gICAgICAgIFJlbW92ZUlmSGFzWmVyb09yT25lUG9seWxpbmVzKCk6IGJvb2xlYW4ge1xyXG4gICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBMb2NhdGlvbnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8TG9jYXRpb24+KCk7XHJcbiAgICAgICAgUmF0aW5nID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgQ29udGVudCA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIFdlYnNpdGUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5IYXJib3VycyBhcyBhbnk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBZGRyZXNzIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBTdHJlZXQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBaaXAgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBUb3duID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgQ29tbWVudCA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG5cclxuICAgICAgICBDb250ZXh0KCkge1xyXG4gICAgICAgICAgICByZXR1cm4gbWFwVmlld01vZGVsLkFkZHJlc3NlcyBhcyBhbnk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBJbWFnZSBleHRlbmRzIEVudGl0eSB7XHJcblxyXG4gICAgICAgIFBhdGggPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBIZWlnaHQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBXaWR0aCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG5cclxuICAgICAgICBDb250ZXh0KCkge1xyXG4gICAgICAgICAgICByZXR1cm4gbWFwVmlld01vZGVsLkltYWdlcyBhcyBhbnk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBhYnN0cmFjdCBjbGFzcyBUYWNrQmFzZSBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgU3RhcnREYXRlID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgRW5kRGF0ZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIFN0YXJ0ID0ga28ub2JzZXJ2YWJsZTxIYXJib3VyPigpO1xyXG4gICAgICAgIFN0YXJ0SWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBFbmRJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIEVuZCA9IGtvLm9ic2VydmFibGU8SGFyYm91cj4oKTtcclxuICAgICAgICBQZXJzb25zID0ga28ub2JzZXJ2YWJsZUFycmF5PFBlcnNvbj4oKTtcclxuICAgICAgICBEaXN0YW5jZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigwKTtcclxuICAgICAgICBBbGJ1bSA9IGtvLm9ic2VydmFibGUobmV3IEFsYnVtKCkpO1xyXG5cclxuICAgICAgICBDcmV3TGlzdCA9IGtvLmNvbXB1dGVkKHtcclxuICAgICAgICAgICAgcmVhZDogKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgdmFyIHBlcnNvbnMgPSB0aGlzLlBlcnNvbnMoKTtcclxuICAgICAgICAgICAgICAgIHZhciBmaXJzdCA9IHBlcnNvbnNbMF07XHJcbiAgICAgICAgICAgICAgICBpZiAoZmlyc3QgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gXCJcIjtcclxuICAgICAgICAgICAgICAgIGlmIChwZXJzb25zLmxlbmd0aCA9PT0gMSlcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gZmlyc3QuRnVsbE5hbWUoKTtcclxuICAgICAgICAgICAgICAgIGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIGxldCBsaXN0ID0gZmlyc3QuRnVsbE5hbWUoKTtcclxuICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBpID0gMTsgaSA8IHBlcnNvbnMubGVuZ3RoOyBpKyspIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgbGlzdCArPSBgLCAke3BlcnNvbnNbaV0uRnVsbE5hbWUoKX1gO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gbGlzdDtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgZGVmZXJFdmFsdWF0aW9uOiB0cnVlXHJcblxyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBTYWlsbGluZ1RpbWUgPSBrby5jb21wdXRlZCgoKSA9PiB7XHJcbiAgICAgICAgICAgIGNvbnN0IHN0YXJ0RGF0ZSA9IHRoaXMuU3RhcnREYXRlKCk7XHJcbiAgICAgICAgICAgIGNvbnN0IGVuZERhdGUgPSB0aGlzLkVuZERhdGUoKTtcclxuICAgICAgICAgICAgaWYgKHN0YXJ0RGF0ZSA9PT0gdW5kZWZpbmVkIHx8IGVuZERhdGUgPT09IHVuZGVmaW5lZCB8fCByZW5kZXJUaW1lID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gXCJcIjtcclxuICAgICAgICAgICAgcmV0dXJuIHJlbmRlclRpbWUobmV3IERhdGUoc3RhcnREYXRlKSwgbmV3IERhdGUoZW5kRGF0ZSkpO1xyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBDb252ZXJ0VG9TZXJ2ZXJFbnRpdHkoaWRPbmx5PzogYm9vbGVhbik6IFNFbnRpdHkge1xyXG4gICAgICAgICAgICB0aGlzLkVuZElkKHRoaXMuRW5kKCkuSWQoKSk7XHJcbiAgICAgICAgICAgIHRoaXMuU3RhcnRJZCh0aGlzLlN0YXJ0KCkuSWQoKSk7XHJcbiAgICAgICAgICAgIHZhciBzRW50aXR5ID0gc3VwZXIuQ29udmVydFRvU2VydmVyRW50aXR5KGlkT25seSk7XHJcbiAgICAgICAgICAgIGNvbnN0IGNyZXcgPSBuZXcgQXJyYXk8U2VydmVyTW9kZWwuQ3Jldz4oKTtcclxuICAgICAgICAgICAgZm9yIChsZXQgcGVyc29uIG9mIHRoaXMuUGVyc29ucygpKSB7XHJcbiAgICAgICAgICAgICAgICBjcmV3LnB1c2goeyBQZXJzb25JZDogcGVyc29uLklkKCksIFRhY2tJZDogdGhpcy5JZCgpIH0pO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICg8YW55PnNFbnRpdHkpLkNyZXcgPSBjcmV3O1xyXG4gICAgICAgICAgICByZXR1cm4gc0VudGl0eTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFRyaXAgZXh0ZW5kcyBUYWNrQmFzZSB7XHJcbiAgICAgICAgTmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIENvbnRlbnQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBUYWNrcyA9IGtvLm9ic2VydmFibGVBcnJheTxUYWNrPigpO1xyXG4gICAgICAgIElzRHVtbXkgPSBrby5vYnNlcnZhYmxlPGJvb2xlYW4+KCk7XHJcblxyXG4gICAgICAgIENvbnRleHQoKSB7XHJcbiAgICAgICAgICAgIHJldHVybiBtYXBWaWV3TW9kZWwuVHJpcHMgYXMgYW55O1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgTG9nQm9va0VudHJ5IGV4dGVuZHMgVGFja0Jhc2Uge1xyXG4gICAgICAgIE1vdG9ySG91cnNTdGFydCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIE1vdG9ySG91cnNFbmQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBMb2dTdGFydCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIExvZ0VuZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFdpbmRTcGVlZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFdpbmREaXJlY3Rpb24gPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBTcGVjaWFsT2NjdXJlbmNlcyA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG5cclxuICAgICAgICBDb250ZXh0KCkge1xyXG4gICAgICAgICAgICByZXR1cm4gbWFwVmlld01vZGVsLkxvZ0Jvb2tFbnRyaWVzIGFzIGFueTtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBUYWNrIGV4dGVuZHMgVGFja0Jhc2Uge1xyXG4gICAgICAgIFdheXBvaW50cyA9IGtvLm9ic2VydmFibGVBcnJheTxTZXJ2ZXJNb2RlbC5XYXlwb2ludFRhY2s+KCk7XHJcblxyXG4gICAgICAgIENhblJlbW92ZVRhY2sgPSBrby5jb21wdXRlZCh7XHJcbiAgICAgICAgICAgIHJlYWQ6ICgpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmIChtYXBWaWV3TW9kZWwuVHJpcEhlbHBlci5FZGl0aW5nKCkgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgICAgICAgICBjb25zdCB0YWNrcyA9IG1hcFZpZXdNb2RlbC5UcmlwSGVscGVyLkVkaXRpbmcoKS5UYWNrcztcclxuICAgICAgICAgICAgICAgIGNvbnN0IGluZGV4ID0gdGFja3MuaW5kZXhPZih0aGlzKTtcclxuICAgICAgICAgICAgICAgIGNvbnN0IHByZXZUYWNrID0gdGFja3MoKVtpbmRleCAtIDFdO1xyXG4gICAgICAgICAgICAgICAgY29uc3QgbmV4dFRhY2sgPSB0YWNrcygpW2luZGV4ICsgMV07XHJcbiAgICAgICAgICAgICAgICBpZiAocHJldlRhY2sgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICBpZiAobmV4dFRhY2sgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmV0dXJuIHByZXZUYWNrLlN0YXJ0KCkgIT09IG5leHRUYWNrLlN0YXJ0KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgICAgICAgICByZXR1cm4gdHJ1ZTtcclxuICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gbmV4dFRhY2sgIT09IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgfSxcclxuICAgICAgICAgICAgZGVmZXJFdmFsdWF0aW9uOiB0cnVlXHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgICAgIENvbXB1dGVQbGFjZWhvbGRlciA9IGtvLmNvbXB1dGVkKCgpOiBzdHJpbmcgPT4ge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5TdGFydERhdGUoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIG1vbWVudCh0aGlzLlN0YXJ0RGF0ZSgpKS5mb3JtYXQoXCJMXCIpO1xyXG4gICAgICAgICAgICByZXR1cm4gXCJcIjtcclxuICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5UYWNrcyBhcyBhbnk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgQ29tbWVudCBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgVGl0bGUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBDb250ZW50ID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgUmF0aW5nID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgUGFyZW50SWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpOktub2Nrb3V0T2JzZXJ2YWJsZUFycmF5PENvbW1lbnQ+IHtcclxuICAgICAgICAgICAgdGhyb3cgXCJub3QgaW1wbGVtZW50ZWRcIjtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBMb2NhdGlvbiBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgSGFyYm91cklkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgV2Vic2l0ZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIE5hbWUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBSYXRpbmcgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBBZGRyZXNzID0ga28ub2JzZXJ2YWJsZTxBZGRyZXNzPigpO1xyXG4gICAgICAgIEFkZHJlc3NJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG5cclxuICAgICAgICBDb250ZXh0KCkge1xyXG4gICAgICAgICAgICByZXR1cm4gbWFwVmlld01vZGVsLkxvY2F0aW9ucyBhcyBhbnk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBSZXN0YXVyYW50IGV4dGVuZHMgTG9jYXRpb24ge1xyXG4gICAgfVxyXG5cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgU3VwZXJtYXJrZXQgZXh0ZW5kcyBMb2NhdGlvbiB7XHJcbiAgICB9XHJcblxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBXYXlwb2ludERpc3RhbmNlIHtcclxuICAgICAgICBjb25zdHJ1Y3RvcihwdWJsaWMgUHJlY2Vzc29yOiBXYXlwb2ludCxcclxuICAgICAgICAgICAgcHVibGljIFdheXBvaW50OiBXYXlwb2ludCxcclxuICAgICAgICAgICAgcHVibGljIERpc3RhbmNlOiBudW1iZXIsXHJcbiAgICAgICAgICAgIGNhbGN1bGF0ZXJkV2F5cG9pbnRzOiBXYXlwb2ludFtdLFxyXG4gICAgICAgICAgICBjYWxjdWxhdGVSb3V0ZTogYm9vbGVhbikge1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZyA9IFdheXBvaW50LkxhdExuZztcclxuICAgICAgICAgICAgY2FsY3VsYXRlcmRXYXlwb2ludHMucHVzaChXYXlwb2ludCk7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHdwIG9mIFdheXBvaW50LkdldENvbm5lY3RlZFdheXBvaW50cygpKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAoY2FsY3VsYXRlcmRXYXlwb2ludHMuaW5kZXhPZih3cCkgPT09IC0xKVxyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuQ29ubmVjdGVkV2F5UG9pbnRzLnB1c2god3ApO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGlmIChjYWxjdWxhdGVSb3V0ZSkge1xyXG4gICAgICAgICAgICAgICAgV2F5cG9pbnQuUm91dGVQcmVjZXNzb3IoUHJlY2Vzc29yKTtcclxuICAgICAgICAgICAgICAgIFdheXBvaW50LlJvdXRlRGlzdGFuY2UoRGlzdGFuY2UpO1xyXG4gICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgV2F5cG9pbnQuUHJlY2Vzc29yKFByZWNlc3Nvcik7XHJcbiAgICAgICAgICAgICAgICBXYXlwb2ludC5EaXN0YW5jZShEaXN0YW5jZSk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIENvbm5lY3RlZFdheVBvaW50cyA9IG5ldyBBcnJheTxXYXlwb2ludD4oKTtcclxuICAgICAgICBDb25uZWN0ZWREaXN0YW5jZXMgPSBuZXcgQXJyYXk8V2F5cG9pbnREaXN0YW5jZT4oKTtcclxuICAgICAgICBMYXRMbmc6IEwuTGF0TG5nO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBXaWZpIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBOYW1lID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgUGFzc3dvcmQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBTcGVlZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIEZyZWUgPSBrby5vYnNlcnZhYmxlPGJvb2xlYW4+KCk7XHJcbiAgICAgICAgSGFyYm91cklkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgSGFyYm91ciA9IGtvLm9ic2VydmFibGU8SGFyYm91cj4oKTtcclxuXHJcbiAgICAgICAgQ29udGV4dCgpIHtcclxuICAgICAgICAgICAgcmV0dXJuIG1hcFZpZXdNb2RlbC5XaWZpcyBhcyBhbnk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBDb250ZW50UGFnZSBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgVGl0bGUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBDb250ZW50ID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcblxyXG4gICAgICAgIENvbnRleHQoKSB7XHJcbiAgICAgICAgICAgIHJldHVybiBtYXBWaWV3TW9kZWwuQ29udGVudFBhZ2VzIGFzIGFueTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG59XHJcblxyXG5lbnVtIE1hcmtlclR5cGUge1xyXG4gICAgSGFyYm91cixcclxuICAgIER1bW15LFxyXG4gICAgV2F5cG9pbnQsXHJcbiAgICBXZWF0aGVyU3RhdGlvblxyXG59Il0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
