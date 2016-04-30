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
    var AlbumImage = (function () {
        function AlbumImage() {
        }
        return AlbumImage;
    }());
    ServerModel.AlbumImage = AlbumImage;
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

//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIlNlcnZlck1vZGVsLnRzIl0sIm5hbWVzIjpbXSwibWFwcGluZ3MiOiI7Ozs7O0FBQUEsSUFBTyxXQUFXLENBbUlqQjtBQW5JRCxXQUFPLFdBQVcsRUFBQyxDQUFDO0lBQ2hCO1FBQUE7UUFJQSxDQUFDO1FBQUQsYUFBQztJQUFELENBSkEsQUFJQyxJQUFBO0lBSnFCLGtCQUFNLFNBSTNCLENBQUE7SUFFRDtRQUFpQywrQkFBTTtRQUF2QztZQUFpQyw4QkFBTTtRQUFDLENBQUM7UUFBRCxrQkFBQztJQUFELENBQXhDLEFBQXlDLENBQVIsTUFBTSxHQUFFO0lBQTVCLHVCQUFXLGNBQWlCLENBQUE7SUFDekM7UUFBNkIsMkJBQU07UUFBbkM7WUFBNkIsOEJBQU07UUFBQyxDQUFDO1FBQUQsY0FBQztJQUFELENBQXBDLEFBQXFDLENBQVIsTUFBTSxHQUFFO0lBQXhCLG1CQUFPLFVBQWlCLENBQUE7SUFFckM7UUFBOEIsNEJBQU07UUFBcEM7WUFBOEIsOEJBQU07UUFXcEMsQ0FBQztRQUhVLGdCQUFPLEdBQWQ7WUFDSSxNQUFNLENBQUMsVUFBVSxDQUFDO1FBQ3RCLENBQUM7UUFDTCxlQUFDO0lBQUQsQ0FYQSxBQVdDLENBWDZCLE1BQU0sR0FXbkM7SUFYWSxvQkFBUSxXQVdwQixDQUFBO0lBRUQ7UUFBQTtRQUdBLENBQUM7UUFBRCx5QkFBQztJQUFELENBSEEsQUFHQyxJQUFBO0lBSFksOEJBQWtCLHFCQUc5QixDQUFBO0lBRUQ7UUFBNkIsMkJBQVE7UUFBckM7WUFBNkIsOEJBQVE7UUFVckMsQ0FBQztRQUhVLGVBQU8sR0FBZDtZQUNJLE1BQU0sQ0FBQyxTQUFTLENBQUM7UUFDckIsQ0FBQztRQUNMLGNBQUM7SUFBRCxDQVZBLEFBVUMsQ0FWNEIsUUFBUSxHQVVwQztJQVZZLG1CQUFPLFVBVW5CLENBQUE7SUFFRDtRQUE0QiwwQkFBTTtRQUFsQztZQUE0Qiw4QkFBTTtRQUlsQyxDQUFDO1FBQUQsYUFBQztJQUFELENBSkEsQUFJQyxDQUoyQixNQUFNLEdBSWpDO0lBSlksa0JBQU0sU0FJbEIsQ0FBQTtJQUVEO1FBQXlCLHVCQUFNO1FBQS9CO1lBQXlCLDhCQUFNO1FBWS9CLENBQUM7UUFBRCxVQUFDO0lBQUQsQ0FaQSxBQVlDLENBWndCLE1BQU0sR0FZOUI7SUFaWSxlQUFHLE1BWWYsQ0FBQTtJQUdEO1FBQTBCLHdCQUFNO1FBQWhDO1lBQTBCLDhCQUFNO1FBTWhDLENBQUM7UUFBRCxXQUFDO0lBQUQsQ0FOQSxBQU1DLENBTnlCLE1BQU0sR0FNL0I7SUFOWSxnQkFBSSxPQU1oQixDQUFBO0lBRUQ7UUFBNkIsMkJBQU07UUFBbkM7WUFBNkIsOEJBQU07UUFLbkMsQ0FBQztRQUFELGNBQUM7SUFBRCxDQUxBLEFBS0MsQ0FMNEIsTUFBTSxHQUtsQztJQUxZLG1CQUFPLFVBS25CLENBQUE7SUFFRDtRQUEyQix5QkFBTTtRQUFqQztZQUEyQiw4QkFBTTtRQU1qQyxDQUFDO1FBQUQsWUFBQztJQUFELENBTkEsQUFNQyxDQU4wQixNQUFNLEdBTWhDO0lBTlksaUJBQUssUUFNakIsQ0FBQTtJQUVEO1FBQTJCLHlCQUFNO1FBQWpDO1lBQTJCLDhCQUFNO1FBSWpDLENBQUM7UUFBRCxZQUFDO0lBQUQsQ0FKQSxBQUlDLENBSjBCLE1BQU0sR0FJaEM7SUFKWSxpQkFBSyxRQUlqQixDQUFBO0lBRUQ7UUFBQTtRQUdBLENBQUM7UUFBRCxpQkFBQztJQUFELENBSEEsQUFHQyxJQUFBO0lBSFksc0JBQVUsYUFHdEIsQ0FBQTtJQUVEO1FBQUE7UUFJQSxDQUFDO1FBQUQsbUJBQUM7SUFBRCxDQUpBLEFBSUMsSUFBQTtJQUpZLHdCQUFZLGVBSXhCLENBQUE7SUFFRDtRQUEwQix3QkFBTTtRQUFoQztZQUEwQiw4QkFBTTtRQUtoQyxDQUFDO1FBQUQsV0FBQztJQUFELENBTEEsQUFLQyxDQUx5QixNQUFNLEdBSy9CO0lBTFksZ0JBQUksT0FLaEIsQ0FBQTtJQUVEO1FBQThCLDRCQUFNO1FBQXBDO1lBQThCLDhCQUFNO1FBV3BDLENBQUM7UUFIVSxnQkFBTyxHQUFkO1lBQ0ssTUFBTSxDQUFDLFVBQVUsQ0FBQztRQUN2QixDQUFDO1FBQ0wsZUFBQztJQUFELENBWEEsQUFXQyxDQVg2QixNQUFNLEdBV25DO0lBWFksb0JBQVEsV0FXcEIsQ0FBQTtJQUVEO1FBQWdDLDhCQUFRO1FBQXhDO1lBQWdDLDhCQUFRO1FBSW5DLENBQUM7UUFGSyxrQkFBTyxHQUFkO1lBQ0ssTUFBTSxDQUFDLFlBQVksQ0FBQztRQUN6QixDQUFDO1FBQUEsaUJBQUM7SUFBRCxDQUpMLEFBSU0sQ0FKMEIsUUFBUSxHQUlsQztJQUpPLHNCQUFVLGFBSWpCLENBQUE7SUFDTjtRQUFpQywrQkFBUTtRQUF6QztZQUFpQyw4QkFBUTtRQUlwQyxDQUFDO1FBRkssbUJBQU8sR0FBZDtZQUNLLE1BQU0sQ0FBQyxhQUFhLENBQUM7UUFDMUIsQ0FBQztRQUFBLGtCQUFDO0lBQUQsQ0FKTCxBQUlNLENBSjJCLFFBQVEsR0FJbkM7SUFKTyx1QkFBVyxjQUlsQixDQUFBO0FBQ1YsQ0FBQyxFQW5JTSxXQUFXLEtBQVgsV0FBVyxRQW1JakIiLCJmaWxlIjoiU2VydmVyTW9kZWwuanMiLCJzb3VyY2VzQ29udGVudCI6WyJtb2R1bGUgU2VydmVyTW9kZWwge1xyXG4gICAgZXhwb3J0IGFic3RyYWN0IGNsYXNzIEVudGl0eSB7XHJcblxyXG4gICAgICAgIElkOiBudW1iZXI7XHJcblxyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBDb21tZW50TGlzdCBleHRlbmRzIEVudGl0eXt9XHJcbiAgICBleHBvcnQgY2xhc3MgQ29tbWVudCBleHRlbmRzIEVudGl0eXt9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFdheXBvaW50IGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBXYXlwb2ludE51bWJlcjogbnVtYmVyO1xyXG4gICAgICAgIE5hbWU6IHN0cmluZztcclxuICAgICAgICBEZXNjcmlwdGlvbjogc3RyaW5nO1xyXG4gICAgICAgIExhdGl0dWRlOiBudW1iZXI7XHJcbiAgICAgICAgTG9uZ2l0dWRlOiBudW1iZXI7XHJcbiAgICAgICAgVHlwZTogc3RyaW5nO1xyXG5cclxuICAgICAgICBzdGF0aWMgR2V0VHlwZSgpOiBzdHJpbmcge1xyXG4gICAgICAgICAgICByZXR1cm4gXCJXYXlwb2ludFwiO1xyXG4gICAgICAgIH1cclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgV2F5cG9pbnRDb25uZWN0aW9uIHtcclxuICAgICAgICBXYXlwb2ludDFJZDpudW1iZXI7XHJcbiAgICAgICAgV2F5cG9pbnQySWQ6bnVtYmVyO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBIYXJib3VyIGV4dGVuZHMgV2F5cG9pbnQge1xyXG4gICAgICAgIExvY2F0aW9uczogQXJyYXk8TG9jYXRpb24+O1xyXG4gICAgICAgIEFsYnVtOiBBbGJ1bTtcclxuICAgICAgICBBbGJ1bUlkOiBudW1iZXI7XHJcbiAgICAgICAgUmF0aW5nOiBudW1iZXI7XHJcbiAgICAgICAgQ29udGVudDpzdHJpbmc7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCk6IHN0cmluZyB7XHJcbiAgICAgICAgICAgIHJldHVybiBcIkhhcmJvdXJcIjtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFBlcnNvbiBleHRlbmRzIEVudGl0eSB7XHJcblxyXG4gICAgICAgIExhc3ROYW1lOiBzdHJpbmc7XHJcbiAgICAgICAgRmlyc3ROYW1lOiBzdHJpbmc7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIEpvYiBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgRHVlVG86IERhdGU7XHJcbiAgICAgICAgQXNzaWduZWRUbzogUGVyc29uO1xyXG4gICAgICAgIEFzc2lnbmVkVG9JZDpudW1iZXI7XHJcbiAgICAgICAgVGl0bGU6IFN0cmluZztcclxuICAgICAgICBDb250ZW50OiBTdHJpbmc7XHJcbiAgICAgICAgRG9uZTogYm9vbGVhbjtcclxuICAgICAgICBTdXBlckpvYjogSm9iO1xyXG4gICAgICAgIFN1cGVySm9iSWQ6bnVtYmVyO1xyXG4gICAgICAgIFRyaXA6VHJpcDtcclxuICAgICAgICBUcmlwSWQ6bnVtYmVyO1xyXG4gICAgICAgIFN1YkpvYnM6Sm9iW107XHJcbiAgICB9XHJcbiAgICBcclxuXHJcbiAgICBleHBvcnQgY2xhc3MgVHJpcCBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgTmFtZTogc3RyaW5nO1xyXG4gICAgICAgIFN0YXJ0OiBEYXRlO1xyXG4gICAgICAgIEVuZDogRGF0ZTtcclxuICAgICAgICBDb250ZW50OiBzdHJpbmc7XHJcbiAgICAgICAgVGFja3M6IEFycmF5PFRhY2s+O1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBZGRyZXNzIGV4dGVuZHMgRW50aXR5IHtcclxuICAgICAgICBTdHJlZXQ6IHN0cmluZztcclxuICAgICAgICBaaXA6IHN0cmluZztcclxuICAgICAgICBUb3duOiBzdHJpbmc7XHJcbiAgICAgICAgQ29tbWVudDogc3RyaW5nO1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBJbWFnZSBleHRlbmRzIEVudGl0eSB7XHJcbiAgICAgICAgQWxidW1JZDogbnVtYmVyO1xyXG4gICAgICAgIFBhdGg6IHN0cmluZztcclxuICAgICAgICBDb21tZW50OiBzdHJpbmc7XHJcbiAgICAgICAgSGVpZ2h0OiBudW1iZXI7XHJcbiAgICAgICAgV2lkdGg6IG51bWJlcjtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgQWxidW0gZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIFN0YW5kQWxvbmU6IGJvb2xlYW47XHJcbiAgICAgICAgQ29tbWVudDogc3RyaW5nO1xyXG4gICAgICAgIEltYWdlczogQXJyYXk8SW1hZ2U+O1xyXG4gICAgfVxyXG5cclxuICAgIGV4cG9ydCBjbGFzcyBBbGJ1bUltYWdlIHtcclxuICAgICAgICBJbWFnZUlkOiBudW1iZXI7XHJcbiAgICAgICAgQWxidW1JZDpudW1iZXI7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFdheXBvaW50VGFjayB7XHJcbiAgICAgICAgV2F5cG9pbnRJZDogbnVtYmVyO1xyXG4gICAgICAgIEluZGV4OiBudW1iZXI7XHJcbiAgICAgICAgVGFja0lkOiBudW1iZXI7XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFRhY2sgZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgIFN0YXJ0OiBEYXRlO1xyXG4gICAgICAgIEVuZDogRGF0ZTtcclxuICAgICAgICBXYXlwb2ludHM6IEFycmF5PFdheXBvaW50VGFjaz47XHJcbiAgICAgICAgQ3JldzogQXJyYXk8UGVyc29uPjtcclxuICAgIH1cclxuXHJcbiAgICBleHBvcnQgY2xhc3MgTG9jYXRpb24gZXh0ZW5kcyBFbnRpdHkge1xyXG4gICAgICAgICBIYXJib3VySWQ6bnVtYmVyO1xyXG4gICAgICAgIFdlYnNpdGU6c3RyaW5nO1xyXG4gICAgICAgIE5hbWU6c3RyaW5nO1xyXG4gICAgICAgIFJhdGluZzpudW1iZXI7XHJcbiAgICAgICAgVHlwZTogc3RyaW5nO1xyXG4gICAgICAgIEFkZHJlc3NJZDpudW1iZXI7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCkge1xyXG4gICAgICAgICAgICAgcmV0dXJuIFwiTG9jYXRpb25cIjtcclxuICAgICAgICB9XHJcbiAgICB9XHJcblxyXG4gICAgZXhwb3J0IGNsYXNzIFJlc3RhdXJhbnQgZXh0ZW5kcyBMb2NhdGlvbnsgXHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCkge1xyXG4gICAgICAgICAgICAgcmV0dXJuIFwiUmVzdGF1cmFudFwiO1xyXG4gICAgICAgIH19XHJcbiAgICBleHBvcnQgY2xhc3MgU3VwZXJtYXJrZXQgZXh0ZW5kcyBMb2NhdGlvbiB7XHJcblxyXG4gICAgICAgIHN0YXRpYyBHZXRUeXBlKCkge1xyXG4gICAgICAgICAgICAgcmV0dXJuIFwiU3VwZXJtYXJrZXRcIjtcclxuICAgICAgICB9fVxyXG59Il0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
