var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
var ClientModel;
(function (ClientModel) {
    var SPerson = ServerModel.Person;
    var SAddress = ServerModel.Address;
    var SAlbum = ServerModel.Album;
    var SCommentList = ServerModel.CommentList;
    var Entity = (function () {
        function Entity() {
            this.Id = ko.observable();
            this.AlbumId = ko.observable();
            this.CommentListId = ko.observable();
            this.Album = ko.observable();
            this.CommentList = ko.observable();
            this.clientId = ++Entity.ClientIdCounter;
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
                .done(function () {
                _this.savedState = undefined;
            });
            ;
        };
        Entity.prototype.LoadFromServerEntity = function (serverEntity) {
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var prop = _a[_i];
                var sVal = serverEntity[prop];
                if (sVal !== undefined && sVal !== null && !(sVal instanceof Array)) {
                    var cVal = this[prop]();
                    if (cVal instanceof Entity)
                        cVal.LoadFromServerEntity(sVal);
                    else
                        this[prop](sVal);
                }
            }
            return this;
        };
        Entity.prototype.ConvertToServerEntity = function (idOnly) {
            if (idOnly === void 0) { idOnly = false; }
            var serverEntity = { ClientId: this.clientId };
            var entity = this;
            for (var _i = 0, _a = this.GetObservableNames(); _i < _a.length; _i++) {
                var propName = _a[_i];
                var prop = entity[propName];
                var val = prop();
                if (val !== undefined && !(val instanceof Array)) {
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
        Entity.ClientIdCounter = 0;
        return Entity;
    }());
    ClientModel.Entity = Entity;
    var Album = (function (_super) {
        __extends(Album, _super);
        function Album() {
            _super.apply(this, arguments);
            this.Images = ko.observableArray();
            this.ServerApi = ServerApi.AlbumApi.GetDefault();
        }
        Album.prototype.CreateServerEntity = function () {
            return new SAlbum();
        };
        return Album;
    }(Entity));
    ClientModel.Album = Album;
    var CommentList = (function (_super) {
        __extends(CommentList, _super);
        function CommentList() {
            _super.apply(this, arguments);
            this.ServerApi = ServerApi.CommentListApi.GetDefault();
        }
        CommentList.prototype.CreateServerEntity = function () {
            return new SCommentList();
        };
        return CommentList;
    }(Entity));
    ClientModel.CommentList = CommentList;
    var Person = (function (_super) {
        __extends(Person, _super);
        function Person() {
            var _this = this;
            _super.apply(this, arguments);
            this.ServerApi = ServerApi.PersonApi.GetDefault();
            this.LastName = ko.observable();
            this.FirstName = ko.observable();
            this.FullName = ko.computed(function () { return _this.FirstName() + " " + _this.LastName(); });
        }
        Person.prototype.CreateServerEntity = function () {
            return new SPerson();
        };
        return Person;
    }(Entity));
    ClientModel.Person = Person;
    var Job = (function (_super) {
        __extends(Job, _super);
        function Job() {
            _super.apply(this, arguments);
            this.DueTo = ko.observable();
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
            this.ServerApi = ServerApi.JobApi.GetDefault();
        }
        Job.prototype.CreateServerEntity = function () {
            return new ServerModel.Job();
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
            this.Latitude = ko.observable();
            this.Longitude = ko.observable();
            this.Distance = ko.observable();
            this.Precessor = ko.observable();
            this.RouteDistance = ko.observable();
            this.RoutePrecessor = ko.observable();
            this.Name = ko.observable();
            this.Description = ko.observable();
            this.ServerApi = ServerApi.WaypointApi.GetDefault();
            this.Latitude(latLng.lat);
            this.Longitude(latLng.lng);
            this.LatLng = new L.LatLng(latLng.lat, latLng.lng);
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
                var wCA = ServerApi.WaypointConnectionApi.GetDefault();
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
        Waypoint.prototype.CreateServerEntity = function () {
            return new ServerModel.Waypoint();
        };
        return Waypoint;
    }(Entity));
    ClientModel.Waypoint = Waypoint;
    var Harbour = (function (_super) {
        __extends(Harbour, _super);
        function Harbour(name, latLng, map) {
            var _this = this;
            _super.call(this, latLng, MarkerType.Harbour, map);
            this.Album = ko.observable(new Album());
            this.CommentList = ko.observable(new CommentList());
            this.ServerApi = ServerApi.HarbourApi.GetDefault();
            this.Locations = ko.observableArray();
            this.Rating = ko.observable();
            this.Content = ko.observable();
            this.Website = ko.observable();
            this.Name(name);
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
        Harbour.prototype.CreateServerEntity = function () {
            return new ServerModel.Harbour();
        };
        return Harbour;
    }(Waypoint));
    ClientModel.Harbour = Harbour;
    var Trip = (function (_super) {
        __extends(Trip, _super);
        function Trip() {
            _super.apply(this, arguments);
            this.Name = ko.observable();
            this.Start = ko.observable();
            this.End = ko.observable();
            this.Content = ko.observable();
            this.Tacks = ko.observableArray();
            this.ServerApi = ServerApi.TripApi.GetDefault();
        }
        Trip.prototype.CreateServerEntity = function () {
            return new ServerModel.Trip();
        };
        return Trip;
    }(Entity));
    ClientModel.Trip = Trip;
    var Address = (function (_super) {
        __extends(Address, _super);
        function Address() {
            _super.apply(this, arguments);
            this.Street = ko.observable();
            this.Zip = ko.observable();
            this.Town = ko.observable();
            this.Comment = ko.observable();
            this.ServerApi = ServerApi.AddressApi.GetDefault();
        }
        Address.prototype.CreateServerEntity = function () {
            return new SAddress;
        };
        return Address;
    }(Entity));
    ClientModel.Address = Address;
    var Image = (function (_super) {
        __extends(Image, _super);
        function Image() {
            _super.apply(this, arguments);
            this.ParentAlbumId = ko.observable();
            this.Path = ko.observable();
            this.Height = ko.observable();
            this.Width = ko.observable();
            this.Width2 = ko.observable();
            this.ServerApi = ServerApi.ImageApi.GetDefault();
        }
        Image.prototype.CreateServerEntity = function () {
            return new ServerModel.Image();
        };
        return Image;
    }(Entity));
    ClientModel.Image = Image;
    var Tack = (function (_super) {
        __extends(Tack, _super);
        function Tack() {
            var _this = this;
            _super.apply(this, arguments);
            this.StartDate = ko.observable();
            this.EndDate = ko.observable();
            this.Start = ko.observable();
            this.End = ko.observable();
            this.Waypoints = ko.observableArray();
            this.Crew = ko.observableArray();
            this.Distance = ko.observable();
            this.ServerApi = ServerApi.TackApi.GetDefault();
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
        Tack.prototype.CreateServerEntity = function () {
            return new ServerModel.Tack();
        };
        return Tack;
    }(Entity));
    ClientModel.Tack = Tack;
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
            this.ServerApi = ServerApi.LocationApi.GetDefault();
        }
        Location.prototype.CreateServerEntity = function () {
            return new ServerModel.Location();
        };
        return Location;
    }(Entity));
    ClientModel.Location = Location;
    var Restaurant = (function (_super) {
        __extends(Restaurant, _super);
        function Restaurant() {
            _super.apply(this, arguments);
            this.ServerApi = ServerApi.RestaurantApi.GetDefault();
        }
        Restaurant.prototype.CreateServerEntity = function () {
            return new ServerModel.Restaurant();
        };
        return Restaurant;
    }(Location));
    ClientModel.Restaurant = Restaurant;
    var Supermarket = (function (_super) {
        __extends(Supermarket, _super);
        function Supermarket() {
            _super.apply(this, arguments);
            this.ServerApi = ServerApi.SupermarketApi.GetDefault();
        }
        Supermarket.prototype.CreateServerEntity = function () {
            return new ServerModel.Supermarket();
        };
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

//# sourceMappingURL=data:application/json;base64,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LElBQUE7SUF4QlksNEJBQWdCLG1CQXdCNUIsQ0FBQTtBQUVMLENBQUMsRUFwa0JNLFdBQVcsS0FBWCxXQUFXLFFBb2tCakI7QUFNRCxJQUFLLFVBS0o7QUFMRCxXQUFLLFVBQVU7SUFDWCxpREFBTyxDQUFBO0lBQ1AsNkNBQUssQ0FBQTtJQUNMLG1EQUFRLENBQUE7SUFDUiwrREFBYyxDQUFBO0FBQ2xCLENBQUMsRUFMSSxVQUFVLEtBQVYsVUFBVSxRQUtkIiwiZmlsZSI6IkNsaWVudE1vZGVsLmpzIiwic291cmNlc0NvbnRlbnQiOlsiXHJcblxyXG5tb2R1bGUgQ2xpZW50TW9kZWwge1xyXG5cclxuXHJcblxyXG4gICAgaW1wb3J0IFNFbnRpdHkgPSBTZXJ2ZXJNb2RlbC5FbnRpdHlcclxuICAgIGltcG9ydCBTV2F5cG9pbnQgPSBTZXJ2ZXJNb2RlbC5XYXlwb2ludFxyXG4gICAgaW1wb3J0IFNXYXlwb2ludENvbm5lY3Rpb24gPSBTZXJ2ZXJNb2RlbC5XYXlwb2ludENvbm5lY3Rpb25cclxuICAgIGltcG9ydCBTSGFyYm91ciA9IFNlcnZlck1vZGVsLkhhcmJvdXJcclxuICAgIGltcG9ydCBTUGVyc29uID0gU2VydmVyTW9kZWwuUGVyc29uXHJcbiAgICBpbXBvcnQgU0pvYiA9IFNlcnZlck1vZGVsLkpvYlxyXG4gICAgaW1wb3J0IFNUcmlwID0gU2VydmVyTW9kZWwuVHJpcFxyXG4gICAgaW1wb3J0IFNBZGRyZXNzID0gU2VydmVyTW9kZWwuQWRkcmVzc1xyXG4gICAgaW1wb3J0IFNJbWFnZSA9IFNlcnZlck1vZGVsLkltYWdlXHJcbiAgICBpbXBvcnQgU0FsYnVtID0gU2VydmVyTW9kZWwuQWxidW1cclxuICAgIGltcG9ydCBTV2F5cG9pbnRUYWNrID0gU2VydmVyTW9kZWwuV2F5cG9pbnRUYWNrXHJcbiAgICBpbXBvcnQgU1RhY2sgPSBTZXJ2ZXJNb2RlbC5UYWNrXHJcbiAgICBpbXBvcnQgU0xvY2F0aW9uID0gU2VydmVyTW9kZWwuTG9jYXRpb25cclxuICAgIGltcG9ydCBTUmVzdGF1cmFudCA9IFNlcnZlck1vZGVsLlJlc3RhdXJhbnRcclxuICAgIGltcG9ydCBTU3VwZXJtYXJrZXQgPSBTZXJ2ZXJNb2RlbC5TdXBlcm1hcmtldFxyXG4gICAgaW1wb3J0IFNDb21tZW50ID0gU2VydmVyTW9kZWwuQ29tbWVudDtcclxuICAgIGltcG9ydCBTQ29tbWVudExpc3QgPSBTZXJ2ZXJNb2RlbC5Db21tZW50TGlzdDtcclxuXHJcbiAgICBleHBvcnQgYWJzdHJhY3QgY2xhc3MgRW50aXR5PFQgZXh0ZW5kcyBTZXJ2ZXJNb2RlbC5FbnRpdHk+IHtcclxuXHJcbiAgICAgICAgSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBBbGJ1bUlkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgQ29tbWVudExpc3RJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIEFsYnVtID0ga28ub2JzZXJ2YWJsZTxBbGJ1bT4oKTtcclxuICAgICAgICBDb21tZW50TGlzdCA9IGtvLm9ic2VydmFibGU8Q29tbWVudExpc3Q+KCk7XHJcblxyXG5cclxuICAgICAgICBwcml2YXRlIHN0YXRpYyBDbGllbnRJZENvdW50ZXIgPSAwO1xyXG5cclxuICAgICAgICBwcml2YXRlIGNsaWVudElkID0rK0VudGl0eS5DbGllbnRJZENvdW50ZXI7XHJcblxyXG4gICAgICAgIHByb3RlY3RlZCBTZXJ2ZXJBcGk6IFNlcnZlckFwaS5BcGk8VD47XHJcblxyXG4gICAgICAgIERlbGV0ZU9uU2VydmVyKCk6IEpRdWVyeVByb21pc2U8VD4ge1xyXG4gICAgICAgICAgICByZXR1cm4gdGhpcy5TZXJ2ZXJBcGkuRGVsZXRlKHRoaXMuSWQoKSk7XHJcbiAgICAgICAgfTtcclxuXHJcbiAgICAgICAgU2F2ZVRvU2VydmVyKCk6IEpRdWVyeVByb21pc2U8VD4ge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5JZCgpID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gdGhpcy5TZXJ2ZXJBcGkuQ3JlYXRlKHRoaXMuQ29udmVydFRvU2VydmVyRW50aXR5KCkpXHJcbiAgICAgICAgICAgICAgICAgICAgLmRvbmUoZGF0YSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuc2F2ZWRTdGF0ZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5Mb2FkRnJvbVNlcnZlckVudGl0eShkYXRhKTtcclxuICAgICAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuU2VydmVyQXBpLlVwZGF0ZSh0aGlzLkNvbnZlcnRUb1NlcnZlckVudGl0eSgpKVxyXG4gICAgICAgICAgICAgICAgLmRvbmUoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuc2F2ZWRTdGF0ZSA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgICAgIH0pOztcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIExvYWRGcm9tU2VydmVyRW50aXR5KHNlcnZlckVudGl0eTogVCk6IHRoaXMge1xyXG4gICAgICAgICAgICBmb3IgKGxldCBwcm9wIG9mIHRoaXMuR2V0T2JzZXJ2YWJsZU5hbWVzKCkpIHtcclxuICAgICAgICAgICAgICAgIGNvbnN0IHNWYWwgPSBzZXJ2ZXJFbnRpdHlbcHJvcF07XHJcbiAgICAgICAgICAgICAgICBpZiAoc1ZhbCAhPT0gdW5kZWZpbmVkICYmIHNWYWwgIT09IG51bGwgJiYgIShzVmFsIGluc3RhbmNlb2YgQXJyYXkpKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgY29uc3QgY1ZhbCA9IHRoaXNbcHJvcF0oKTtcclxuICAgICAgICAgICAgICAgICAgICBpZiAoY1ZhbCBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICAgICAgY1ZhbC5Mb2FkRnJvbVNlcnZlckVudGl0eShzVmFsKTtcclxuICAgICAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXNbcHJvcF0oc1ZhbCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIHRoaXM7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBDb252ZXJ0VG9TZXJ2ZXJFbnRpdHkoaWRPbmx5OiBib29sZWFuID0gZmFsc2UpOiBUIHtcclxuICAgICAgICAgICAgY29uc3Qgc2VydmVyRW50aXR5ID0ge0NsaWVudElkOnRoaXMuY2xpZW50SWR9O1xyXG4gICAgICAgICAgICBjb25zdCBlbnRpdHkgPSB0aGlzO1xyXG5cclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcE5hbWUgb2YgdGhpcy5HZXRPYnNlcnZhYmxlTmFtZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgcHJvcCA9IGVudGl0eVtwcm9wTmFtZV07XHJcbiAgICAgICAgICAgICAgICBjb25zdCB2YWwgPSBwcm9wKCk7XHJcbiAgICAgICAgICAgICAgICBpZiAodmFsICE9PSB1bmRlZmluZWQgJiYgISh2YWwgaW5zdGFuY2VvZiBBcnJheSkpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAodmFsIGluc3RhbmNlb2YgQXJyYXkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29uc3QgYXJyID0gbmV3IEFycmF5PFQ+KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGZvciAobGV0IGVsZW0gb2YgdmFsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBhcnIucHVzaChlbGVtLkNvbnZlcnRUb1NlcnZlckVudGl0eSgpKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBzZXJ2ZXJFbnRpdHlbcHJvcE5hbWVdID0gYXJyO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHNlcnZlckVudGl0eVtwcm9wTmFtZV0gPSB2YWwgaW5zdGFuY2VvZiBFbnRpdHkgPyB2YWwuQ29udmVydFRvU2VydmVyRW50aXR5KCkgOiB2YWw7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIDxhbnk+c2VydmVyRW50aXR5O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29weVRvKGVudGl0eTogdGhpcykge1xyXG4gICAgICAgICAgICBlbnRpdHkuSWQodGhpcy5JZCgpKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGFic3RyYWN0IENyZWF0ZVNlcnZlckVudGl0eSgpOiBUO1xyXG5cclxuICAgICAgICBwcml2YXRlIHNhdmVkU3RhdGU6IGFueTtcclxuXHJcbiAgICAgICAgU2F2ZVN0YXRlKCkge1xyXG4gICAgICAgICAgICBjb25zdCBlbnRpdHkgPSB0aGlzO1xyXG4gICAgICAgICAgICBlbnRpdHkuc2F2ZWRTdGF0ZSA9IG5ldyBPYmplY3QoKTtcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcCBvZiB0aGlzLkdldE9ic2VydmFibGVOYW1lcygpKSB7XHJcbiAgICAgICAgICAgICAgICBsZXQgdmFsID0ga28udW53cmFwKGVudGl0eVtwcm9wXSk7XHJcbiAgICAgICAgICAgICAgICBpZiAodmFsIGluc3RhbmNlb2YgQXJyYXkpIHtcclxuICAgICAgICAgICAgICAgICAgICB2YWwgPSB2YWwuc2xpY2UoMCk7XHJcbiAgICAgICAgICAgICAgICAgICAgZm9yIChsZXQgZWxlbSBvZiB2YWwpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKGVsZW0gaW5zdGFuY2VvZiBFbnRpdHkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBlbGVtLlNhdmVTdGF0ZSgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH0gZWxzZSBpZiAodmFsIGluc3RhbmNlb2YgRW50aXR5KVxyXG4gICAgICAgICAgICAgICAgICAgIHZhbC5TYXZlU3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVudGl0eS5zYXZlZFN0YXRlW3Byb3BdID0gdmFsO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBwcm90ZWN0ZWQgR2V0T2JzZXJ2YWJsZU5hbWVzKCk6IHN0cmluZ1tdIHtcclxuICAgICAgICAgICAgY29uc3Qgb3V0ID0gbmV3IEFycmF5PHN0cmluZz4oKTtcclxuICAgICAgICAgICAgY29uc3QgZW50aXR5ID0gdGhpcztcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcCBpbiBlbnRpdHkpXHJcbiAgICAgICAgICAgICAgICBpZiAoZW50aXR5Lmhhc093blByb3BlcnR5KHByb3ApKVxyXG4gICAgICAgICAgICAgICAgICAgIGlmIChrby5pc1dyaXRlYWJsZU9ic2VydmFibGUoZW50aXR5W3Byb3BdKSAmJiAhZW50aXR5W3Byb3BdLkJsb2NrKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBvdXQucHVzaChwcm9wKTtcclxuICAgICAgICAgICAgcmV0dXJuIG91dDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJldmVydFN0YXRlKGlnbm9yZUVycm9yOiBib29sZWFuID0gZmFsc2UpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuc2F2ZWRTdGF0ZSA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgaWYgKGlnbm9yZUVycm9yKVxyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICB0aHJvdyBcIk5vIHNhdmVkIHN0YXRlXCI7XHJcbiAgICAgICAgICAgIGNvbnN0IGVudGl0eSA9IHRoaXM7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHByb3Agb2YgdGhpcy5HZXRPYnNlcnZhYmxlTmFtZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgdmFsID0gZW50aXR5LnNhdmVkU3RhdGVbcHJvcF07XHJcbiAgICAgICAgICAgICAgICAvL3RoaXMuc2F2ZWRTdGF0ZVtwcm9wXSA9IHZhbDtcclxuICAgICAgICAgICAgICAgIGVudGl0eVtwcm9wXSh2YWwpO1xyXG4gICAgICAgICAgICAgICAgaWYgKHZhbCBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICB2YWwuUmV2ZXJ0U3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVsc2UgaWYgKHZhbCBpbnN0YW5jZW9mIEFycmF5KVxyXG4gICAgICAgICAgICAgICAgICAgIGZvciAobGV0IGVsZW0gb2YgdmFsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmIChlbGVtIGluc3RhbmNlb2YgRW50aXR5KVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgZWxlbS5SZXZlcnRTdGF0ZSgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgQWxidW0gZXh0ZW5kcyBFbnRpdHk8U0FsYnVtPiB7XHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNBbGJ1bSB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU0FsYnVtKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBJbWFnZXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8SW1hZ2U+KCk7XHJcblxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuQWxidW1BcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBDb21tZW50TGlzdCBleHRlbmRzIEVudGl0eTxTQ29tbWVudExpc3Q+IHtcclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU0NvbW1lbnRMaXN0IHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTQ29tbWVudExpc3QoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5Db21tZW50TGlzdEFwaS5HZXREZWZhdWx0KCk7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFBlcnNvbiBleHRlbmRzIEVudGl0eTxTUGVyc29uPiB7XHJcblxyXG4gICAgICAgIENyZWF0ZVNlcnZlckVudGl0eSgpOiBTUGVyc29uIHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTUGVyc29uKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuUGVyc29uQXBpLkdldERlZmF1bHQoKTtcclxuICAgICAgICBMYXN0TmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIEZpcnN0TmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIEZ1bGxOYW1lID0ga28uY29tcHV0ZWQoKCkgPT4gdGhpcy5GaXJzdE5hbWUoKSArIFwiIFwiICsgdGhpcy5MYXN0TmFtZSgpKTtcclxuICAgICAgICBcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgSm9iIGV4dGVuZHMgRW50aXR5PFNKb2I+IHtcclxuICAgICAgICBEdWVUbyA9IGtvLm9ic2VydmFibGU8RGF0ZT4oKTtcclxuICAgICAgICBBc3NpZ25lZFRvID0ga28ub2JzZXJ2YWJsZTxQZXJzb24+KCk7XHJcbiAgICAgICAgQXNzaWduZWRUb0lkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgVGl0bGUgPSBrby5vYnNlcnZhYmxlPFN0cmluZz4oKTtcclxuICAgICAgICBDb250ZW50ID0ga28ub2JzZXJ2YWJsZTxTdHJpbmc+KCk7XHJcbiAgICAgICAgRG9uZSA9IGtvLm9ic2VydmFibGU8Ym9vbGVhbj4oKTtcclxuICAgICAgICBTdXBlckpvYiA9IGtvLm9ic2VydmFibGU8Sm9iPigpO1xyXG4gICAgICAgIFN1cGVySm9iSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBUcmlwID0ga28ub2JzZXJ2YWJsZTxUcmlwPigpO1xyXG4gICAgICAgIFRyaXBJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFN1YkpvYnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Sm9iPigpO1xyXG5cclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLkpvYiB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU2VydmVyTW9kZWwuSm9iKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuSm9iQXBpLkdldERlZmF1bHQoKTtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgV2F5cG9pbnQgZXh0ZW5kcyBFbnRpdHk8U1dheXBvaW50PiB7XHJcbiAgICAgICAgY29uc3RydWN0b3IobGF0TG5nOiBMLkxhdExuZywgbWFya2VyVHlwZTogTWFya2VyVHlwZSwgcHJvdGVjdGVkIE1hcDogTC5tYXBib3guTWFwKSB7XHJcbiAgICAgICAgICAgIHN1cGVyKCk7XHJcbiAgICAgICAgICAgIHRoaXMuTGF0aXR1ZGUobGF0TG5nLmxhdCk7XHJcbiAgICAgICAgICAgIHRoaXMuTG9uZ2l0dWRlKGxhdExuZy5sbmcpO1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZyA9IG5ldyBMLkxhdExuZyhsYXRMbmcubGF0LCBsYXRMbmcubG5nKTtcclxuICAgICAgICAgICAgdGhpcy5MYXRpdHVkZS5zdWJzY3JpYmUoKHZhbHVlKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAodGhpcy5MYXRMbmcubGF0ICE9PSB2YWx1ZSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuTGF0TG5nLmxhdCA9IHZhbHVlO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuUmVkcmF3KCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB0aGlzLkxvbmdpdHVkZS5zdWJzY3JpYmUoKHZhbHVlKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAodGhpcy5MYXRMbmcubG5nICE9PSB2YWx1ZSkge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuTGF0TG5nLmxuZyA9IHZhbHVlO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuUmVkcmF3KCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlclR5cGUgPSBtYXJrZXJUeXBlO1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5Qb2x5bGluZXMgPSBuZXcgQXJyYXkoKTtcclxuICAgICAgICAgICAgdGhpcy5MYXRMbmcuV2F5cG9pbnQgPSB0aGlzO1xyXG4gICAgICAgICAgICAvL3RoaXMuTGF0aXR1ZGUuc3Vic2NyaWJlKCh2YWx1ZSkgPT4ge1xyXG4gICAgICAgICAgICAvLyAgICBpZiAodGhpcy5MYXRMbmcubGF0ICE9PSB2YWx1ZSkge1xyXG4gICAgICAgICAgICAvLyAgICAgICAgdGhpcy5SZWRyYXcoKTtcclxuICAgICAgICAgICAgLy8gICAgfVxyXG4gICAgICAgICAgICAvL30pO1xyXG4gICAgICAgICAgICAvL3RoaXMuTG9uZ2l0dWRlLnN1YnNjcmliZSgodmFsdWUpID0+IHtcclxuICAgICAgICAgICAgLy8gICAgaWYgKHRoaXMuTGF0TG5nLmxuZyAhPT0gdmFsdWUpIHtcclxuICAgICAgICAgICAgLy8gICAgICAgIHRoaXMuUmVkcmF3KCk7XHJcbiAgICAgICAgICAgIC8vICAgIH1cclxuICAgICAgICAgICAgLy99KTtcclxuICAgICAgICAgICAgdGhpcy5QcmVjZXNzb3IuQmxvY2sgPSB0cnVlO1xyXG4gICAgICAgICAgICB0aGlzLkRpc3RhbmNlLkJsb2NrID0gdHJ1ZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFBvcHVwKGNvbnRlbnQ6IHN0cmluZyk7XHJcbiAgICAgICAgUG9wdXAoKTtcclxuICAgICAgICBQb3B1cChjb250ZW50Pzogc3RyaW5nKSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLnBvcHVwICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIHRoaXMuTWFwLnJlbW92ZUxheWVyKHRoaXMucG9wdXApO1xyXG4gICAgICAgICAgICAgICAgdGhpcy5wb3B1cCA9IHVuZGVmaW5lZDtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBpZiAoY29udGVudCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICAvL3RoaXMucG9wdXAgPSBMLnBvcHVwKCk7XHJcbiAgICAgICAgICAgICAgICB0aGlzLm1hcmtlci5iaW5kUG9wdXAoY29udGVudCk7IC8vLm9wZW5Qb3B1cCgpO1xyXG5cclxuXHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNob3coaGlnaGxpZ2h0OiBib29sZWFuID0gZmFsc2UpOiB2b2lkIHtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXIuc2V0T3BhY2l0eSh0aGlzLm1hcmtlci5XYXlwb2ludC5Jc0R1bW15KCkgPyAwLjUgOiAxKTtcclxuICAgICAgICAgICAgaWYgKGhpZ2hsaWdodClcclxuICAgICAgICAgICAgICAgICQodGhpcy5tYXJrZXIuX2ljb24pLmFkZENsYXNzKFwiZXhwb3NlXCIpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgSGlkZSgpOiB2b2lkIHtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXIuc2V0T3BhY2l0eSgwLjEpO1xyXG4gICAgICAgICAgICAkKHRoaXMubWFya2VyLl9pY29uKS5yZW1vdmVDbGFzcyhcImV4cG9zZVwiKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJlZHJhdyh1cGRhdGVQb2x5bGluZXMgPSB0cnVlKTogdm9pZCB7XHJcbiAgICAgICAgICAgIHRoaXMubWFya2VyLnNldExhdExuZyh0aGlzLkxhdExuZyk7XHJcbiAgICAgICAgICAgIGlmICh1cGRhdGVQb2x5bGluZXMpXHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBpID0gMDsgaSA8IHRoaXMucG9seWxpbmVzLmxlbmd0aDsgaSsrKVxyXG4gICAgICAgICAgICAgICAgICAgIHJlZHJhd1BvbHlsaW5lKHRoaXMucG9seWxpbmVzW2ldKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEdldENvbm5lY3RlZFdheXBvaW50cygpOiBXYXlwb2ludFtdIHtcclxuICAgICAgICAgICAgY29uc3QgcmV0ID0gbmV3IEFycmF5PFdheXBvaW50PigpO1xyXG4gICAgICAgICAgICBmb3IgKGxldCBsaW5lIG9mIHRoaXMucG9seWxpbmVzKSB7XHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCB3YXlwb2ludCBvZiBsaW5lLldheXBvaW50cykge1xyXG4gICAgICAgICAgICAgICAgICAgIGlmICh3YXlwb2ludCAhPT0gdGhpcylcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmV0LnB1c2god2F5cG9pbnQpO1xyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiByZXQuc29ydCgodzEsIHcyKSA9PiB7XHJcbiAgICAgICAgICAgICAgICByZXR1cm4gdzEuTGF0TG5nLmRpc3RhbmNlVG8odGhpcy5MYXRMbmcpIC0gdzIuTGF0TG5nLmRpc3RhbmNlVG8odGhpcy5MYXRMbmcpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGNvbnZlcnRGcm9tRHVtbXlIYW5kbGUoKSB7XHJcbiAgICAgICAgICAgIHRoaXMubWFya2VyLnNldE9wYWNpdHkoMSk7XHJcbiAgICAgICAgICAgIHZhciB3MSA9IHRoaXMucG9seWxpbmVzWzBdLldheXBvaW50c1swXTtcclxuICAgICAgICAgICAgdmFyIHcyID0gdGhpcy5wb2x5bGluZXNbMF0uV2F5cG9pbnRzWzFdO1xyXG4gICAgICAgICAgICBzcGxpdFBvbHlsaW5lKHRoaXMucG9seWxpbmVzWzBdKTtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXJUeXBlID0gTWFya2VyVHlwZS5XYXlwb2ludDtcclxuICAgICAgICAgICAgdGhpcy5TYXZlVG9TZXJ2ZXIoKVxyXG4gICAgICAgICAgICAgICAgLmRvbmUoKHcpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBjb25zdCB3Q0EgPSBTZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKTtcclxuICAgICAgICAgICAgICAgICAgICB3Q0EuRGlzY29ubmVjdCh3MS5JZCgpLCB3Mi5JZCgpKTtcclxuICAgICAgICAgICAgICAgICAgICB3Q0EuQ29ubmVjdCh3MS5JZCgpLCB3LklkKTtcclxuICAgICAgICAgICAgICAgICAgICB3Q0EuQ29ubmVjdCh3Mi5JZCgpLCB3LklkKTtcclxuICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgSXNJblBvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIGZvciAoY29uc3QgY3VycmVudFBvbHlsaW5lIG9mIHRoaXMucG9seWxpbmVzKSB7XHJcbiAgICAgICAgICAgICAgICBpZiAocG9seWxpbmUgPT09IGN1cnJlbnRQb2x5bGluZSlcclxuICAgICAgICAgICAgICAgICAgICByZXR1cm4gdHJ1ZTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBSZW1vdmVGcm9tTWFwKCkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5tYXJrZXJUeXBlICE9PSBNYXJrZXJUeXBlLkR1bW15KVxyXG4gICAgICAgICAgICAgICAgZm9yIChsZXQgcG9seWxpbmUgb2YgdGhpcy5wb2x5bGluZXMpXHJcbiAgICAgICAgICAgICAgICAgICAgcmVtb3ZlUG9seWxpbmUocG9seWxpbmUpO1xyXG4gICAgICAgICAgICB0aGlzLk1hcC5yZW1vdmVMYXllcih0aGlzLm1hcmtlcik7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBBZGRUb1BvbHlsaW5lKHBvbHlsaW5lOiBMLlBvbHlsaW5lKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLklzSW5Qb2x5bGluZShwb2x5bGluZSkpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgICAgIGlmIChwb2x5bGluZS5EdW1teUhhbmRsZSAhPT0gdGhpcykge1xyXG4gICAgICAgICAgICAgICAgcG9seWxpbmUuV2F5cG9pbnRzLnB1c2godGhpcyk7XHJcbiAgICAgICAgICAgICAgICBwb2x5bGluZS5hZGRMYXRMbmcodGhpcy5MYXRMbmcpO1xyXG4gICAgICAgICAgICAgICAgcG9seWxpbmUucmVkcmF3KCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgdGhpcy5MYXRMbmcuUG9seWxpbmVzLnB1c2gocG9seWxpbmUpO1xyXG4gICAgICAgICAgICB0aGlzLnBvbHlsaW5lcy5wdXNoKHBvbHlsaW5lKTtcclxuICAgICAgICAgICAgLy9TZXJ2ZXJBcGkuV2F5cG9pbnRDb25uZWN0aW9uQXBpLkdldERlZmF1bHQoKS5cclxuICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBSZW1vdmVGcm9tUG9seWxpbmUocG9seWxpbmU6IEwuUG9seWxpbmUpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgaWYgKCF0aGlzLklzSW5Qb2x5bGluZShwb2x5bGluZSkpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheShwb2x5bGluZS5XYXlwb2ludHMsIHRoaXMpO1xyXG4gICAgICAgICAgICByZW1vdmVGcm9tQXJyYXkodGhpcy5wb2x5bGluZXMsIHBvbHlsaW5lKTtcclxuICAgICAgICAgICAgcmVtb3ZlRnJvbUFycmF5KHRoaXMuTGF0TG5nLlBvbHlsaW5lcywgcG9seWxpbmUpO1xyXG4gICAgICAgICAgICByZW1vdmVGcm9tQXJyYXkocG9seWxpbmUuZ2V0TGF0TG5ncygpLCB0aGlzLkxhdExuZyk7XHJcbiAgICAgICAgICAgIHBvbHlsaW5lLnJlZHJhdygpO1xyXG4gICAgICAgICAgICByZXR1cm4gdHJ1ZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJlbW92ZUlmSGFzWmVyb09yT25lUG9seWxpbmVzKCk6IGJvb2xlYW4ge1xyXG4gICAgICAgICAgICByZXR1cm4gdHJ1ZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFdheXBvaW50TnVtYmVyID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgcHJpdmF0ZSBwb2x5bGluZXMgPSBuZXcgQXJyYXk8TC5Qb2x5bGluZT4oKTtcclxuXHJcblxyXG4gICAgICAgIFNldExhdExuZyhsYXRMbmc6IEwuTGF0TG5nLCB1cGRhdGVQb2x5bGluZXM9dHJ1ZSk6IHZvaWQge1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5sYXQgPSBsYXRMbmcubGF0O1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5sbmcgPSBsYXRMbmcubG5nO1xyXG4gICAgICAgICAgICB0aGlzLkxhdGl0dWRlKGxhdExuZy5sYXQpO1xyXG4gICAgICAgICAgICB0aGlzLkxvbmdpdHVkZShsYXRMbmcubG5nKTtcclxuICAgICAgICAgICAgdGhpcy5SZWRyYXcodXBkYXRlUG9seWxpbmVzKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIENlbnRlck9uTWFwKCkge1xyXG4gICAgICAgICAgICB0aGlzLk1hcC5zZXRWaWV3KHRoaXMuTGF0TG5nKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIElzRHVtbXkoKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLm1hcmtlclR5cGUgPT09IE1hcmtlclR5cGUuRHVtbXk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBMYXRpdHVkZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIExvbmdpdHVkZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIERpc3RhbmNlID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgUHJlY2Vzc29yID0ga28ub2JzZXJ2YWJsZTxXYXlwb2ludD4oKTtcclxuICAgICAgICBSb3V0ZURpc3RhbmNlID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgUm91dGVQcmVjZXNzb3IgPSBrby5vYnNlcnZhYmxlPFdheXBvaW50PigpO1xyXG4gICAgICAgIHByb3RlY3RlZCBwb3B1cDogTC5Qb3B1cDtcclxuICAgICAgICBMYXRMbmc6IEwuTGF0TG5nO1xyXG4gICAgICAgIHByb3RlY3RlZCBtYXJrZXJUeXBlOiBNYXJrZXJUeXBlO1xyXG4gICAgICAgIG1hcmtlcjogTC5NYXJrZXI7XHJcblxyXG4gICAgICAgIE5hbWUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBEZXNjcmlwdGlvbiA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU2VydmVyTW9kZWwuV2F5cG9pbnQge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLldheXBvaW50KCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuV2F5cG9pbnRBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBIYXJib3VyIGV4dGVuZHMgV2F5cG9pbnQge1xyXG5cclxuICAgICAgICBjb25zdHJ1Y3RvcihuYW1lOiBzdHJpbmcsIGxhdExuZzogTC5MYXRMbmcsIG1hcDogTC5tYXBib3guTWFwKSB7XHJcbiAgICAgICAgICAgIHN1cGVyKGxhdExuZywgTWFya2VyVHlwZS5IYXJib3VyLCBtYXApO1xyXG4gICAgICAgICAgICB0aGlzLk5hbWUobmFtZSk7XHJcbiAgICAgICAgICAgIC8vdGhpcy5EaXN0YW5jZS5zdWJzY3JpYmUoKGQpID0+IHtcclxuICAgICAgICAgICAgLy8gICAgY29uc3QgbGFiZWwgPSB0aGlzLm1hcmtlci5nZXRMYWJlbCgpO1xyXG4gICAgICAgICAgICAvLyAgICBpZiAoZCA+IDApIHtcclxuICAgICAgICAgICAgLy8gICAgICAgIGlmIChsYWJlbCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgdGhpcy5tYXJrZXIudXBkYXRlTGFiZWxDb250ZW50KGQudG9TdHJpbmcoKSArIFwiIGttXCIpO1xyXG4gICAgICAgICAgICAvLyAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgLy8gICAgICAgICAgICB0aGlzLm1hcmtlci5iaW5kTGFiZWwoZC50b1N0cmluZygpICsgXCIga21cIixcclxuICAgICAgICAgICAgLy8gICAgICAgICAgICAgICAgPGFueT57XHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgICAgICAgICBkaXJlY3Rpb246IFwiYXV0b1wiXHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgICAgIH0pO1xyXG4gICAgICAgICAgICAvLyAgICAgICAgfVxyXG4gICAgICAgICAgICAvLyAgICB9IGVsc2UgaWYgKGxhYmVsICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgLy8gICAgICAgIHRoaXMubWFya2VyLnVuYmluZExhYmVsKCk7XHJcbiAgICAgICAgICAgIC8vICAgIH1cclxuICAgICAgICAgICAgLy99KTtcclxuICAgICAgICAgICAgdGhpcy5OYW1lLnN1YnNjcmliZSgoZCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgbGFiZWwgPSB0aGlzLm1hcmtlci5nZXRMYWJlbCgpO1xyXG4gICAgICAgICAgICAgICAgaWYgKGxhYmVsICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLm1hcmtlci51cGRhdGVMYWJlbENvbnRlbnQoZCk7XHJcbiAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMubWFya2VyLmJpbmRMYWJlbChkLFxyXG4gICAgICAgICAgICAgICAgICAgICAgICB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBkaXJlY3Rpb246IFwiYXV0b1wiXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH0gYXMgYW55KTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBBbGJ1bSA9IGtvLm9ic2VydmFibGUobmV3IEFsYnVtKCkpO1xyXG4gICAgICAgIENvbW1lbnRMaXN0ID0ga28ub2JzZXJ2YWJsZShuZXcgQ29tbWVudExpc3QoKSk7XHJcblxyXG4gICAgICAgIFJlbW92ZUlmSGFzWmVyb09yT25lUG9seWxpbmVzKCk6IGJvb2xlYW4ge1xyXG4gICAgICAgICAgICByZXR1cm4gZmFsc2U7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU2VydmVyTW9kZWwuSGFyYm91ciB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU2VydmVyTW9kZWwuSGFyYm91cigpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2VydmVyQXBpID0gU2VydmVyQXBpLkhhcmJvdXJBcGkuR2V0RGVmYXVsdCgpO1xyXG5cclxuICAgICAgICBMb2NhdGlvbnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8TG9jYXRpb24+KCk7XHJcbiAgICAgICAgUmF0aW5nID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgQ29udGVudCA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIFdlYnNpdGUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgVHJpcCBleHRlbmRzIEVudGl0eTxTVHJpcD4ge1xyXG4gICAgICAgIE5hbWUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBTdGFydCA9IGtvLm9ic2VydmFibGU8RGF0ZT4oKTtcclxuICAgICAgICBFbmQgPSBrby5vYnNlcnZhYmxlPERhdGU+KCk7XHJcbiAgICAgICAgQ29udGVudCA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIFRhY2tzID0ga28ub2JzZXJ2YWJsZUFycmF5PFRhY2s+KCk7XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5UcmlwQXBpLkdldERlZmF1bHQoKTtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLlRyaXAge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLlRyaXAoKTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIEFkZHJlc3MgZXh0ZW5kcyBFbnRpdHk8U0FkZHJlc3M+IHtcclxuICAgICAgICBTdHJlZXQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBaaXAgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBUb3duID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgQ29tbWVudCA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU0FkZHJlc3Mge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNBZGRyZXNzO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2VydmVyQXBpID0gU2VydmVyQXBpLkFkZHJlc3NBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBJbWFnZSBleHRlbmRzIEVudGl0eTxTSW1hZ2U+IHtcclxuXHJcbiAgICAgICAgUGFyZW50QWxidW1JZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFBhdGggPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBIZWlnaHQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBXaWR0aCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFdpZHRoMiA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU2VydmVyTW9kZWwuSW1hZ2Uge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLkltYWdlKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuSW1hZ2VBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBUYWNrIGV4dGVuZHMgRW50aXR5PFNUYWNrPiB7XHJcbiAgICAgICAgU3RhcnREYXRlID0ga28ub2JzZXJ2YWJsZTxEYXRlPigpO1xyXG4gICAgICAgIEVuZERhdGUgPSBrby5vYnNlcnZhYmxlPERhdGU+KCk7XHJcbiAgICAgICAgU3RhcnQgPSBrby5vYnNlcnZhYmxlPEhhcmJvdXI+KCk7XHJcbiAgICAgICAgRW5kID0ga28ub2JzZXJ2YWJsZTxIYXJib3VyPigpO1xyXG4gICAgICAgIFdheXBvaW50cyA9IGtvLm9ic2VydmFibGVBcnJheTxTZXJ2ZXJNb2RlbC5XYXlwb2ludFRhY2s+KCk7XHJcbiAgICAgICAgQ3JldyA9IGtvLm9ic2VydmFibGVBcnJheTxQZXJzb24+KCk7XHJcbiAgICAgICAgRGlzdGFuY2UgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLlRhY2sge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLlRhY2soKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5UYWNrQXBpLkdldERlZmF1bHQoKTtcclxuXHJcblxyXG5cclxuICAgICAgICBDYW5SZW1vdmVUYWNrID0ga28uY29tcHV0ZWQoe1xyXG4gICAgICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgICAgICAgICAgY29uc3QgdGFja3MgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKCkuVGFja3M7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBpbmRleCA9IHRhY2tzLmluZGV4T2YodGhpcyk7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwcmV2VGFjayA9IHRhY2tzKClbaW5kZXggLSAxXTtcclxuICAgICAgICAgICAgICAgIGNvbnN0IG5leHRUYWNrID0gdGFja3MoKVtpbmRleCArIDFdO1xyXG4gICAgICAgICAgICAgICAgaWYgKHByZXZUYWNrICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKG5leHRUYWNrICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJldHVybiBwcmV2VGFjay5TdGFydCgpICE9PSBuZXh0VGFjay5TdGFydCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIG5leHRUYWNrICE9PSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgIH0sXHJcbiAgICAgICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBDb21wdXRlUGxhY2Vob2xkZXI9a28uY29tcHV0ZWQoKCk6c3RyaW5nPT4ge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5TdGFydERhdGUoKSAhPT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIG1vbWVudCh0aGlzLlN0YXJ0RGF0ZSgpKS5mb3JtYXQoXCJMXCIpO1xyXG4gICAgICAgICAgICByZXR1cm4gXCJcIjtcclxuICAgICAgICB9KTtcclxuXHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIExvY2F0aW9uIGV4dGVuZHMgRW50aXR5PFNMb2NhdGlvbj4ge1xyXG4gICAgICAgIEhhcmJvdXJJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFdlYnNpdGUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBOYW1lID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgUmF0aW5nID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgQWRkcmVzcyA9IGtvLm9ic2VydmFibGU8QWRkcmVzcz4oKTtcclxuICAgICAgICBBZGRyZXNzSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLkxvY2F0aW9uIHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTZXJ2ZXJNb2RlbC5Mb2NhdGlvbigpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2VydmVyQXBpID0gU2VydmVyQXBpLkxvY2F0aW9uQXBpLkdldERlZmF1bHQoKTtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgUmVzdGF1cmFudCBleHRlbmRzIExvY2F0aW9uIHtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLlJlc3RhdXJhbnQge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLlJlc3RhdXJhbnQoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5SZXN0YXVyYW50QXBpLkdldERlZmF1bHQoKTtcclxuICAgIH1cclxuXHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFN1cGVybWFya2V0IGV4dGVuZHMgTG9jYXRpb24ge1xyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU2VydmVyTW9kZWwuU3VwZXJtYXJrZXQge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLlN1cGVybWFya2V0KCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuU3VwZXJtYXJrZXRBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgV2F5cG9pbnREaXN0YW5jZSB7XHJcbiAgICAgICAgY29uc3RydWN0b3IocHVibGljIFByZWNlc3NvcjogV2F5cG9pbnQsXHJcbiAgICAgICAgICAgIHB1YmxpYyBXYXlwb2ludDogV2F5cG9pbnQsXHJcbiAgICAgICAgICAgIHB1YmxpYyBEaXN0YW5jZTogbnVtYmVyLFxyXG4gICAgICAgICAgICBjYWxjdWxhdGVyZFdheXBvaW50czogV2F5cG9pbnRbXSxcclxuICAgICAgICAgICAgY2FsY3VsYXRlUm91dGU6IGJvb2xlYW4pIHtcclxuICAgICAgICAgICAgdGhpcy5MYXRMbmcgPSBXYXlwb2ludC5MYXRMbmc7XHJcbiAgICAgICAgICAgIGNhbGN1bGF0ZXJkV2F5cG9pbnRzLnB1c2goV2F5cG9pbnQpO1xyXG4gICAgICAgICAgICBmb3IgKGxldCB3cCBvZiBXYXlwb2ludC5HZXRDb25uZWN0ZWRXYXlwb2ludHMoKSkge1xyXG4gICAgICAgICAgICAgICAgaWYgKGNhbGN1bGF0ZXJkV2F5cG9pbnRzLmluZGV4T2Yod3ApID09PSAtMSlcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLkNvbm5lY3RlZFdheVBvaW50cy5wdXNoKHdwKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICBpZiAoY2FsY3VsYXRlUm91dGUpIHtcclxuICAgICAgICAgICAgICAgIFdheXBvaW50LlJvdXRlUHJlY2Vzc29yKFByZWNlc3Nvcik7XHJcbiAgICAgICAgICAgICAgICBXYXlwb2ludC5Sb3V0ZURpc3RhbmNlKERpc3RhbmNlKTtcclxuICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgIFdheXBvaW50LlByZWNlc3NvcihQcmVjZXNzb3IpO1xyXG4gICAgICAgICAgICAgICAgV2F5cG9pbnQuRGlzdGFuY2UoRGlzdGFuY2UpO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBDb25uZWN0ZWRXYXlQb2ludHMgPSBuZXcgQXJyYXk8V2F5cG9pbnQ+KCk7XHJcbiAgICAgICAgQ29ubmVjdGVkRGlzdGFuY2VzID0gbmV3IEFycmF5PFdheXBvaW50RGlzdGFuY2U+KCk7XHJcbiAgICAgICAgTGF0TG5nOiBMLkxhdExuZztcclxuICAgIH1cclxuXHJcbn1cclxuXHJcbmludGVyZmFjZSBLbm9ja291dE9ic2VydmFibGU8VD4gZXh0ZW5kcyBLbm9ja291dFN1YnNjcmliYWJsZTxUPiwgS25vY2tvdXRPYnNlcnZhYmxlRnVuY3Rpb25zPFQ+IHtcclxuICAgIEJsb2NrOiBib29sZWFuO1xyXG59XHJcblxyXG5lbnVtIE1hcmtlclR5cGUge1xyXG4gICAgSGFyYm91cixcclxuICAgIER1bW15LFxyXG4gICAgV2F5cG9pbnQsXHJcbiAgICBXZWF0aGVyU3RhdGlvblxyXG59Il0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
