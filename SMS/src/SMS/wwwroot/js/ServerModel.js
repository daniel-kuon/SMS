var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
var ServerModel;
(function (ServerModel) {
    var Entity = (function () {
        function Entity() {
        }
        return Entity;
    }());
    ServerModel.Entity = Entity;
    var CommentList = (function (_super) {
        __extends(CommentList, _super);
        function CommentList() {
            _super.apply(this, arguments);
        }
        return CommentList;
    }(Entity));
    ServerModel.CommentList = CommentList;
    var Comment = (function (_super) {
        __extends(Comment, _super);
        function Comment() {
            _super.apply(this, arguments);
        }
        return Comment;
    }(Entity));
    ServerModel.Comment = Comment;
    var Waypoint = (function (_super) {
        __extends(Waypoint, _super);
        function Waypoint() {
            _super.apply(this, arguments);
        }
        Waypoint.GetType = function () {
            return "Waypoint";
        };
        return Waypoint;
    }(Entity));
    ServerModel.Waypoint = Waypoint;
    var WaypointConnection = (function () {
        function WaypointConnection() {
        }
        return WaypointConnection;
    }());
    ServerModel.WaypointConnection = WaypointConnection;
    var Harbour = (function (_super) {
        __extends(Harbour, _super);
        function Harbour() {
            _super.apply(this, arguments);
        }
        Harbour.GetType = function () {
            return "Harbour";
        };
        return Harbour;
    }(Waypoint));
    ServerModel.Harbour = Harbour;
    var Person = (function (_super) {
        __extends(Person, _super);
        function Person() {
            _super.apply(this, arguments);
        }
        return Person;
    }(Entity));
    ServerModel.Person = Person;
    var Job = (function (_super) {
        __extends(Job, _super);
        function Job() {
            _super.apply(this, arguments);
        }
        return Job;
    }(Entity));
    ServerModel.Job = Job;
    var Trip = (function (_super) {
        __extends(Trip, _super);
        function Trip() {
            _super.apply(this, arguments);
        }
        return Trip;
    }(Entity));
    ServerModel.Trip = Trip;
    var Address = (function (_super) {
        __extends(Address, _super);
        function Address() {
            _super.apply(this, arguments);
        }
        return Address;
    }(Entity));
    ServerModel.Address = Address;
    var Image = (function (_super) {
        __extends(Image, _super);
        function Image() {
            _super.apply(this, arguments);
        }
        return Image;
    }(Entity));
    ServerModel.Image = Image;
    var Album = (function (_super) {
        __extends(Album, _super);
        function Album() {
            _super.apply(this, arguments);
        }
        return Album;
    }(Entity));
    ServerModel.Album = Album;
    var WaypointTack = (function () {
        function WaypointTack() {
        }
        return WaypointTack;
    }());
    ServerModel.WaypointTack = WaypointTack;
    var Tack = (function (_super) {
        __extends(Tack, _super);
        function Tack() {
            _super.apply(this, arguments);
        }
        return Tack;
    }(Entity));
    ServerModel.Tack = Tack;
    var Location = (function (_super) {
        __extends(Location, _super);
        function Location() {
            _super.apply(this, arguments);
        }
        Location.GetType = function () {
            return "Location";
        };
        return Location;
    }(Entity));
    ServerModel.Location = Location;
    var Restaurant = (function (_super) {
        __extends(Restaurant, _super);
        function Restaurant() {
            _super.apply(this, arguments);
        }
        Restaurant.GetType = function () {
            return "Restaurant";
        };
        return Restaurant;
    }(Location));
    ServerModel.Restaurant = Restaurant;
    var Supermarket = (function (_super) {
        __extends(Supermarket, _super);
        function Supermarket() {
            _super.apply(this, arguments);
        }
        Supermarket.GetType = function () {
            return "Supermarket";
        };
        return Supermarket;
    }(Location));
    ServerModel.Supermarket = Supermarket;
})(ServerModel || (ServerModel = {}));

