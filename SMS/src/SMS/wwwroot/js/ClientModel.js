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
            this.Album = CreateObservable({ AddTransferMode: TransferMode.Include, UpdateTransferMode: TransferMode.Include });
            this.CommentList = ko.observable();
            this.ClientId = ++Entity.ClientIdCounter;
            Entity.EntityDB[this.ClientId.toString()] = this;
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
                            var entity = Entity.EntityDB[obj.ClientId.toString()];
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
                if (val !== undefined) {
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
        Entity.EntityDB = {};
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
    var AlbumImage = (function () {
        function AlbumImage() {
        }
        return AlbumImage;
    }());
    ClientModel.AlbumImage = AlbumImage;
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
            this.Path = ko.observable();
            this.Height = ko.observable();
            this.Width = ko.observable();
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

//# sourceMappingURL=data:application/json;base64,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UUFBUSxDQUFDLHFCQUFxQixFQUFFLEVBQWhDLGNBQWdDLEVBQWhDLElBQWdDLENBQUM7Z0JBQTNDLElBQUksRUFBRSxTQUFBO2dCQUNQLEVBQUUsQ0FBQyxDQUFDLG9CQUFvQixDQUFDLE9BQU8sQ0FBQyxFQUFFLENBQUMsS0FBSyxDQUFDLENBQUMsQ0FBQztvQkFDeEMsSUFBSSxDQUFDLGtCQUFrQixDQUFDLElBQUksQ0FBQyxFQUFFLENBQUMsQ0FBQzthQUN4QztZQUNELEVBQUUsQ0FBQyxDQUFDLGNBQWMsQ0FBQyxDQUFDLENBQUM7Z0JBQ2pCLFFBQVEsQ0FBQyxjQUFjLENBQUMsU0FBUyxDQUFDLENBQUM7Z0JBQ25DLFFBQVEsQ0FBQyxhQUFhLENBQUMsUUFBUSxDQUFDLENBQUM7WUFDckMsQ0FBQztZQUFDLElBQUksQ0FBQyxDQUFDO2dCQUNKLFFBQVEsQ0FBQyxTQUFTLENBQUMsU0FBUyxDQUFDLENBQUM7Z0JBQzlCLFFBQVEsQ0FBQyxRQUFRLENBQUMsUUFBUSxDQUFDLENBQUM7WUFDaEMsQ0FBQztRQUNMLENBQUM7UUFLTCx1QkFBQztJQUFELENBeEJBLEFBd0JDLElBQUE7SUF4QlksNEJBQWdCLG1CQXdCNUIsQ0FBQTtBQUVMLENBQUMsRUF4bEJNLFdBQVcsS0FBWCxXQUFXLFFBd2xCakI7QUFNRCxJQUFLLFVBS0o7QUFMRCxXQUFLLFVBQVU7SUFDWCxpREFBTyxDQUFBO0lBQ1AsNkNBQUssQ0FBQTtJQUNMLG1EQUFRLENBQUE7SUFDUiwrREFBYyxDQUFBO0FBQ2xCLENBQUMsRUFMSSxVQUFVLEtBQVYsVUFBVSxRQUtkIiwiZmlsZSI6IkNsaWVudE1vZGVsLmpzIiwic291cmNlc0NvbnRlbnQiOlsiXHJcblxyXG5tb2R1bGUgQ2xpZW50TW9kZWwge1xyXG5cclxuXHJcblxyXG4gICAgaW1wb3J0IFNFbnRpdHkgPSBTZXJ2ZXJNb2RlbC5FbnRpdHlcclxuICAgIGltcG9ydCBTV2F5cG9pbnQgPSBTZXJ2ZXJNb2RlbC5XYXlwb2ludFxyXG4gICAgaW1wb3J0IFNXYXlwb2ludENvbm5lY3Rpb24gPSBTZXJ2ZXJNb2RlbC5XYXlwb2ludENvbm5lY3Rpb25cclxuICAgIGltcG9ydCBTSGFyYm91ciA9IFNlcnZlck1vZGVsLkhhcmJvdXJcclxuICAgIGltcG9ydCBTUGVyc29uID0gU2VydmVyTW9kZWwuUGVyc29uXHJcbiAgICBpbXBvcnQgU0pvYiA9IFNlcnZlck1vZGVsLkpvYlxyXG4gICAgaW1wb3J0IFNUcmlwID0gU2VydmVyTW9kZWwuVHJpcFxyXG4gICAgaW1wb3J0IFNBZGRyZXNzID0gU2VydmVyTW9kZWwuQWRkcmVzc1xyXG4gICAgaW1wb3J0IFNJbWFnZSA9IFNlcnZlck1vZGVsLkltYWdlXHJcbiAgICBpbXBvcnQgU0FsYnVtID0gU2VydmVyTW9kZWwuQWxidW1cclxuICAgIGltcG9ydCBTV2F5cG9pbnRUYWNrID0gU2VydmVyTW9kZWwuV2F5cG9pbnRUYWNrXHJcbiAgICBpbXBvcnQgU1RhY2sgPSBTZXJ2ZXJNb2RlbC5UYWNrXHJcbiAgICBpbXBvcnQgU0xvY2F0aW9uID0gU2VydmVyTW9kZWwuTG9jYXRpb25cclxuICAgIGltcG9ydCBTUmVzdGF1cmFudCA9IFNlcnZlck1vZGVsLlJlc3RhdXJhbnRcclxuICAgIGltcG9ydCBTU3VwZXJtYXJrZXQgPSBTZXJ2ZXJNb2RlbC5TdXBlcm1hcmtldFxyXG4gICAgaW1wb3J0IFNDb21tZW50ID0gU2VydmVyTW9kZWwuQ29tbWVudDtcclxuICAgIGltcG9ydCBTQ29tbWVudExpc3QgPSBTZXJ2ZXJNb2RlbC5Db21tZW50TGlzdDtcclxuXHJcbiAgICBleHBvcnQgaW50ZXJmYWNlIElFbnRpdHkge1xyXG4gICAgICAgIElkOiBLbm9ja291dE9ic2VydmFibGU8bnVtYmVyPjtcclxuICAgICAgICBDbGllbnRJZDogbnVtYmVyO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBhYnN0cmFjdCBjbGFzcyBFbnRpdHk8VCBleHRlbmRzIFNlcnZlck1vZGVsLkVudGl0eT4gaW1wbGVtZW50cyBJRW50aXR5IHtcclxuICAgICAgICBjb25zdHJ1Y3RvcigpIHtcclxuICAgICAgICAgICAgRW50aXR5LkVudGl0eURCW3RoaXMuQ2xpZW50SWQudG9TdHJpbmcoKV0gPSB0aGlzO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBBbGJ1bUlkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgQ29tbWVudExpc3RJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIEFsYnVtID0gQ3JlYXRlT2JzZXJ2YWJsZTxBbGJ1bT4oeyBBZGRUcmFuc2Zlck1vZGU6IFRyYW5zZmVyTW9kZS5JbmNsdWRlLCBVcGRhdGVUcmFuc2Zlck1vZGU6IFRyYW5zZmVyTW9kZS5JbmNsdWRlIH0pO1xyXG4gICAgICAgIENvbW1lbnRMaXN0ID0ga28ub2JzZXJ2YWJsZTxDb21tZW50TGlzdD4oKTtcclxuXHJcblxyXG5cclxuICAgICAgICBwcml2YXRlIHN0YXRpYyBDbGllbnRJZENvdW50ZXIgPSAwO1xyXG4gICAgICAgIHByaXZhdGUgc3RhdGljIEVudGl0eURCID0ge307XHJcblxyXG4gICAgICAgIENsaWVudElkID0gKytFbnRpdHkuQ2xpZW50SWRDb3VudGVyO1xyXG5cclxuICAgICAgICBwcm90ZWN0ZWQgU2VydmVyQXBpOiBTZXJ2ZXJBcGkuQXBpPFQ+O1xyXG5cclxuICAgICAgICBEZWxldGVPblNlcnZlcigpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuU2VydmVyQXBpLkRlbGV0ZSh0aGlzLklkKCkpO1xyXG4gICAgICAgIH07XHJcblxyXG4gICAgICAgIFNhdmVUb1NlcnZlcigpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuSWQoKSA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHRoaXMuU2VydmVyQXBpLkNyZWF0ZSh0aGlzLkNvbnZlcnRUb1NlcnZlckVudGl0eSgpKVxyXG4gICAgICAgICAgICAgICAgICAgIC5kb25lKGRhdGEgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLnNhdmVkU3RhdGUgPSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHRoaXMuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoZGF0YSk7XHJcbiAgICAgICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLlNlcnZlckFwaS5VcGRhdGUodGhpcy5Db252ZXJ0VG9TZXJ2ZXJFbnRpdHkoKSlcclxuICAgICAgICAgICAgICAgIC5kb25lKChkYXRhKSA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5zYXZlZFN0YXRlID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICAgICAgICAgIHRoaXMuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoZGF0YSk7XHJcbiAgICAgICAgICAgICAgICB9KTs7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBMb2FkRnJvbVNlcnZlckVudGl0eShzZXJ2ZXJFbnRpdHk6IFQpOiB0aGlzIHtcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcCBvZiB0aGlzLkdldE9ic2VydmFibGVOYW1lcygpKSB7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBzVmFsID0gc2VydmVyRW50aXR5W3Byb3BdO1xyXG4gICAgICAgICAgICAgICAgaWYgKHNWYWwgIT09IHVuZGVmaW5lZCAmJiBzVmFsICE9PSBudWxsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHNWYWwgaW5zdGFuY2VvZiBBcnJheSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBvYmogb2Ygc1ZhbCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgdmFyIGVudGl0eSA9IEVudGl0eS5FbnRpdHlEQltvYmouQ2xpZW50SWQudG9TdHJpbmcoKV07XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBpZiAoZW50aXR5ICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgZW50aXR5LkxvYWRGcm9tU2VydmVyRW50aXR5KG9iaik7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICB9IGVsc2Uge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBjb25zdCBjVmFsID0gdGhpc1twcm9wXSgpO1xyXG4gICAgICAgICAgICAgICAgICAgICAgICBpZiAoY1ZhbCBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIGNWYWwuTG9hZEZyb21TZXJ2ZXJFbnRpdHkoc1ZhbCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICAgICAgICAgIHRoaXNbcHJvcF0oc1ZhbCk7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29udmVydFRvU2VydmVyRW50aXR5KGlkT25seTogYm9vbGVhbiA9IGZhbHNlKTogVCB7XHJcbiAgICAgICAgICAgIGNvbnN0IHNlcnZlckVudGl0eSA9IHsgQ2xpZW50SWQ6IHRoaXMuQ2xpZW50SWQgfTtcclxuICAgICAgICAgICAgY29uc3QgZW50aXR5ID0gdGhpcztcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcE5hbWUgb2YgdGhpcy5HZXRPYnNlcnZhYmxlTmFtZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgcHJvcCA9IGVudGl0eVtwcm9wTmFtZV07XHJcbiAgICAgICAgICAgICAgICBjb25zdCB2YWwgPSBwcm9wKCk7XHJcbiAgICAgICAgICAgICAgICBpZiAodmFsICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgICAgICBpZiAodmFsIGluc3RhbmNlb2YgQXJyYXkpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgY29uc3QgYXJyID0gbmV3IEFycmF5PFQ+KCk7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGZvciAobGV0IGVsZW0gb2YgdmFsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBhcnIucHVzaChlbGVtLkNvbnZlcnRUb1NlcnZlckVudGl0eSgpKTtcclxuICAgICAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBzZXJ2ZXJFbnRpdHlbcHJvcE5hbWVdID0gYXJyO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHNlcnZlckVudGl0eVtwcm9wTmFtZV0gPSB2YWwgaW5zdGFuY2VvZiBFbnRpdHkgPyB2YWwuQ29udmVydFRvU2VydmVyRW50aXR5KCkgOiB2YWw7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIDxhbnk+c2VydmVyRW50aXR5O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29weVRvKGVudGl0eTogdGhpcykge1xyXG4gICAgICAgICAgICBlbnRpdHkuSWQodGhpcy5JZCgpKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIGFic3RyYWN0IENyZWF0ZVNlcnZlckVudGl0eSgpOiBUO1xyXG5cclxuICAgICAgICBwcml2YXRlIHNhdmVkU3RhdGU6IGFueTtcclxuXHJcbiAgICAgICAgU2F2ZVN0YXRlKCkge1xyXG4gICAgICAgICAgICBjb25zdCBlbnRpdHkgPSB0aGlzO1xyXG4gICAgICAgICAgICBlbnRpdHkuc2F2ZWRTdGF0ZSA9IG5ldyBPYmplY3QoKTtcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcCBvZiB0aGlzLkdldE9ic2VydmFibGVOYW1lcygpKSB7XHJcbiAgICAgICAgICAgICAgICBsZXQgdmFsID0ga28udW53cmFwKGVudGl0eVtwcm9wXSk7XHJcbiAgICAgICAgICAgICAgICBpZiAodmFsIGluc3RhbmNlb2YgQXJyYXkpIHtcclxuICAgICAgICAgICAgICAgICAgICB2YWwgPSB2YWwuc2xpY2UoMCk7XHJcbiAgICAgICAgICAgICAgICAgICAgZm9yIChsZXQgZWxlbSBvZiB2YWwpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgaWYgKGVsZW0gaW5zdGFuY2VvZiBFbnRpdHkpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgICAgICBlbGVtLlNhdmVTdGF0ZSgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgICAgIH0gZWxzZSBpZiAodmFsIGluc3RhbmNlb2YgRW50aXR5KVxyXG4gICAgICAgICAgICAgICAgICAgIHZhbC5TYXZlU3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVudGl0eS5zYXZlZFN0YXRlW3Byb3BdID0gdmFsO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBwcm90ZWN0ZWQgR2V0T2JzZXJ2YWJsZU5hbWVzKCk6IHN0cmluZ1tdIHtcclxuICAgICAgICAgICAgY29uc3Qgb3V0ID0gbmV3IEFycmF5PHN0cmluZz4oKTtcclxuICAgICAgICAgICAgY29uc3QgZW50aXR5ID0gdGhpcztcclxuICAgICAgICAgICAgZm9yIChsZXQgcHJvcCBpbiBlbnRpdHkpXHJcbiAgICAgICAgICAgICAgICBpZiAoZW50aXR5Lmhhc093blByb3BlcnR5KHByb3ApKVxyXG4gICAgICAgICAgICAgICAgICAgIGlmIChrby5pc1dyaXRlYWJsZU9ic2VydmFibGUoZW50aXR5W3Byb3BdKSAmJiAhZW50aXR5W3Byb3BdLkJsb2NrKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICBvdXQucHVzaChwcm9wKTtcclxuICAgICAgICAgICAgcmV0dXJuIG91dDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJldmVydFN0YXRlKGlnbm9yZUVycm9yOiBib29sZWFuID0gZmFsc2UpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuc2F2ZWRTdGF0ZSA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgaWYgKGlnbm9yZUVycm9yKVxyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybjtcclxuICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICB0aHJvdyBcIk5vIHNhdmVkIHN0YXRlXCI7XHJcbiAgICAgICAgICAgIGNvbnN0IGVudGl0eSA9IHRoaXM7XHJcbiAgICAgICAgICAgIGZvciAobGV0IHByb3Agb2YgdGhpcy5HZXRPYnNlcnZhYmxlTmFtZXMoKSkge1xyXG4gICAgICAgICAgICAgICAgY29uc3QgdmFsID0gZW50aXR5LnNhdmVkU3RhdGVbcHJvcF07XHJcbiAgICAgICAgICAgICAgICAvL3RoaXMuc2F2ZWRTdGF0ZVtwcm9wXSA9IHZhbDtcclxuICAgICAgICAgICAgICAgIGVudGl0eVtwcm9wXSh2YWwpO1xyXG4gICAgICAgICAgICAgICAgaWYgKHZhbCBpbnN0YW5jZW9mIEVudGl0eSlcclxuICAgICAgICAgICAgICAgICAgICB2YWwuUmV2ZXJ0U3RhdGUoKTtcclxuICAgICAgICAgICAgICAgIGVsc2UgaWYgKHZhbCBpbnN0YW5jZW9mIEFycmF5KVxyXG4gICAgICAgICAgICAgICAgICAgIGZvciAobGV0IGVsZW0gb2YgdmFsKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgICAgIGlmIChlbGVtIGluc3RhbmNlb2YgRW50aXR5KVxyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgZWxlbS5SZXZlcnRTdGF0ZSgpO1xyXG4gICAgICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgQWxidW0gZXh0ZW5kcyBFbnRpdHk8U0FsYnVtPiB7XHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNBbGJ1bSB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU0FsYnVtKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBJbWFnZXMgPSBrby5vYnNlcnZhYmxlQXJyYXk8SW1hZ2U+KCk7XHJcblxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuQWxidW1BcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBbGJ1bUltYWdlIHtcclxuICAgICAgICBcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgQ29tbWVudExpc3QgZXh0ZW5kcyBFbnRpdHk8U0NvbW1lbnRMaXN0PiB7XHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNDb21tZW50TGlzdCB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU0NvbW1lbnRMaXN0KCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuQ29tbWVudExpc3RBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBQZXJzb24gZXh0ZW5kcyBFbnRpdHk8U1BlcnNvbj4ge1xyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU1BlcnNvbiB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU1BlcnNvbigpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2VydmVyQXBpID0gU2VydmVyQXBpLlBlcnNvbkFwaS5HZXREZWZhdWx0KCk7XHJcbiAgICAgICAgTGFzdE5hbWUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBGaXJzdE5hbWUgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBGdWxsTmFtZSA9IGtvLmNvbXB1dGVkKCgpID0+IHRoaXMuRmlyc3ROYW1lKCkgKyBcIiBcIiArIHRoaXMuTGFzdE5hbWUoKSk7XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBKb2IgZXh0ZW5kcyBFbnRpdHk8U0pvYj4ge1xyXG4gICAgICAgIER1ZVRvID0ga28ub2JzZXJ2YWJsZTxEYXRlPigpO1xyXG4gICAgICAgIEFzc2lnbmVkVG8gPSBrby5vYnNlcnZhYmxlPFBlcnNvbj4oKTtcclxuICAgICAgICBBc3NpZ25lZFRvSWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBUaXRsZSA9IGtvLm9ic2VydmFibGU8U3RyaW5nPigpO1xyXG4gICAgICAgIENvbnRlbnQgPSBrby5vYnNlcnZhYmxlPFN0cmluZz4oKTtcclxuICAgICAgICBEb25lID0ga28ub2JzZXJ2YWJsZTxib29sZWFuPigpO1xyXG4gICAgICAgIFN1cGVySm9iID0ga28ub2JzZXJ2YWJsZTxKb2I+KCk7XHJcbiAgICAgICAgU3VwZXJKb2JJZCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFRyaXAgPSBrby5vYnNlcnZhYmxlPFRyaXA+KCk7XHJcbiAgICAgICAgVHJpcElkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcbiAgICAgICAgU3ViSm9icyA9IGtvLm9ic2VydmFibGVBcnJheTxKb2I+KCk7XHJcblxyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU2VydmVyTW9kZWwuSm9iIHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTZXJ2ZXJNb2RlbC5Kb2IoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5Kb2JBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBXYXlwb2ludCBleHRlbmRzIEVudGl0eTxTV2F5cG9pbnQ+IHtcclxuICAgICAgICBjb25zdHJ1Y3RvcihsYXRMbmc6IEwuTGF0TG5nLCBtYXJrZXJUeXBlOiBNYXJrZXJUeXBlLCBwcm90ZWN0ZWQgTWFwOiBMLm1hcGJveC5NYXApIHtcclxuICAgICAgICAgICAgc3VwZXIoKTtcclxuICAgICAgICAgICAgdGhpcy5MYXRpdHVkZShsYXRMbmcubGF0KTtcclxuICAgICAgICAgICAgdGhpcy5Mb25naXR1ZGUobGF0TG5nLmxuZyk7XHJcbiAgICAgICAgICAgIHRoaXMuTGF0TG5nID0gbmV3IEwuTGF0TG5nKGxhdExuZy5sYXQsIGxhdExuZy5sbmcpO1xyXG4gICAgICAgICAgICB0aGlzLkxhdGl0dWRlLnN1YnNjcmliZSgodmFsdWUpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkxhdExuZy5sYXQgIT09IHZhbHVlKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5MYXRMbmcubGF0ID0gdmFsdWU7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5SZWRyYXcoKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIHRoaXMuTG9uZ2l0dWRlLnN1YnNjcmliZSgodmFsdWUpID0+IHtcclxuICAgICAgICAgICAgICAgIGlmICh0aGlzLkxhdExuZy5sbmcgIT09IHZhbHVlKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5MYXRMbmcubG5nID0gdmFsdWU7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5SZWRyYXcoKTtcclxuICAgICAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIHRoaXMubWFya2VyVHlwZSA9IG1hcmtlclR5cGU7XHJcbiAgICAgICAgICAgIHRoaXMuTGF0TG5nLlBvbHlsaW5lcyA9IG5ldyBBcnJheSgpO1xyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5XYXlwb2ludCA9IHRoaXM7XHJcbiAgICAgICAgICAgIC8vdGhpcy5MYXRpdHVkZS5zdWJzY3JpYmUoKHZhbHVlKSA9PiB7XHJcbiAgICAgICAgICAgIC8vICAgIGlmICh0aGlzLkxhdExuZy5sYXQgIT09IHZhbHVlKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICB0aGlzLlJlZHJhdygpO1xyXG4gICAgICAgICAgICAvLyAgICB9XHJcbiAgICAgICAgICAgIC8vfSk7XHJcbiAgICAgICAgICAgIC8vdGhpcy5Mb25naXR1ZGUuc3Vic2NyaWJlKCh2YWx1ZSkgPT4ge1xyXG4gICAgICAgICAgICAvLyAgICBpZiAodGhpcy5MYXRMbmcubG5nICE9PSB2YWx1ZSkge1xyXG4gICAgICAgICAgICAvLyAgICAgICAgdGhpcy5SZWRyYXcoKTtcclxuICAgICAgICAgICAgLy8gICAgfVxyXG4gICAgICAgICAgICAvL30pO1xyXG4gICAgICAgICAgICB0aGlzLlByZWNlc3Nvci5CbG9jayA9IHRydWU7XHJcbiAgICAgICAgICAgIHRoaXMuRGlzdGFuY2UuQmxvY2sgPSB0cnVlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgUG9wdXAoY29udGVudDogc3RyaW5nKTtcclxuICAgICAgICBQb3B1cCgpO1xyXG4gICAgICAgIFBvcHVwKGNvbnRlbnQ/OiBzdHJpbmcpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMucG9wdXAgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAgICAgdGhpcy5NYXAucmVtb3ZlTGF5ZXIodGhpcy5wb3B1cCk7XHJcbiAgICAgICAgICAgICAgICB0aGlzLnBvcHVwID0gdW5kZWZpbmVkO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIGlmIChjb250ZW50ICE9PSB1bmRlZmluZWQpIHtcclxuICAgICAgICAgICAgICAgIC8vdGhpcy5wb3B1cCA9IEwucG9wdXAoKTtcclxuICAgICAgICAgICAgICAgIHRoaXMubWFya2VyLmJpbmRQb3B1cChjb250ZW50KTsgLy8ub3BlblBvcHVwKCk7XHJcblxyXG5cclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2hvdyhoaWdobGlnaHQ6IGJvb2xlYW4gPSBmYWxzZSk6IHZvaWQge1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlci5zZXRPcGFjaXR5KHRoaXMubWFya2VyLldheXBvaW50LklzRHVtbXkoKSA/IDAuNSA6IDEpO1xyXG4gICAgICAgICAgICBpZiAoaGlnaGxpZ2h0KVxyXG4gICAgICAgICAgICAgICAgJCh0aGlzLm1hcmtlci5faWNvbikuYWRkQ2xhc3MoXCJleHBvc2VcIik7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBIaWRlKCk6IHZvaWQge1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlci5zZXRPcGFjaXR5KDAuMSk7XHJcbiAgICAgICAgICAgICQodGhpcy5tYXJrZXIuX2ljb24pLnJlbW92ZUNsYXNzKFwiZXhwb3NlXCIpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgUmVkcmF3KHVwZGF0ZVBvbHlsaW5lcyA9IHRydWUpOiB2b2lkIHtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXIuc2V0TGF0TG5nKHRoaXMuTGF0TG5nKTtcclxuICAgICAgICAgICAgaWYgKHVwZGF0ZVBvbHlsaW5lcylcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IGkgPSAwOyBpIDwgdGhpcy5wb2x5bGluZXMubGVuZ3RoOyBpKyspXHJcbiAgICAgICAgICAgICAgICAgICAgcmVkcmF3UG9seWxpbmUodGhpcy5wb2x5bGluZXNbaV0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgR2V0Q29ubmVjdGVkV2F5cG9pbnRzKCk6IFdheXBvaW50W10ge1xyXG4gICAgICAgICAgICBjb25zdCByZXQgPSBuZXcgQXJyYXk8V2F5cG9pbnQ+KCk7XHJcbiAgICAgICAgICAgIGZvciAobGV0IGxpbmUgb2YgdGhpcy5wb2x5bGluZXMpIHtcclxuICAgICAgICAgICAgICAgIGZvciAobGV0IHdheXBvaW50IG9mIGxpbmUuV2F5cG9pbnRzKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKHdheXBvaW50ICE9PSB0aGlzKVxyXG4gICAgICAgICAgICAgICAgICAgICAgICByZXQucHVzaCh3YXlwb2ludCk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgcmV0dXJuIHJldC5zb3J0KCh3MSwgdzIpID0+IHtcclxuICAgICAgICAgICAgICAgIHJldHVybiB3MS5MYXRMbmcuZGlzdGFuY2VUbyh0aGlzLkxhdExuZykgLSB3Mi5MYXRMbmcuZGlzdGFuY2VUbyh0aGlzLkxhdExuZyk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgY29udmVydEZyb21EdW1teUhhbmRsZSgpIHtcclxuICAgICAgICAgICAgdGhpcy5tYXJrZXIuc2V0T3BhY2l0eSgxKTtcclxuICAgICAgICAgICAgdmFyIHcxID0gdGhpcy5wb2x5bGluZXNbMF0uV2F5cG9pbnRzWzBdO1xyXG4gICAgICAgICAgICB2YXIgdzIgPSB0aGlzLnBvbHlsaW5lc1swXS5XYXlwb2ludHNbMV07XHJcbiAgICAgICAgICAgIHNwbGl0UG9seWxpbmUodGhpcy5wb2x5bGluZXNbMF0pO1xyXG4gICAgICAgICAgICB0aGlzLm1hcmtlclR5cGUgPSBNYXJrZXJUeXBlLldheXBvaW50O1xyXG4gICAgICAgICAgICB0aGlzLlNhdmVUb1NlcnZlcigpXHJcbiAgICAgICAgICAgICAgICAuZG9uZSgodykgPT4ge1xyXG4gICAgICAgICAgICAgICAgICAgIGNvbnN0IHdDQSA9IFNlcnZlckFwaS5XYXlwb2ludENvbm5lY3Rpb25BcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIHdDQS5EaXNjb25uZWN0KHcxLklkKCksIHcyLklkKCkpO1xyXG4gICAgICAgICAgICAgICAgICAgIHdDQS5Db25uZWN0KHcxLklkKCksIHcuSWQpO1xyXG4gICAgICAgICAgICAgICAgICAgIHdDQS5Db25uZWN0KHcyLklkKCksIHcuSWQpO1xyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBJc0luUG9seWxpbmUocG9seWxpbmU6IEwuUG9seWxpbmUpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgZm9yIChjb25zdCBjdXJyZW50UG9seWxpbmUgb2YgdGhpcy5wb2x5bGluZXMpIHtcclxuICAgICAgICAgICAgICAgIGlmIChwb2x5bGluZSA9PT0gY3VycmVudFBvbHlsaW5lKVxyXG4gICAgICAgICAgICAgICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJlbW92ZUZyb21NYXAoKSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLm1hcmtlclR5cGUgIT09IE1hcmtlclR5cGUuRHVtbXkpXHJcbiAgICAgICAgICAgICAgICBmb3IgKGxldCBwb2x5bGluZSBvZiB0aGlzLnBvbHlsaW5lcylcclxuICAgICAgICAgICAgICAgICAgICByZW1vdmVQb2x5bGluZShwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIHRoaXMuTWFwLnJlbW92ZUxheWVyKHRoaXMubWFya2VyKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEFkZFRvUG9seWxpbmUocG9seWxpbmU6IEwuUG9seWxpbmUpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuSXNJblBvbHlsaW5lKHBvbHlsaW5lKSlcclxuICAgICAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICAgICAgaWYgKHBvbHlsaW5lLkR1bW15SGFuZGxlICE9PSB0aGlzKSB7XHJcbiAgICAgICAgICAgICAgICBwb2x5bGluZS5XYXlwb2ludHMucHVzaCh0aGlzKTtcclxuICAgICAgICAgICAgICAgIHBvbHlsaW5lLmFkZExhdExuZyh0aGlzLkxhdExuZyk7XHJcbiAgICAgICAgICAgICAgICBwb2x5bGluZS5yZWRyYXcoKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICB0aGlzLkxhdExuZy5Qb2x5bGluZXMucHVzaChwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIHRoaXMucG9seWxpbmVzLnB1c2gocG9seWxpbmUpO1xyXG4gICAgICAgICAgICAvL1NlcnZlckFwaS5XYXlwb2ludENvbm5lY3Rpb25BcGkuR2V0RGVmYXVsdCgpLlxyXG4gICAgICAgICAgICByZXR1cm4gdHJ1ZTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFJlbW92ZUZyb21Qb2x5bGluZShwb2x5bGluZTogTC5Qb2x5bGluZSk6IGJvb2xlYW4ge1xyXG4gICAgICAgICAgICBpZiAoIXRoaXMuSXNJblBvbHlsaW5lKHBvbHlsaW5lKSlcclxuICAgICAgICAgICAgICAgIHJldHVybiBmYWxzZTtcclxuICAgICAgICAgICAgcmVtb3ZlRnJvbUFycmF5KHBvbHlsaW5lLldheXBvaW50cywgdGhpcyk7XHJcbiAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheSh0aGlzLnBvbHlsaW5lcywgcG9seWxpbmUpO1xyXG4gICAgICAgICAgICByZW1vdmVGcm9tQXJyYXkodGhpcy5MYXRMbmcuUG9seWxpbmVzLCBwb2x5bGluZSk7XHJcbiAgICAgICAgICAgIHJlbW92ZUZyb21BcnJheShwb2x5bGluZS5nZXRMYXRMbmdzKCksIHRoaXMuTGF0TG5nKTtcclxuICAgICAgICAgICAgcG9seWxpbmUucmVkcmF3KCk7XHJcbiAgICAgICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgUmVtb3ZlSWZIYXNaZXJvT3JPbmVQb2x5bGluZXMoKTogYm9vbGVhbiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0cnVlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgV2F5cG9pbnROdW1iZXIgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBwcml2YXRlIHBvbHlsaW5lcyA9IG5ldyBBcnJheTxMLlBvbHlsaW5lPigpO1xyXG5cclxuXHJcbiAgICAgICAgU2V0TGF0TG5nKGxhdExuZzogTC5MYXRMbmcsIHVwZGF0ZVBvbHlsaW5lcyA9IHRydWUpOiB2b2lkIHtcclxuICAgICAgICAgICAgdGhpcy5MYXRMbmcubGF0ID0gbGF0TG5nLmxhdDtcclxuICAgICAgICAgICAgdGhpcy5MYXRMbmcubG5nID0gbGF0TG5nLmxuZztcclxuICAgICAgICAgICAgdGhpcy5MYXRpdHVkZShsYXRMbmcubGF0KTtcclxuICAgICAgICAgICAgdGhpcy5Mb25naXR1ZGUobGF0TG5nLmxuZyk7XHJcbiAgICAgICAgICAgIHRoaXMuUmVkcmF3KHVwZGF0ZVBvbHlsaW5lcyk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBDZW50ZXJPbk1hcCgpIHtcclxuICAgICAgICAgICAgdGhpcy5NYXAuc2V0Vmlldyh0aGlzLkxhdExuZyk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBJc0R1bW15KCk6IGJvb2xlYW4ge1xyXG4gICAgICAgICAgICByZXR1cm4gdGhpcy5tYXJrZXJUeXBlID09PSBNYXJrZXJUeXBlLkR1bW15O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgTGF0aXR1ZGUgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBMb25naXR1ZGUgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBEaXN0YW5jZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFByZWNlc3NvciA9IGtvLm9ic2VydmFibGU8V2F5cG9pbnQ+KCk7XHJcbiAgICAgICAgUm91dGVEaXN0YW5jZSA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIFJvdXRlUHJlY2Vzc29yID0ga28ub2JzZXJ2YWJsZTxXYXlwb2ludD4oKTtcclxuICAgICAgICBwcm90ZWN0ZWQgcG9wdXA6IEwuUG9wdXA7XHJcbiAgICAgICAgTGF0TG5nOiBMLkxhdExuZztcclxuICAgICAgICBwcm90ZWN0ZWQgbWFya2VyVHlwZTogTWFya2VyVHlwZTtcclxuICAgICAgICBtYXJrZXI6IEwuTWFya2VyO1xyXG5cclxuICAgICAgICBOYW1lID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgRGVzY3JpcHRpb24gPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLldheXBvaW50IHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTZXJ2ZXJNb2RlbC5XYXlwb2ludCgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2VydmVyQXBpID0gU2VydmVyQXBpLldheXBvaW50QXBpLkdldERlZmF1bHQoKTtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgSGFyYm91ciBleHRlbmRzIFdheXBvaW50IHtcclxuXHJcbiAgICAgICAgY29uc3RydWN0b3IobmFtZTogc3RyaW5nLCBsYXRMbmc6IEwuTGF0TG5nLCBtYXA6IEwubWFwYm94Lk1hcCkge1xyXG4gICAgICAgICAgICBzdXBlcihsYXRMbmcsIE1hcmtlclR5cGUuSGFyYm91ciwgbWFwKTtcclxuICAgICAgICAgICAgdGhpcy5OYW1lKG5hbWUpO1xyXG4gICAgICAgICAgICAvL3RoaXMuRGlzdGFuY2Uuc3Vic2NyaWJlKChkKSA9PiB7XHJcbiAgICAgICAgICAgIC8vICAgIGNvbnN0IGxhYmVsID0gdGhpcy5tYXJrZXIuZ2V0TGFiZWwoKTtcclxuICAgICAgICAgICAgLy8gICAgaWYgKGQgPiAwKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICBpZiAobGFiZWwgIT09IHVuZGVmaW5lZCkge1xyXG4gICAgICAgICAgICAvLyAgICAgICAgICAgIHRoaXMubWFya2VyLnVwZGF0ZUxhYmVsQ29udGVudChkLnRvU3RyaW5nKCkgKyBcIiBrbVwiKTtcclxuICAgICAgICAgICAgLy8gICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgdGhpcy5tYXJrZXIuYmluZExhYmVsKGQudG9TdHJpbmcoKSArIFwiIGttXCIsXHJcbiAgICAgICAgICAgIC8vICAgICAgICAgICAgICAgIDxhbnk+e1xyXG4gICAgICAgICAgICAvLyAgICAgICAgICAgICAgICAgICAgZGlyZWN0aW9uOiBcImF1dG9cIlxyXG4gICAgICAgICAgICAvLyAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgLy8gICAgICAgIH1cclxuICAgICAgICAgICAgLy8gICAgfSBlbHNlIGlmIChsYWJlbCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgIC8vICAgICAgICB0aGlzLm1hcmtlci51bmJpbmRMYWJlbCgpO1xyXG4gICAgICAgICAgICAvLyAgICB9XHJcbiAgICAgICAgICAgIC8vfSk7XHJcbiAgICAgICAgICAgIHRoaXMuTmFtZS5zdWJzY3JpYmUoKGQpID0+IHtcclxuICAgICAgICAgICAgICAgIGNvbnN0IGxhYmVsID0gdGhpcy5tYXJrZXIuZ2V0TGFiZWwoKTtcclxuICAgICAgICAgICAgICAgIGlmIChsYWJlbCAhPT0gdW5kZWZpbmVkKSB7XHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5tYXJrZXIudXBkYXRlTGFiZWxDb250ZW50KGQpO1xyXG4gICAgICAgICAgICAgICAgfSBlbHNlIHtcclxuICAgICAgICAgICAgICAgICAgICB0aGlzLm1hcmtlci5iaW5kTGFiZWwoZCxcclxuICAgICAgICAgICAgICAgICAgICAgICAge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICAgICAgZGlyZWN0aW9uOiBcImF1dG9cIlxyXG4gICAgICAgICAgICAgICAgICAgICAgICB9IGFzIGFueSk7XHJcbiAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQWxidW0gPSBrby5vYnNlcnZhYmxlKG5ldyBBbGJ1bSgpKTtcclxuICAgICAgICBDb21tZW50TGlzdCA9IGtvLm9ic2VydmFibGUobmV3IENvbW1lbnRMaXN0KCkpO1xyXG5cclxuICAgICAgICBSZW1vdmVJZkhhc1plcm9Pck9uZVBvbHlsaW5lcygpOiBib29sZWFuIHtcclxuICAgICAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLkhhcmJvdXIge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLkhhcmJvdXIoKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5IYXJib3VyQXBpLkdldERlZmF1bHQoKTtcclxuXHJcbiAgICAgICAgTG9jYXRpb25zID0ga28ub2JzZXJ2YWJsZUFycmF5PExvY2F0aW9uPigpO1xyXG4gICAgICAgIFJhdGluZyA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIENvbnRlbnQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBXZWJzaXRlID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFRyaXAgZXh0ZW5kcyBFbnRpdHk8U1RyaXA+IHtcclxuICAgICAgICBOYW1lID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgU3RhcnQgPSBrby5vYnNlcnZhYmxlPERhdGU+KCk7XHJcbiAgICAgICAgRW5kID0ga28ub2JzZXJ2YWJsZTxEYXRlPigpO1xyXG4gICAgICAgIENvbnRlbnQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBUYWNrcyA9IGtvLm9ic2VydmFibGVBcnJheTxUYWNrPigpO1xyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuVHJpcEFwaS5HZXREZWZhdWx0KCk7XHJcblxyXG4gICAgICAgIENyZWF0ZVNlcnZlckVudGl0eSgpOiBTZXJ2ZXJNb2RlbC5UcmlwIHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTZXJ2ZXJNb2RlbC5UcmlwKCk7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBZGRyZXNzIGV4dGVuZHMgRW50aXR5PFNBZGRyZXNzPiB7XHJcbiAgICAgICAgU3RyZWV0ID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgWmlwID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgVG93biA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIENvbW1lbnQgPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNBZGRyZXNzIHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTQWRkcmVzcztcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5BZGRyZXNzQXBpLkdldERlZmF1bHQoKTtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgSW1hZ2UgZXh0ZW5kcyBFbnRpdHk8U0ltYWdlPiB7XHJcblxyXG4gICAgICAgIFBhdGggPSBrby5vYnNlcnZhYmxlPHN0cmluZz4oKTtcclxuICAgICAgICBIZWlnaHQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBXaWR0aCA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG5cclxuICAgICAgICBDcmVhdGVTZXJ2ZXJFbnRpdHkoKTogU2VydmVyTW9kZWwuSW1hZ2Uge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLkltYWdlKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuSW1hZ2VBcGkuR2V0RGVmYXVsdCgpO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBUYWNrIGV4dGVuZHMgRW50aXR5PFNUYWNrPiB7XHJcbiAgICAgICAgU3RhcnREYXRlID0ga28ub2JzZXJ2YWJsZTxEYXRlPigpO1xyXG4gICAgICAgIEVuZERhdGUgPSBrby5vYnNlcnZhYmxlPERhdGU+KCk7XHJcbiAgICAgICAgU3RhcnQgPSBrby5vYnNlcnZhYmxlPEhhcmJvdXI+KCk7XHJcbiAgICAgICAgRW5kID0ga28ub2JzZXJ2YWJsZTxIYXJib3VyPigpO1xyXG4gICAgICAgIFdheXBvaW50cyA9IGtvLm9ic2VydmFibGVBcnJheTxTZXJ2ZXJNb2RlbC5XYXlwb2ludFRhY2s+KCk7XHJcbiAgICAgICAgQ3JldyA9IGtvLm9ic2VydmFibGVBcnJheTxQZXJzb24+KCk7XHJcbiAgICAgICAgRGlzdGFuY2UgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLlRhY2sge1xyXG4gICAgICAgICAgICByZXR1cm4gbmV3IFNlcnZlck1vZGVsLlRhY2soKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5UYWNrQXBpLkdldERlZmF1bHQoKTtcclxuXHJcblxyXG5cclxuICAgICAgICBDYW5SZW1vdmVUYWNrID0ga28uY29tcHV0ZWQoe1xyXG4gICAgICAgICAgICByZWFkOiAoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBpZiAobWFwVmlld01vZGVsLlNlbGVjdGVkVHJpcCgpID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIGZhbHNlO1xyXG4gICAgICAgICAgICAgICAgY29uc3QgdGFja3MgPSBtYXBWaWV3TW9kZWwuU2VsZWN0ZWRUcmlwKCkuVGFja3M7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBpbmRleCA9IHRhY2tzLmluZGV4T2YodGhpcyk7XHJcbiAgICAgICAgICAgICAgICBjb25zdCBwcmV2VGFjayA9IHRhY2tzKClbaW5kZXggLSAxXTtcclxuICAgICAgICAgICAgICAgIGNvbnN0IG5leHRUYWNrID0gdGFja3MoKVtpbmRleCArIDFdO1xyXG4gICAgICAgICAgICAgICAgaWYgKHByZXZUYWNrICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgaWYgKG5leHRUYWNrICE9PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICAgICAgICAgIHJldHVybiBwcmV2VGFjay5TdGFydCgpICE9PSBuZXh0VGFjay5TdGFydCgpO1xyXG4gICAgICAgICAgICAgICAgICAgIGVsc2VcclxuICAgICAgICAgICAgICAgICAgICAgICAgcmV0dXJuIHRydWU7XHJcbiAgICAgICAgICAgICAgICBlbHNlXHJcbiAgICAgICAgICAgICAgICAgICAgcmV0dXJuIG5leHRUYWNrICE9PSB1bmRlZmluZWQ7XHJcbiAgICAgICAgICAgIH0sXHJcbiAgICAgICAgICAgIGRlZmVyRXZhbHVhdGlvbjogdHJ1ZVxyXG4gICAgICAgIH0pO1xyXG5cclxuICAgICAgICBDb21wdXRlUGxhY2Vob2xkZXIgPSBrby5jb21wdXRlZCgoKTogc3RyaW5nID0+IHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuU3RhcnREYXRlKCkgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHJldHVybiBtb21lbnQodGhpcy5TdGFydERhdGUoKSkuZm9ybWF0KFwiTFwiKTtcclxuICAgICAgICAgICAgcmV0dXJuIFwiXCI7XHJcbiAgICAgICAgfSk7XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBMb2NhdGlvbiBleHRlbmRzIEVudGl0eTxTTG9jYXRpb24+IHtcclxuICAgICAgICBIYXJib3VySWQgPSBrby5vYnNlcnZhYmxlPG51bWJlcj4oKTtcclxuICAgICAgICBXZWJzaXRlID0ga28ub2JzZXJ2YWJsZTxzdHJpbmc+KCk7XHJcbiAgICAgICAgTmFtZSA9IGtvLm9ic2VydmFibGU8c3RyaW5nPigpO1xyXG4gICAgICAgIFJhdGluZyA9IGtvLm9ic2VydmFibGU8bnVtYmVyPigpO1xyXG4gICAgICAgIEFkZHJlc3MgPSBrby5vYnNlcnZhYmxlPEFkZHJlc3M+KCk7XHJcbiAgICAgICAgQWRkcmVzc0lkID0ga28ub2JzZXJ2YWJsZTxudW1iZXI+KCk7XHJcblxyXG4gICAgICAgIENyZWF0ZVNlcnZlckVudGl0eSgpOiBTZXJ2ZXJNb2RlbC5Mb2NhdGlvbiB7XHJcbiAgICAgICAgICAgIHJldHVybiBuZXcgU2VydmVyTW9kZWwuTG9jYXRpb24oKTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIFNlcnZlckFwaSA9IFNlcnZlckFwaS5Mb2NhdGlvbkFwaS5HZXREZWZhdWx0KCk7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFJlc3RhdXJhbnQgZXh0ZW5kcyBMb2NhdGlvbiB7XHJcblxyXG4gICAgICAgIENyZWF0ZVNlcnZlckVudGl0eSgpOiBTZXJ2ZXJNb2RlbC5SZXN0YXVyYW50IHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTZXJ2ZXJNb2RlbC5SZXN0YXVyYW50KCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZXJ2ZXJBcGkgPSBTZXJ2ZXJBcGkuUmVzdGF1cmFudEFwaS5HZXREZWZhdWx0KCk7XHJcbiAgICB9XHJcblxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBTdXBlcm1hcmtldCBleHRlbmRzIExvY2F0aW9uIHtcclxuXHJcbiAgICAgICAgQ3JlYXRlU2VydmVyRW50aXR5KCk6IFNlcnZlck1vZGVsLlN1cGVybWFya2V0IHtcclxuICAgICAgICAgICAgcmV0dXJuIG5ldyBTZXJ2ZXJNb2RlbC5TdXBlcm1hcmtldCgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgU2VydmVyQXBpID0gU2VydmVyQXBpLlN1cGVybWFya2V0QXBpLkdldERlZmF1bHQoKTtcclxuICAgIH1cclxuXHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFdheXBvaW50RGlzdGFuY2Uge1xyXG4gICAgICAgIGNvbnN0cnVjdG9yKHB1YmxpYyBQcmVjZXNzb3I6IFdheXBvaW50LFxyXG4gICAgICAgICAgICBwdWJsaWMgV2F5cG9pbnQ6IFdheXBvaW50LFxyXG4gICAgICAgICAgICBwdWJsaWMgRGlzdGFuY2U6IG51bWJlcixcclxuICAgICAgICAgICAgY2FsY3VsYXRlcmRXYXlwb2ludHM6IFdheXBvaW50W10sXHJcbiAgICAgICAgICAgIGNhbGN1bGF0ZVJvdXRlOiBib29sZWFuKSB7XHJcbiAgICAgICAgICAgIHRoaXMuTGF0TG5nID0gV2F5cG9pbnQuTGF0TG5nO1xyXG4gICAgICAgICAgICBjYWxjdWxhdGVyZFdheXBvaW50cy5wdXNoKFdheXBvaW50KTtcclxuICAgICAgICAgICAgZm9yIChsZXQgd3Agb2YgV2F5cG9pbnQuR2V0Q29ubmVjdGVkV2F5cG9pbnRzKCkpIHtcclxuICAgICAgICAgICAgICAgIGlmIChjYWxjdWxhdGVyZFdheXBvaW50cy5pbmRleE9mKHdwKSA9PT0gLTEpXHJcbiAgICAgICAgICAgICAgICAgICAgdGhpcy5Db25uZWN0ZWRXYXlQb2ludHMucHVzaCh3cCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICAgICAgaWYgKGNhbGN1bGF0ZVJvdXRlKSB7XHJcbiAgICAgICAgICAgICAgICBXYXlwb2ludC5Sb3V0ZVByZWNlc3NvcihQcmVjZXNzb3IpO1xyXG4gICAgICAgICAgICAgICAgV2F5cG9pbnQuUm91dGVEaXN0YW5jZShEaXN0YW5jZSk7XHJcbiAgICAgICAgICAgIH0gZWxzZSB7XHJcbiAgICAgICAgICAgICAgICBXYXlwb2ludC5QcmVjZXNzb3IoUHJlY2Vzc29yKTtcclxuICAgICAgICAgICAgICAgIFdheXBvaW50LkRpc3RhbmNlKERpc3RhbmNlKTtcclxuICAgICAgICAgICAgfVxyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29ubmVjdGVkV2F5UG9pbnRzID0gbmV3IEFycmF5PFdheXBvaW50PigpO1xyXG4gICAgICAgIENvbm5lY3RlZERpc3RhbmNlcyA9IG5ldyBBcnJheTxXYXlwb2ludERpc3RhbmNlPigpO1xyXG4gICAgICAgIExhdExuZzogTC5MYXRMbmc7XHJcbiAgICB9XHJcblxyXG59XHJcblxyXG5pbnRlcmZhY2UgS25vY2tvdXRPYnNlcnZhYmxlPFQ+IGV4dGVuZHMgS25vY2tvdXRTdWJzY3JpYmFibGU8VD4sIEtub2Nrb3V0T2JzZXJ2YWJsZUZ1bmN0aW9uczxUPiB7XHJcbiAgICBCbG9jazogYm9vbGVhbjtcclxufVxyXG5cclxuZW51bSBNYXJrZXJUeXBlIHtcclxuICAgIEhhcmJvdXIsXHJcbiAgICBEdW1teSxcclxuICAgIFdheXBvaW50LFxyXG4gICAgV2VhdGhlclN0YXRpb25cclxufSJdLCJzb3VyY2VSb290IjoiL3NvdXJjZS8ifQ==
