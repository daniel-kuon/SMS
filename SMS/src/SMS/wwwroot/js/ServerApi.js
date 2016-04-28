var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
var ServerApi;
(function (ServerApi) {
    (function (HttpMethod) {
        HttpMethod[HttpMethod["POST"] = 0] = "POST";
        HttpMethod[HttpMethod["GET"] = 1] = "GET";
        HttpMethod[HttpMethod["PUT"] = 2] = "PUT";
        HttpMethod[HttpMethod["DELETE"] = 3] = "DELETE";
    })(ServerApi.HttpMethod || (ServerApi.HttpMethod = {}));
    var HttpMethod = ServerApi.HttpMethod;
    var Api = (function () {
        function Api(baseUrl) {
            this.baseUrl = baseUrl;
        }
        Api.prototype.BuildRequestBody = function (url, method, data) {
            return {
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                method: HttpMethod[method],
                url: this.baseUrl + "/" + url,
                data: JSON.stringify(data)
            };
        };
        Api.prototype.Get = function (id) {
            if (id === undefined)
                return this.CreateRequest((this.BuildRequestBody("", HttpMethod.GET)));
            else
                return this.CreateRequest((this.BuildRequestBody(id.toString(), HttpMethod.GET)));
        };
        Api.prototype.Delete = function (id) {
            return this.CreateRequest((this.BuildRequestBody(id.toString(), HttpMethod.DELETE)));
        };
        Api.prototype.Create = function (entity) {
            return this.CreateRequest((this.BuildRequestBody("", HttpMethod.POST, entity)));
        };
        Api.prototype.Update = function (entity) {
            return this.CreateRequest(this.BuildRequestBody(entity.Id.toString(), HttpMethod.PUT, entity));
        };
        Api.prototype.CreateRequest = function (body) {
            Api.conntectionCount(Api.conntectionCount() + 1);
            return $.ajax(body).fail(function (d) {
                alert(d);
                console.log(d);
            }).always(function () {
                Api.conntectionCount(Api.conntectionCount() - 1);
            });
        };
        Api.conntectionCount = ko.observable(0);
        Api.connectionOpen = ko.computed(function () {
            return Api.conntectionCount() > 0;
        });
        return Api;
    }());
    ServerApi.Api = Api;
    var ConnectionApi = (function () {
        function ConnectionApi(baseUrl) {
            this.baseUrl = baseUrl;
        }
        ConnectionApi.prototype.Get = function () {
            return this.CreateRequest(this.BuildRequestBody("", HttpMethod.GET));
        };
        ConnectionApi.prototype.Connect = function (id1, id2) {
            return this.CreateRequest(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.POST));
        };
        ConnectionApi.prototype.Disconnect = function (id1, id2) {
            return this.CreateRequest(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.DELETE));
        };
        ConnectionApi.prototype.CreateRequest = function (body) {
            Api.conntectionCount(Api.conntectionCount() + 1);
            return $.ajax(body).fail(function (d) {
                alert(d);
                console.log(d);
            }).always(function () {
                Api.conntectionCount(Api.conntectionCount() - 1);
            });
        };
        ConnectionApi.prototype.BuildRequestBody = function (url, method, data) {
            return {
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                method: HttpMethod[method],
                url: this.baseUrl + "/" + url,
                data: JSON.stringify(data)
            };
        };
        return ConnectionApi;
    }());
    ServerApi.ConnectionApi = ConnectionApi;
    var HarbourApi = (function (_super) {
        __extends(HarbourApi, _super);
        function HarbourApi() {
            _super.apply(this, arguments);
        }
        HarbourApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new HarbourApi("/api/Harbours");
            return this.default;
        };
        return HarbourApi;
    }(Api));
    ServerApi.HarbourApi = HarbourApi;
    var AlbumApi = (function (_super) {
        __extends(AlbumApi, _super);
        function AlbumApi() {
            _super.apply(this, arguments);
        }
        AlbumApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new AlbumApi("/api/Albums");
            return this.default;
        };
        return AlbumApi;
    }(Api));
    ServerApi.AlbumApi = AlbumApi;
    var CommentApi = (function (_super) {
        __extends(CommentApi, _super);
        function CommentApi() {
            _super.apply(this, arguments);
        }
        CommentApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new CommentApi("/api/Comments");
            return this.default;
        };
        return CommentApi;
    }(Api));
    ServerApi.CommentApi = CommentApi;
    var CommentListApi = (function (_super) {
        __extends(CommentListApi, _super);
        function CommentListApi() {
            _super.apply(this, arguments);
        }
        CommentListApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new CommentListApi("/api/CommentLists");
            return this.default;
        };
        return CommentListApi;
    }(Api));
    ServerApi.CommentListApi = CommentListApi;
    var WaypointApi = (function (_super) {
        __extends(WaypointApi, _super);
        function WaypointApi() {
            _super.apply(this, arguments);
        }
        WaypointApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new WaypointApi("/api/Waypoints");
            return this.default;
        };
        return WaypointApi;
    }(Api));
    ServerApi.WaypointApi = WaypointApi;
    var WaypointConnectionApi = (function () {
        function WaypointConnectionApi(baseUrl) {
            this.baseUrl = baseUrl;
        }
        WaypointConnectionApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new WaypointConnectionApi("/api/WaypointConnections");
            return this.default;
        };
        WaypointConnectionApi.prototype.Get = function () {
            return this.CreateRequest(this.BuildRequestBody("", HttpMethod.GET));
        };
        WaypointConnectionApi.prototype.Connect = function (id1, id2) {
            return this.CreateRequest(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.POST));
        };
        WaypointConnectionApi.prototype.Disconnect = function (id1, id2) {
            if (id2 !== undefined)
                return this.CreateRequest(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.DELETE));
            return this.CreateRequest(this.BuildRequestBody(id1.toString(), HttpMethod.DELETE));
        };
        WaypointConnectionApi.prototype.CreateRequest = function (body) {
            Api.conntectionCount(Api.conntectionCount() + 1);
            return $.ajax(body).fail(function (d) {
                alert(d);
                console.log(d);
            }).always(function () {
                Api.conntectionCount(Api.conntectionCount() - 1);
            });
        };
        WaypointConnectionApi.prototype.BuildRequestBody = function (url, method, data) {
            return {
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                method: HttpMethod[method],
                url: this.baseUrl + "/" + url,
                data: JSON.stringify(data)
            };
        };
        return WaypointConnectionApi;
    }());
    ServerApi.WaypointConnectionApi = WaypointConnectionApi;
    var PersonApi = (function (_super) {
        __extends(PersonApi, _super);
        function PersonApi() {
            _super.apply(this, arguments);
        }
        PersonApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new PersonApi("/api/People");
            return this.default;
        };
        return PersonApi;
    }(Api));
    ServerApi.PersonApi = PersonApi;
    var JobApi = (function (_super) {
        __extends(JobApi, _super);
        function JobApi() {
            _super.apply(this, arguments);
        }
        JobApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new JobApi("/api/Jobs");
            return this.default;
        };
        return JobApi;
    }(Api));
    ServerApi.JobApi = JobApi;
    var TripApi = (function (_super) {
        __extends(TripApi, _super);
        function TripApi() {
            _super.apply(this, arguments);
        }
        TripApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new TripApi("/api/Trips");
            return this.default;
        };
        return TripApi;
    }(Api));
    ServerApi.TripApi = TripApi;
    var TackApi = (function (_super) {
        __extends(TackApi, _super);
        function TackApi() {
            _super.apply(this, arguments);
        }
        TackApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new TackApi("/api/Tacks");
            return this.default;
        };
        return TackApi;
    }(Api));
    ServerApi.TackApi = TackApi;
    var AddressApi = (function (_super) {
        __extends(AddressApi, _super);
        function AddressApi() {
            _super.apply(this, arguments);
        }
        AddressApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new AddressApi("/api/Addresses");
            return this.default;
        };
        return AddressApi;
    }(Api));
    ServerApi.AddressApi = AddressApi;
    var ImageApi = (function (_super) {
        __extends(ImageApi, _super);
        function ImageApi() {
            _super.apply(this, arguments);
        }
        ImageApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new ImageApi("/api/Images");
            return this.default;
        };
        return ImageApi;
    }(Api));
    ServerApi.ImageApi = ImageApi;
    var LocationApi = (function (_super) {
        __extends(LocationApi, _super);
        function LocationApi() {
            _super.apply(this, arguments);
        }
        LocationApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new LocationApi("/api/Locations");
            return this.default;
        };
        return LocationApi;
    }(Api));
    ServerApi.LocationApi = LocationApi;
    var SupermarketApi = (function (_super) {
        __extends(SupermarketApi, _super);
        function SupermarketApi() {
            _super.apply(this, arguments);
        }
        SupermarketApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new SupermarketApi("/api/Supermarkets");
            return this.default;
        };
        return SupermarketApi;
    }(Api));
    ServerApi.SupermarketApi = SupermarketApi;
    var RestaurantApi = (function (_super) {
        __extends(RestaurantApi, _super);
        function RestaurantApi() {
            _super.apply(this, arguments);
        }
        RestaurantApi.GetDefault = function () {
            if (this.default === undefined)
                this.default = new RestaurantApi("/api/Restaurants");
            return this.default;
        };
        return RestaurantApi;
    }(Api));
    ServerApi.RestaurantApi = RestaurantApi;
})(ServerApi || (ServerApi = {}));