//# sourceMappingURL=data:application/json;base64,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QkFBUSxXQVdwQixDQUFBO0lBRUQ7UUFBZ0MsOEJBQVE7UUFBeEM7WUFBZ0MsOEJBQVE7UUFJbkMsQ0FBQztRQUZLLGtCQUFPLEdBQWQ7WUFDSyxNQUFNLENBQUMsWUFBWSxDQUFDO1FBQ3pCLENBQUM7UUFBQSxpQkFBQztJQUFELENBSkwsQUFJTSxDQUowQixRQUFRLEdBSWxDO0lBSk8sc0JBQVUsYUFJakIsQ0FBQTtJQUNOO1FBQWlDLCtCQUFRO1FBQXpDO1lBQWlDLDhCQUFRO1FBSXBDLENBQUM7UUFGSyxtQkFBTyxHQUFkO1lBQ0ssTUFBTSxDQUFDLGFBQWEsQ0FBQztRQUMxQixDQUFDO1FBQUEsa0JBQUM7SUFBRCxDQUpMLEFBSU0sQ0FKMkIsUUFBUSxHQUluQztJQUpPLHVCQUFXLGNBSWxCLENBQUE7QUFDVixDQUFDLEVBOUhNLFdBQVcsS0FBWCxXQUFXLFFBOEhqQiIsImZpbGUiOiJTZXJ2ZXJNb2RlbC5qcyIsInNvdXJjZXNDb250ZW50IjpbIm1vZHVsZSBTZXJ2ZXJNb2RlbCB7XHJcbiAgICBleHBvcnQgYWJzdHJhY3QgY2xhc3MgRW50aXR5IHtcclxuXHJcbiAgICAgICAgSWQ6IG51bWJlcjtcclxuXHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIENvbW1lbnRMaXN0IGV4dGVuZHMgRW50aXR5e31cclxuICAgIGV4cG9ydCBjbGFzcyBDb21tZW50IGV4dGVuZHMgRW50aXR5e31cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgV2F5cG9pbnQgZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIFdheXBvaW50TnVtYmVyOiBudW1iZXI7XHJcbiAgICAgICAgTmFtZTogc3RyaW5nO1xyXG4gICAgICAgIERlc2NyaXB0aW9uOiBzdHJpbmc7XHJcbiAgICAgICAgTGF0aXR1ZGU6IG51bWJlcjtcclxuICAgICAgICBMb25naXR1ZGU6IG51bWJlcjtcclxuICAgICAgICBUeXBlOiBzdHJpbmc7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCk6IHN0cmluZyB7XHJcbiAgICAgICAgICAgIHJldHVybiBcIldheXBvaW50XCI7XHJcbiAgICAgICAgfVxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBXYXlwb2ludENvbm5lY3Rpb24ge1xyXG4gICAgICAgIFdheXBvaW50MUlkOm51bWJlcjtcclxuICAgICAgICBXYXlwb2ludDJJZDpudW1iZXI7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIEhhcmJvdXIgZXh0ZW5kcyBXYXlwb2ludCB7XHJcbiAgICAgICAgTG9jYXRpb25zOiBBcnJheTxMb2NhdGlvbj47XHJcbiAgICAgICAgQWxidW06IEFsYnVtO1xyXG4gICAgICAgIEFsYnVtSWQ6IG51bWJlcjtcclxuICAgICAgICBSYXRpbmc6IG51bWJlcjtcclxuICAgICAgICBDb250ZW50OnN0cmluZztcclxuXHJcbiAgICAgICAgc3RhdGljIEdldFR5cGUoKTogc3RyaW5nIHtcclxuICAgICAgICAgICAgcmV0dXJuIFwiSGFyYm91clwiO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgUGVyc29uIGV4dGVuZHMgRW50aXR5IHtcclxuXHJcbiAgICAgICAgTGFzdE5hbWU6IHN0cmluZztcclxuICAgICAgICBGaXJzdE5hbWU6IHN0cmluZztcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgSm9iIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBEdWVUbzogRGF0ZTtcclxuICAgICAgICBBc3NpZ25lZFRvOiBQZXJzb247XHJcbiAgICAgICAgQXNzaWduZWRUb0lkOm51bWJlcjtcclxuICAgICAgICBUaXRsZTogU3RyaW5nO1xyXG4gICAgICAgIENvbnRlbnQ6IFN0cmluZztcclxuICAgICAgICBEb25lOiBib29sZWFuO1xyXG4gICAgICAgIFN1cGVySm9iOiBKb2I7XHJcbiAgICAgICAgU3VwZXJKb2JJZDpudW1iZXI7XHJcbiAgICAgICAgVHJpcDpUcmlwO1xyXG4gICAgICAgIFRyaXBJZDpudW1iZXI7XHJcbiAgICAgICAgU3ViSm9iczpKb2JbXTtcclxuICAgIH1cclxuICAgIFxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBUcmlwIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBOYW1lOiBzdHJpbmc7XHJcbiAgICAgICAgU3RhcnQ6IERhdGU7XHJcbiAgICAgICAgRW5kOiBEYXRlO1xyXG4gICAgICAgIENvbnRlbnQ6IHN0cmluZztcclxuICAgICAgICBUYWNrczogQXJyYXk8VGFjaz47XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIEFkZHJlc3MgZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIFN0cmVldDogc3RyaW5nO1xyXG4gICAgICAgIFppcDogc3RyaW5nO1xyXG4gICAgICAgIFRvd246IHN0cmluZztcclxuICAgICAgICBDb21tZW50OiBzdHJpbmc7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIEltYWdlIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBBbGJ1bUlkOiBudW1iZXI7XHJcbiAgICAgICAgUGF0aDogc3RyaW5nO1xyXG4gICAgICAgIENvbW1lbnQ6IHN0cmluZztcclxuICAgICAgICBIZWlnaHQ6IG51bWJlcjtcclxuICAgICAgICBXaWR0aDogbnVtYmVyO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBbGJ1bSBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgU3RhbmRBbG9uZTogYm9vbGVhbjtcclxuICAgICAgICBDb21tZW50OiBzdHJpbmc7XHJcbiAgICAgICAgSW1hZ2VzOiBBcnJheTxJbWFnZT47XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFdheXBvaW50VGFjayB7XHJcbiAgICAgICAgV2F5cG9pbnRJZDogbnVtYmVyO1xyXG4gICAgICAgIEluZGV4OiBudW1iZXI7XHJcbiAgICAgICAgVGFja0lkOiBudW1iZXI7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFRhY2sgZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIFN0YXJ0OiBEYXRlO1xyXG4gICAgICAgIEVuZDogRGF0ZTtcclxuICAgICAgICBXYXlwb2ludHM6IEFycmF5PFdheXBvaW50VGFjaz47XHJcbiAgICAgICAgQ3JldzogQXJyYXk8UGVyc29uPjtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgTG9jYXRpb24gZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgICBIYXJib3VySWQ6bnVtYmVyO1xyXG4gICAgICAgIFdlYnNpdGU6c3RyaW5nO1xyXG4gICAgICAgIE5hbWU6c3RyaW5nO1xyXG4gICAgICAgIFJhdGluZzpudW1iZXI7XHJcbiAgICAgICAgVHlwZTogc3RyaW5nO1xyXG4gICAgICAgIEFkZHJlc3NJZDpudW1iZXI7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCkge1xyXG4gICAgICAgICAgICAgcmV0dXJuIFwiTG9jYXRpb25cIjtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFJlc3RhdXJhbnQgZXh0ZW5kcyBMb2NhdGlvbnsgXHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCkge1xyXG4gICAgICAgICAgICAgcmV0dXJuIFwiUmVzdGF1cmFudFwiO1xyXG4gICAgICAgIH19XHJcbiAgICBleHBvcnQgY2xhc3MgU3VwZXJtYXJrZXQgZXh0ZW5kcyBMb2NhdGlvbiB7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCkge1xyXG4gICAgICAgICAgICAgcmV0dXJuIFwiU3VwZXJtYXJrZXRcIjtcclxuICAgICAgICB9fVxyXG59Il0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
