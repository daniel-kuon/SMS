var WaypointConnection = ServerModel.WaypointConnection;
var WaypointTack = ServerModel.WaypointTack;
var AlbumImage = ServerModel.AlbumImage;
var HttpMethod;
(function (HttpMethod) {
    HttpMethod[HttpMethod["POST"] = 0] = "POST";
    HttpMethod[HttpMethod["GET"] = 1] = "GET";
    HttpMethod[HttpMethod["PUT"] = 2] = "PUT";
    HttpMethod[HttpMethod["DELETE"] = 3] = "DELETE";
})(HttpMethod || (HttpMethod = {}));
var AlbumImageApi = (function () {
    function AlbumImageApi(baseUrl) {
        this.baseUrl = baseUrl;
    }
    AlbumImageApi.prototype.Get = function () {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl, HttpMethod.GET));
    };
    return AlbumImageApi;
}());
var CrewApi = (function () {
    function CrewApi(baseUrl) {
        this.baseUrl = baseUrl;
    }
    CrewApi.prototype.Get = function () {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl, HttpMethod.GET));
    };
    return CrewApi;
}());
var ConnectionApi = (function () {
    function ConnectionApi(baseUrl) {
        this.baseUrl = baseUrl;
    }
    ConnectionApi.prototype.Get = function () {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody("", HttpMethod.GET));
    };
    ConnectionApi.prototype.Connect = function (id1, id2) {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(id1 + "/" + id2, HttpMethod.POST));
    };
    ConnectionApi.prototype.Disconnect = function (id1, id2) {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(id1 + "/" + id2, HttpMethod.DELETE));
    };
    return ConnectionApi;
}());
var WaypointConnectionApi = (function () {
    function WaypointConnectionApi(baseUrl) {
        this.baseUrl = baseUrl;
    }
    WaypointConnectionApi.prototype.Get = function () {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl, HttpMethod.GET));
    };
    WaypointConnectionApi.prototype.Connect = function (id1, id2) {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl + "/" + id1 + "/" + id2, HttpMethod.POST));
    };
    WaypointConnectionApi.prototype.Disconnect = function (id1, id2) {
        if (id2 !== undefined)
            return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl + "/" + id1 + "/" + id2, HttpMethod.DELETE));
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl + "/" + id1.toString(), HttpMethod.DELETE));
    };
    return WaypointConnectionApi;
}());
var ServerApi = (function () {
    function ServerApi(baseUrl) {
        this.baseUrl = baseUrl;
    }
    ServerApi.BuildRequestBody = function (url, method, data) {
        return {
            contentType: "application/json; charset=utf-8",
            dataType: "json",
            method: HttpMethod[method],
            url: url,
            data: JSON.stringify(data)
        };
    };
    ServerApi.prototype.Get = function (id) {
        if (id === undefined)
            return ServerApi.CreateRequest((ServerApi.BuildRequestBody(this.baseUrl, HttpMethod.GET)));
        else
            return ServerApi.CreateRequest((ServerApi.BuildRequestBody(this.baseUrl + "/" + id.toString(), HttpMethod.GET)));
    };
    ServerApi.prototype.Delete = function (id) {
        return ServerApi.CreateRequest((ServerApi.BuildRequestBody(this.baseUrl + "/" + id.toString(), HttpMethod.DELETE)));
    };
    ServerApi.prototype.Create = function (entity) {
        return ServerApi.CreateRequest((ServerApi.BuildRequestBody(this.baseUrl, HttpMethod.POST, entity)));
    };
    ServerApi.prototype.Update = function (entity) {
        return ServerApi.CreateRequest(ServerApi.BuildRequestBody(this.baseUrl + "/" + entity.Id.toString(), HttpMethod.PUT, entity));
    };
    ServerApi.CreateRequest = function (body) {
        ServerApi.conntectionCount(ServerApi.conntectionCount() + 1);
        return $.ajax(body).fail(function (d) {
            //alert(d);
            alert("Es gab ein Fehler beim Verarbeiten der Daten auf dem Server. Bitte überprüfe deine Eingaben und versuche er erneut.");
            console.log(d);
        }).always(function () {
            ServerApi.conntectionCount(ServerApi.conntectionCount() - 1);
        });
    };
    ServerApi.GetApi = function (type) {
        if (type instanceof ClientModel.Person)
            return ServerApi.Persons;
        if (type instanceof ClientModel.Job)
            return ServerApi.Jobs;
        if (type instanceof ClientModel.Trip)
            return ServerApi.Trips;
        if (type instanceof ClientModel.Tack)
            return ServerApi.Tacks;
        if (type instanceof ClientModel.Address)
            return ServerApi.Addresses;
        if (type instanceof ClientModel.Image)
            return ServerApi.Images;
        if (type instanceof ClientModel.Location)
            return ServerApi.Locations;
        //if (type instanceof ClientModel.Supermarket)
        //return ServerApi.Supermarkets;
        //if (type instanceof ClientModel.Restaurant)
        //return ServerApi.Restaurants;
        if (type instanceof ClientModel.Harbour)
            return ServerApi.Harbours;
        if (type instanceof ClientModel.Album)
            return ServerApi.Albums;
        if (type instanceof ClientModel.Comment)
            return ServerApi.Comments;
        if (type instanceof ClientModel.Waypoint)
            return ServerApi.Waypoints;
        if (type instanceof ClientModel.LogBookEntry)
            return ServerApi.LogBookEntries;
        if (type instanceof ClientModel.Wifi)
            return ServerApi.Wifis;
        if (type instanceof ClientModel.ContentPage)
            return ServerApi.ContentPages;
        throw ("No suitable Api found");
    };
    ServerApi.conntectionCount = ko.observable(0);
    ServerApi.connectionOpen = ko.computed(function () {
        return ServerApi.conntectionCount() > 0;
    });
    ServerApi.Persons = new ServerApi("/api/People");
    ServerApi.Jobs = new ServerApi("/api/Jobs");
    ServerApi.Trips = new ServerApi("/api/Trips");
    ServerApi.Tacks = new ServerApi("/api/Tacks");
    ServerApi.Addresses = new ServerApi("/api/Addresses");
    ServerApi.Images = new ServerApi("/api/Images");
    ServerApi.Locations = new ServerApi("api/Locations");
    //static Supermarkets = new EntityApi("/api/Supermarkets");
    //static Restaurants = new EntityApi("/api/Restaurants");
    ServerApi.Harbours = new ServerApi("/api/Harbours");
    ServerApi.Albums = new ServerApi("/api/Albums");
    ServerApi.Comments = new ServerApi("/api/Comments");
    ServerApi.Waypoints = new ServerApi("/api/Waypoints");
    ServerApi.Wifis = new ServerApi("/api/Wifis");
    ServerApi.AlbumImages = new AlbumImageApi("/api/AlbumImages");
    ServerApi.Crews = new CrewApi("/api/Crews");
    ServerApi.LogBookEntries = new ServerApi("/api/LogBookEntries");
    ServerApi.ContentPages = new ServerApi("/api/ContentPages");
    ServerApi.WaypointConnections = new WaypointConnectionApi("/api/WaypointConnections");
    return ServerApi;
}());