//# sourceMappingURL=data:application/json;base64,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QUFVQyxDQVZnQyxHQUFHLEdBVW5DO0lBVlkscUJBQVcsY0FVdkIsQ0FBQTtJQUVEO1FBQW9DLGtDQUFnQjtRQUFwRDtZQUFvQyw4QkFBZ0I7UUFVcEQsQ0FBQztRQU5VLHlCQUFVLEdBQWpCO1lBQ0ksRUFBRSxDQUFDLENBQUMsSUFBSSxDQUFDLE9BQU8sS0FBSyxTQUFTLENBQUM7Z0JBQzNCLElBQUksQ0FBQyxPQUFPLEdBQUcsSUFBSSxjQUFjLENBQUMsbUJBQW1CLENBQUMsQ0FBQztZQUMzRCxNQUFNLENBQUMsSUFBSSxDQUFDLE9BQU8sQ0FBQztRQUN4QixDQUFDO1FBRUwscUJBQUM7SUFBRCxDQVZBLEFBVUMsQ0FWbUMsR0FBRyxHQVV0QztJQVZZLHdCQUFjLGlCQVUxQixDQUFBO0lBRUQ7UUFBbUMsaUNBQWU7UUFBbEQ7WUFBbUMsOEJBQWU7UUFVbEQsQ0FBQztRQU5VLHdCQUFVLEdBQWpCO1lBQ0ksRUFBRSxDQUFDLENBQUMsSUFBSSxDQUFDLE9BQU8sS0FBSyxTQUFTLENBQUM7Z0JBQzNCLElBQUksQ0FBQyxPQUFPLEdBQUcsSUFBSSxhQUFhLENBQUMsa0JBQWtCLENBQUMsQ0FBQztZQUN6RCxNQUFNLENBQUMsSUFBSSxDQUFDLE9BQU8sQ0FBQztRQUN4QixDQUFDO1FBRUwsb0JBQUM7SUFBRCxDQVZBLEFBVUMsQ0FWa0MsR0FBRyxHQVVyQztJQVZZLHVCQUFhLGdCQVV6QixDQUFBO0FBY0wsQ0FBQyxFQWhXTSxTQUFTLEtBQVQsU0FBUyxRQWdXZiIsImZpbGUiOiJTZXJ2ZXJBcGkuanMiLCJzb3VyY2VzQ29udGVudCI6WyJtb2R1bGUgU2VydmVyQXBpIHtcclxuICAgIGltcG9ydCBFbnRpdHkgPSBTZXJ2ZXJNb2RlbC5FbnRpdHk7XHJcbiAgICBpbXBvcnQgSGFyYm91ciA9IFNlcnZlck1vZGVsLkhhcmJvdXI7XHJcbiAgICBpbXBvcnQgV2F5cG9pbnQgPSBTZXJ2ZXJNb2RlbC5XYXlwb2ludDtcclxuICAgIGltcG9ydCBQZXJzb24gPSBTZXJ2ZXJNb2RlbC5QZXJzb247XHJcbiAgICBpbXBvcnQgSm9iID0gU2VydmVyTW9kZWwuSm9iO1xyXG4gICAgaW1wb3J0IFRyaXAgPSBTZXJ2ZXJNb2RlbC5UcmlwO1xyXG4gICAgaW1wb3J0IFdheXBvaW50Q29ubmVjdGlvbiA9IFNlcnZlck1vZGVsLldheXBvaW50Q29ubmVjdGlvbjtcclxuICAgIGltcG9ydCBUYWNrID0gU2VydmVyTW9kZWwuVGFjaztcclxuICAgIGltcG9ydCBBZGRyZXNzID0gU2VydmVyTW9kZWwuQWRkcmVzcztcclxuICAgIGltcG9ydCBJbWFnZSA9IFNlcnZlck1vZGVsLkltYWdlO1xyXG4gICAgaW1wb3J0IEFsYnVtID0gU2VydmVyTW9kZWwuQWxidW07XHJcbiAgICBpbXBvcnQgV2F5cG9pbnRUYWNrID0gU2VydmVyTW9kZWwuV2F5cG9pbnRUYWNrO1xyXG4gICAgaW1wb3J0IExvY2F0aW9uID0gU2VydmVyTW9kZWwuTG9jYXRpb247XHJcbiAgICBpbXBvcnQgUmVzdGF1cmFudCA9IFNlcnZlck1vZGVsLlJlc3RhdXJhbnQ7XHJcbiAgICBpbXBvcnQgU3VwZXJtYXJrZXQgPSBTZXJ2ZXJNb2RlbC5TdXBlcm1hcmtldDtcclxuICAgIGltcG9ydCBDb21tZW50ID0gU2VydmVyTW9kZWwuQ29tbWVudDtcclxuICAgIGltcG9ydCBDb21tZW50TGlzdCA9IFNlcnZlck1vZGVsLkNvbW1lbnRMaXN0O1xyXG5cclxuICAgIGV4cG9ydCBlbnVtIEh0dHBNZXRob2Qge1xyXG4gICAgICAgIFBPU1QsXHJcbiAgICAgICAgR0VULFxyXG4gICAgICAgIFBVVCxcclxuICAgICAgICBERUxFVEVcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgYWJzdHJhY3QgY2xhc3MgQXBpPFQgZXh0ZW5kcyBFbnRpdHk+IHtcclxuICAgICAgICBzdGF0aWMgY29ubnRlY3Rpb25Db3VudCA9IGtvLm9ic2VydmFibGUoMCk7XHJcblxyXG4gICAgICAgIHN0YXRpYyBjb25uZWN0aW9uT3Blbj1rby5jb21wdXRlZCgoKSA9PiB7XHJcbiAgICAgICAgICAgIHJldHVybiBBcGkuY29ubnRlY3Rpb25Db3VudCgpID4gMDtcclxuICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgY29uc3RydWN0b3IocHVibGljIGJhc2VVcmw6IHN0cmluZykge1xyXG5cclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEJ1aWxkUmVxdWVzdEJvZHkodXJsOiBzdHJpbmcsIG1ldGhvZDogSHR0cE1ldGhvZCwgZGF0YT86IGFueSk6IEpRdWVyeUFqYXhTZXR0aW5ncyB7XHJcbiAgICAgICAgICAgIHJldHVybiB7XHJcbiAgICAgICAgICAgICAgICBjb250ZW50VHlwZTogXCJhcHBsaWNhdGlvbi9qc29uOyBjaGFyc2V0PXV0Zi04XCIsXHJcbiAgICAgICAgICAgICAgICBkYXRhVHlwZTogXCJqc29uXCIsXHJcbiAgICAgICAgICAgICAgICBtZXRob2Q6IEh0dHBNZXRob2RbbWV0aG9kXSxcclxuICAgICAgICAgICAgICAgIHVybDogdGhpcy5iYXNlVXJsICsgXCIvXCIgKyB1cmwsXHJcbiAgICAgICAgICAgICAgICBkYXRhOiBKU09OLnN0cmluZ2lmeShkYXRhKVxyXG4gICAgICAgICAgICB9O1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgR2V0KCk6IEpRdWVyeVByb21pc2U8VFtdPjtcclxuICAgICAgICBHZXQoaWQ6IG51bWJlcik6IEpRdWVyeVByb21pc2U8VD47XHJcbiAgICAgICAgR2V0KGlkPzogbnVtYmVyKTogYW55IHtcclxuICAgICAgICAgICAgaWYgKGlkID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICByZXR1cm4gdGhpcy5DcmVhdGVSZXF1ZXN0PFRbXT4oKHRoaXMuQnVpbGRSZXF1ZXN0Qm9keShcIlwiLCBIdHRwTWV0aG9kLkdFVCkpKTtcclxuICAgICAgICAgICAgZWxzZVxyXG4gICAgICAgICAgICAgICAgcmV0dXJuIHRoaXMuQ3JlYXRlUmVxdWVzdDxUPigodGhpcy5CdWlsZFJlcXVlc3RCb2R5KGlkLnRvU3RyaW5nKCksIEh0dHBNZXRob2QuR0VUKSkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgRGVsZXRlKGlkOiBudW1iZXIpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuQ3JlYXRlUmVxdWVzdDxUPigodGhpcy5CdWlsZFJlcXVlc3RCb2R5KGlkLnRvU3RyaW5nKCksIEh0dHBNZXRob2QuREVMRVRFKSkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ3JlYXRlKGVudGl0eTogVCk6IEpRdWVyeVByb21pc2U8VD4ge1xyXG4gICAgICAgICAgICByZXR1cm4gdGhpcy5DcmVhdGVSZXF1ZXN0PFQ+KCh0aGlzLkJ1aWxkUmVxdWVzdEJvZHkoXCJcIiwgSHR0cE1ldGhvZC5QT1NULCBlbnRpdHkpKSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBVcGRhdGUoZW50aXR5OiBUKTogSlF1ZXJ5UHJvbWlzZTxUPiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLkNyZWF0ZVJlcXVlc3Q8VD4odGhpcy5CdWlsZFJlcXVlc3RCb2R5KGVudGl0eS5JZC50b1N0cmluZygpLCBIdHRwTWV0aG9kLlBVVCwgZW50aXR5KSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBwcm90ZWN0ZWQgQ3JlYXRlUmVxdWVzdDxUPihib2R5OiBKUXVlcnlBamF4U2V0dGluZ3MpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgQXBpLmNvbm50ZWN0aW9uQ291bnQoQXBpLmNvbm50ZWN0aW9uQ291bnQoKSArIDEpO1xyXG4gICAgICAgICAgICByZXR1cm4gJC5hamF4KGJvZHkpLmZhaWwoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBhbGVydChkKTtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGQpO1xyXG4gICAgICAgICAgICB9KS5hbHdheXMoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgQXBpLmNvbm50ZWN0aW9uQ291bnQoQXBpLmNvbm50ZWN0aW9uQ291bnQoKSAtIDEpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGFic3RyYWN0IGNsYXNzIENvbm5lY3Rpb25BcGk8VD4ge1xyXG5cclxuICAgICAgICBHZXQoKTogSlF1ZXJ5UHJvbWlzZTxUW10+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuQ3JlYXRlUmVxdWVzdDxUW10+KHRoaXMuQnVpbGRSZXF1ZXN0Qm9keShcIlwiLCBIdHRwTWV0aG9kLkdFVCkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29ubmVjdChpZDE6IG51bWJlciwgaWQyOiBudW1iZXIpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuQ3JlYXRlUmVxdWVzdDxUPih0aGlzLkJ1aWxkUmVxdWVzdEJvZHkoaWQxICsgXCIvXCIgKyBpZDIsIEh0dHBNZXRob2QuUE9TVCkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgRGlzY29ubmVjdChpZDE6IG51bWJlciwgaWQyOiBudW1iZXIpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuQ3JlYXRlUmVxdWVzdDxUPih0aGlzLkJ1aWxkUmVxdWVzdEJvZHkoaWQxICsgXCIvXCIgKyBpZDIsIEh0dHBNZXRob2QuREVMRVRFKSk7XHJcbiAgICAgICAgfVxyXG5cclxuXHJcbiAgICAgICAgcHJvdGVjdGVkIENyZWF0ZVJlcXVlc3Q8VD4oYm9keTogSlF1ZXJ5QWpheFNldHRpbmdzKTogSlF1ZXJ5UHJvbWlzZTxUPiB7XHJcbiAgICAgICAgICAgICBBcGkuY29ubnRlY3Rpb25Db3VudChBcGkuY29ubnRlY3Rpb25Db3VudCgpICsgMSk7XHJcbiAgICAgICAgICAgIHJldHVybiAkLmFqYXgoYm9keSkuZmFpbChkID0+IHtcclxuICAgICAgICAgICAgICAgIGFsZXJ0KGQpO1xyXG4gICAgICAgICAgICAgICAgY29uc29sZS5sb2coZCk7XHJcbiAgICAgICAgICAgIH0pLmFsd2F5cygoKSA9PiB7XHJcbiAgICAgICAgICAgICAgICBBcGkuY29ubnRlY3Rpb25Db3VudChBcGkuY29ubnRlY3Rpb25Db3VudCgpIC0gMSk7XHJcbiAgICAgICAgICAgIH0pO1xyXG4gICAgICAgIH1cclxuXHJcblxyXG4gICAgICAgIGNvbnN0cnVjdG9yKHB1YmxpYyBiYXNlVXJsOiBzdHJpbmcpIHtcclxuXHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBCdWlsZFJlcXVlc3RCb2R5KHVybDogc3RyaW5nLCBtZXRob2Q6IEh0dHBNZXRob2QsIGRhdGE/OiBhbnkpOiBKUXVlcnlBamF4U2V0dGluZ3Mge1xyXG4gICAgICAgICAgICByZXR1cm4ge1xyXG4gICAgICAgICAgICAgICAgY29udGVudFR5cGU6IFwiYXBwbGljYXRpb24vanNvbjsgY2hhcnNldD11dGYtOFwiLFxyXG4gICAgICAgICAgICAgICAgZGF0YVR5cGU6IFwianNvblwiLFxyXG4gICAgICAgICAgICAgICAgbWV0aG9kOiBIdHRwTWV0aG9kW21ldGhvZF0sXHJcbiAgICAgICAgICAgICAgICB1cmw6IHRoaXMuYmFzZVVybCArIFwiL1wiICsgdXJsLFxyXG4gICAgICAgICAgICAgICAgZGF0YTogSlNPTi5zdHJpbmdpZnkoZGF0YSlcclxuICAgICAgICAgICAgfTtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBIYXJib3VyQXBpIGV4dGVuZHMgQXBpPEhhcmJvdXI+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogSGFyYm91ckFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogSGFyYm91ckFwaSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLmRlZmF1bHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHRoaXMuZGVmYXVsdCA9IG5ldyBIYXJib3VyQXBpKFwiL2FwaS9IYXJib3Vyc1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBbGJ1bUFwaSBleHRlbmRzIEFwaTxBbGJ1bT4ge1xyXG5cclxuICAgICAgICBwcml2YXRlIHN0YXRpYyBkZWZhdWx0OiBBbGJ1bUFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogQWxidW1BcGkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5kZWZhdWx0ID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICB0aGlzLmRlZmF1bHQgPSBuZXcgQWxidW1BcGkoXCIvYXBpL0FsYnVtc1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBDb21tZW50QXBpIGV4dGVuZHMgQXBpPENvbW1lbnQ+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogQ29tbWVudEFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogQ29tbWVudEFwaSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLmRlZmF1bHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHRoaXMuZGVmYXVsdCA9IG5ldyBDb21tZW50QXBpKFwiL2FwaS9Db21tZW50c1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBDb21tZW50TGlzdEFwaSBleHRlbmRzIEFwaTxDb21tZW50TGlzdD4ge1xyXG5cclxuICAgICAgICBwcml2YXRlIHN0YXRpYyBkZWZhdWx0OiBDb21tZW50TGlzdEFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogQ29tbWVudExpc3RBcGkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5kZWZhdWx0ID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICB0aGlzLmRlZmF1bHQgPSBuZXcgQ29tbWVudExpc3RBcGkoXCIvYXBpL0NvbW1lbnRMaXN0c1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBXYXlwb2ludEFwaSBleHRlbmRzIEFwaTxXYXlwb2ludD4ge1xyXG5cclxuICAgICAgICBwcml2YXRlIHN0YXRpYyBkZWZhdWx0OiBXYXlwb2ludEFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogV2F5cG9pbnRBcGkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5kZWZhdWx0ID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICB0aGlzLmRlZmF1bHQgPSBuZXcgV2F5cG9pbnRBcGkoXCIvYXBpL1dheXBvaW50c1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFdheXBvaW50Q29ubmVjdGlvbkFwaSB7XHJcblxyXG4gICAgICAgIHByaXZhdGUgc3RhdGljIGRlZmF1bHQ6IFdheXBvaW50Q29ubmVjdGlvbkFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogV2F5cG9pbnRDb25uZWN0aW9uQXBpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuZGVmYXVsdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgdGhpcy5kZWZhdWx0ID0gbmV3IFdheXBvaW50Q29ubmVjdGlvbkFwaShcIi9hcGkvV2F5cG9pbnRDb25uZWN0aW9uc1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEdldCgpOiBKUXVlcnlQcm9taXNlPFdheXBvaW50Q29ubmVjdGlvbltdPiB7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLkNyZWF0ZVJlcXVlc3Q8V2F5cG9pbnRDb25uZWN0aW9uW10+KHRoaXMuQnVpbGRSZXF1ZXN0Qm9keShcIlwiLCBIdHRwTWV0aG9kLkdFVCkpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQ29ubmVjdChpZDE6IG51bWJlciwgaWQyOiBudW1iZXIpOiBKUXVlcnlQcm9taXNlPFdheXBvaW50Q29ubmVjdGlvbj4ge1xyXG4gICAgICAgICAgICByZXR1cm4gdGhpcy5DcmVhdGVSZXF1ZXN0PFdheXBvaW50Q29ubmVjdGlvbj4odGhpcy5CdWlsZFJlcXVlc3RCb2R5KGlkMSArIFwiL1wiICsgaWQyLCBIdHRwTWV0aG9kLlBPU1QpKTtcclxuICAgICAgICB9XHJcbiAgICAgICAgRGlzY29ubmVjdChpZDogbnVtYmVyKTogSlF1ZXJ5UHJvbWlzZTxXYXlwb2ludENvbm5lY3Rpb24+O1xyXG4gICAgICAgIERpc2Nvbm5lY3QoaWQxOiBudW1iZXIsIGlkMjogbnVtYmVyKTogSlF1ZXJ5UHJvbWlzZTxXYXlwb2ludENvbm5lY3Rpb24+O1xyXG4gICAgICAgIERpc2Nvbm5lY3QoaWQxOiBudW1iZXIsIGlkMj86IG51bWJlcik6IEpRdWVyeVByb21pc2U8V2F5cG9pbnRDb25uZWN0aW9uPiB7XHJcbiAgICAgICAgICAgIGlmIChpZDIgIT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHJldHVybiB0aGlzLkNyZWF0ZVJlcXVlc3Q8V2F5cG9pbnRDb25uZWN0aW9uPih0aGlzLkJ1aWxkUmVxdWVzdEJvZHkoaWQxICsgXCIvXCIgKyBpZDIsIEh0dHBNZXRob2QuREVMRVRFKSk7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLkNyZWF0ZVJlcXVlc3Q8V2F5cG9pbnRDb25uZWN0aW9uPih0aGlzLkJ1aWxkUmVxdWVzdEJvZHkoaWQxLnRvU3RyaW5nKCksIEh0dHBNZXRob2QuREVMRVRFKSk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBwcm90ZWN0ZWQgQ3JlYXRlUmVxdWVzdDxUPihib2R5OiBKUXVlcnlBamF4U2V0dGluZ3MpOiBKUXVlcnlQcm9taXNlPFQ+IHtcclxuICAgICAgICAgICAgQXBpLmNvbm50ZWN0aW9uQ291bnQoQXBpLmNvbm50ZWN0aW9uQ291bnQoKSArIDEpO1xyXG4gICAgICAgICAgICByZXR1cm4gJC5hamF4KGJvZHkpLmZhaWwoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICBhbGVydChkKTtcclxuICAgICAgICAgICAgICAgIGNvbnNvbGUubG9nKGQpO1xyXG4gICAgICAgICAgICB9KS5hbHdheXMoKCkgPT4ge1xyXG4gICAgICAgICAgICAgICAgQXBpLmNvbm50ZWN0aW9uQ291bnQoQXBpLmNvbm50ZWN0aW9uQ291bnQoKSAtIDEpO1xyXG4gICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcblxyXG5cclxuICAgICAgICBjb25zdHJ1Y3RvcihwdWJsaWMgYmFzZVVybDogc3RyaW5nKSB7XHJcblxyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgQnVpbGRSZXF1ZXN0Qm9keSh1cmw6IHN0cmluZywgbWV0aG9kOiBIdHRwTWV0aG9kLCBkYXRhPzogYW55KTogSlF1ZXJ5QWpheFNldHRpbmdzIHtcclxuICAgICAgICAgICAgcmV0dXJuIHtcclxuICAgICAgICAgICAgICAgIGNvbnRlbnRUeXBlOiBcImFwcGxpY2F0aW9uL2pzb247IGNoYXJzZXQ9dXRmLThcIixcclxuICAgICAgICAgICAgICAgIGRhdGFUeXBlOiBcImpzb25cIixcclxuICAgICAgICAgICAgICAgIG1ldGhvZDogSHR0cE1ldGhvZFttZXRob2RdLFxyXG4gICAgICAgICAgICAgICAgdXJsOiB0aGlzLmJhc2VVcmwgKyBcIi9cIiArIHVybCxcclxuICAgICAgICAgICAgICAgIGRhdGE6IEpTT04uc3RyaW5naWZ5KGRhdGEpXHJcbiAgICAgICAgICAgIH07XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgUGVyc29uQXBpIGV4dGVuZHMgQXBpPFBlcnNvbj4ge1xyXG5cclxuICAgICAgICBwcml2YXRlIHN0YXRpYyBkZWZhdWx0OiBQZXJzb25BcGk7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXREZWZhdWx0KCk6IFBlcnNvbkFwaSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLmRlZmF1bHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHRoaXMuZGVmYXVsdCA9IG5ldyBQZXJzb25BcGkoXCIvYXBpL1Blb3BsZVwiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBKb2JBcGkgZXh0ZW5kcyBBcGk8Sm9iPiB7XHJcblxyXG4gICAgICAgIHByaXZhdGUgc3RhdGljIGRlZmF1bHQ6IEpvYkFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogSm9iQXBpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuZGVmYXVsdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgdGhpcy5kZWZhdWx0ID0gbmV3IEpvYkFwaShcIi9hcGkvSm9ic1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBUcmlwQXBpIGV4dGVuZHMgQXBpPFRyaXA+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogVHJpcEFwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogVHJpcEFwaSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLmRlZmF1bHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHRoaXMuZGVmYXVsdCA9IG5ldyBUcmlwQXBpKFwiL2FwaS9Ucmlwc1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBUYWNrQXBpIGV4dGVuZHMgQXBpPFRhY2s+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogVGFja0FwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogVGFja0FwaSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLmRlZmF1bHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHRoaXMuZGVmYXVsdCA9IG5ldyBUYWNrQXBpKFwiL2FwaS9UYWNrc1wiKTtcclxuICAgICAgICAgICAgcmV0dXJuIHRoaXMuZGVmYXVsdDtcclxuICAgICAgICB9XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBZGRyZXNzQXBpIGV4dGVuZHMgQXBpPEFkZHJlc3M+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogQWRkcmVzc0FwaTtcclxuXHJcbiAgICAgICAgc3RhdGljIEdldERlZmF1bHQoKTogQWRkcmVzc0FwaSB7XHJcbiAgICAgICAgICAgIGlmICh0aGlzLmRlZmF1bHQgPT09IHVuZGVmaW5lZClcclxuICAgICAgICAgICAgICAgIHRoaXMuZGVmYXVsdCA9IG5ldyBBZGRyZXNzQXBpKFwiL2FwaS9BZGRyZXNzZXNcIik7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLmRlZmF1bHQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgSW1hZ2VBcGkgZXh0ZW5kcyBBcGk8SW1hZ2U+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogSW1hZ2VBcGk7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXREZWZhdWx0KCk6IEltYWdlQXBpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuZGVmYXVsdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgdGhpcy5kZWZhdWx0ID0gbmV3IEltYWdlQXBpKFwiL2FwaS9JbWFnZXNcIik7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLmRlZmF1bHQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgTG9jYXRpb25BcGkgZXh0ZW5kcyBBcGk8TG9jYXRpb24+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogTG9jYXRpb25BcGk7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXREZWZhdWx0KCk6IExvY2F0aW9uQXBpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuZGVmYXVsdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgdGhpcy5kZWZhdWx0ID0gbmV3IExvY2F0aW9uQXBpKFwiL2FwaS9Mb2NhdGlvbnNcIik7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLmRlZmF1bHQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgU3VwZXJtYXJrZXRBcGkgZXh0ZW5kcyBBcGk8U3VwZXJtYXJrZXQ+IHtcclxuXHJcbiAgICAgICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogU3VwZXJtYXJrZXRBcGk7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXREZWZhdWx0KCk6IFN1cGVybWFya2V0QXBpIHtcclxuICAgICAgICAgICAgaWYgKHRoaXMuZGVmYXVsdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgICAgICAgICAgICAgdGhpcy5kZWZhdWx0ID0gbmV3IFN1cGVybWFya2V0QXBpKFwiL2FwaS9TdXBlcm1hcmtldHNcIik7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLmRlZmF1bHQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgUmVzdGF1cmFudEFwaSBleHRlbmRzIEFwaTxSZXN0YXVyYW50PiB7XHJcblxyXG4gICAgICAgIHByaXZhdGUgc3RhdGljIGRlZmF1bHQ6IFJlc3RhdXJhbnRBcGk7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXREZWZhdWx0KCk6IFJlc3RhdXJhbnRBcGkge1xyXG4gICAgICAgICAgICBpZiAodGhpcy5kZWZhdWx0ID09PSB1bmRlZmluZWQpXHJcbiAgICAgICAgICAgICAgICB0aGlzLmRlZmF1bHQgPSBuZXcgUmVzdGF1cmFudEFwaShcIi9hcGkvUmVzdGF1cmFudHNcIik7XHJcbiAgICAgICAgICAgIHJldHVybiB0aGlzLmRlZmF1bHQ7XHJcbiAgICAgICAgfVxyXG5cclxuICAgIH1cclxuXHJcbiAgICAvL2V4cG9ydCBjbGFzcyBXYXlwb2ludFRhY2tBcGkgZXh0ZW5kcyBBcGk8V2F5cG9pbnRUYWNrPiB7XHJcblxyXG4gICAgLy8gICAgcHJpdmF0ZSBzdGF0aWMgZGVmYXVsdDogV2F5cG9pbnRUYWNrQXBpO1xyXG5cclxuICAgIC8vICAgIHN0YXRpYyBHZXREZWZhdWx0KCk6IFdheXBvaW50VGFja0FwaSB7XHJcbiAgICAvLyAgICAgICAgaWYgKHRoaXMuZGVmYXVsdCA9PT0gdW5kZWZpbmVkKVxyXG4gICAgLy8gICAgICAgICAgICB0aGlzLmRlZmF1bHQgPSBuZXcgV2F5cG9pbnRUYWNrQXBpKFwiL2FwaS9XYXlwb2ludFRhY2tzXCIpO1xyXG4gICAgLy8gICAgICAgIHJldHVybiB0aGlzLmRlZmF1bHQ7XHJcbiAgICAvLyAgICB9XHJcblxyXG4gICAgLy99XHJcblxyXG59Il0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
