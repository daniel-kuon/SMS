

module ClientModel {



    import SEntity = ServerModel.Entity
    import SWaypoint = ServerModel.Waypoint
    import SWaypointConnection = ServerModel.WaypointConnection
    import SHarbour = ServerModel.Harbour
    import SPerson = ServerModel.Person
    import SJob = ServerModel.Job
    import STrip = ServerModel.Trip
    import SAddress = ServerModel.Address
    import SImage = ServerModel.Image
    import SAlbum = ServerModel.Album
    import SWaypointTack = ServerModel.WaypointTack
    import STack = ServerModel.Tack
    import SLocation = ServerModel.Location
    import SRestaurant = ServerModel.Restaurant
    import SSupermarket = ServerModel.Supermarket
    import SComment = ServerModel.Comment;
    import SCommentList = ServerModel.CommentList;

    export abstract class Entity<T extends ServerModel.Entity> {

        Id = ko.observable<number>();
        AlbumId = ko.observable<number>();
        CommentListId = ko.observable<number>();
        Album = ko.observable<Album>();
        CommentList = ko.observable<CommentList>();


        private static ClientIdCounter = 0;

        private clientId =++Entity.ClientIdCounter;

        protected ServerApi: ServerApi.Api<T>;

        DeleteOnServer(): JQueryPromise<T> {
            return this.ServerApi.Delete(this.Id());
        };

        SaveToServer(): JQueryPromise<T> {
            if (this.Id() === undefined)
                return this.ServerApi.Create(this.ConvertToServerEntity())
                    .done(data => {
                        this.savedState = undefined;
                        this.LoadFromServerEntity(data);
                    });
            return this.ServerApi.Update(this.ConvertToServerEntity())
                .done(() => {
                    this.savedState = undefined;
                });;
        }

        LoadFromServerEntity(serverEntity: T): this {
            for (let prop of this.GetObservableNames()) {
                const sVal = serverEntity[prop];
                if (sVal !== undefined && sVal !== null && !(sVal instanceof Array)) {
                    const cVal = this[prop]();
                    if (cVal instanceof Entity)
                        cVal.LoadFromServerEntity(sVal);
                    else
                        this[prop](sVal);
                }
            }
            return this;
        }

        ConvertToServerEntity(idOnly: boolean = false): T {
            const serverEntity = {ClientId:this.clientId};
            const entity = this;

            for (let propName of this.GetObservableNames()) {
                const prop = entity[propName];
                const val = prop();
                if (val !== undefined && !(val instanceof Array)) {
                    if (val instanceof Array) {
                        const arr = new Array<T>();
                        for (let elem of val) {
                            arr.push(elem.ConvertToServerEntity());
                        }
                        serverEntity[propName] = arr;
                    }
                    else
                        serverEntity[propName] = val instanceof Entity ? val.ConvertToServerEntity() : val;
                }
            }
            return <any>serverEntity;
        }

        CopyTo(entity: this) {
            entity.Id(this.Id());
        }

        abstract CreateServerEntity(): T;

        private savedState: any;

        SaveState() {
            const entity = this;
            entity.savedState = new Object();
            for (let prop of this.GetObservableNames()) {
                let val = ko.unwrap(entity[prop]);
                if (val instanceof Array) {
                    val = val.slice(0);
                    for (let elem of val) {
                        if (elem instanceof Entity)
                            elem.SaveState();
                    }
                } else if (val instanceof Entity)
                    val.SaveState();
                entity.savedState[prop] = val;
            }
        }

        protected GetObservableNames(): string[] {
            const out = new Array<string>();
            const entity = this;
            for (let prop in entity)
                if (entity.hasOwnProperty(prop))
                    if (ko.isWriteableObservable(entity[prop]) && !entity[prop].Block)
                        out.push(prop);
            return out;
        }

        RevertState(ignoreError: boolean = false) {
            if (this.savedState === undefined)
                if (ignoreError)
                    return;
                else
                    throw "No saved state";
            const entity = this;
            for (let prop of this.GetObservableNames()) {
                const val = entity.savedState[prop];
                //this.savedState[prop] = val;
                entity[prop](val);
                if (val instanceof Entity)
                    val.RevertState();
                else if (val instanceof Array)
                    for (let elem of val) {
                        if (elem instanceof Entity)
                            elem.RevertState();
                    }
            }
        }
    }

    export class Album extends Entity<SAlbum> {
        CreateServerEntity(): SAlbum {
            return new SAlbum();
        }

        Images = ko.observableArray<Image>();


        ServerApi = ServerApi.AlbumApi.GetDefault();
    }

    export class CommentList extends Entity<SCommentList> {
        CreateServerEntity(): SCommentList {
            return new SCommentList();
        }

        ServerApi = ServerApi.CommentListApi.GetDefault();
    }

    export class Person extends Entity<SPerson> {

        CreateServerEntity(): SPerson {
            return new SPerson();
        }

        ServerApi = ServerApi.PersonApi.GetDefault();
        LastName = ko.observable<string>();
        FirstName = ko.observable<string>();
        FullName = ko.computed(() => this.FirstName() + " " + this.LastName());
        
    }

    export class Job extends Entity<SJob> {
        DueTo = ko.observable<Date>();
        AssignedTo = ko.observable<Person>();
        AssignedToId = ko.observable<number>();
        Title = ko.observable<String>();
        Content = ko.observable<String>();
        Done = ko.observable<boolean>();
        SuperJob = ko.observable<Job>();
        SuperJobId = ko.observable<number>();
        Trip = ko.observable<Trip>();
        TripId = ko.observable<number>();
        SubJobs = ko.observableArray<Job>();


        CreateServerEntity(): ServerModel.Job {
            return new ServerModel.Job();
        }

        ServerApi = ServerApi.JobApi.GetDefault();
    }

    export class Waypoint extends Entity<SWaypoint> {
        constructor(latLng: L.LatLng, markerType: MarkerType, protected Map: L.mapbox.Map) {
            super();
            this.Latitude(latLng.lat);
            this.Longitude(latLng.lng);
            this.LatLng = new L.LatLng(latLng.lat, latLng.lng);
            this.Latitude.subscribe((value) => {
                if (this.LatLng.lat !== value) {
                    this.LatLng.lat = value;
                    this.Redraw();
                }
            });
            this.Longitude.subscribe((value) => {
                if (this.LatLng.lng !== value) {
                    this.LatLng.lng = value;
                    this.Redraw();
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

        Popup(content: string);
        Popup();
        Popup(content?: string) {
            if (this.popup !== undefined) {
                this.Map.removeLayer(this.popup);
                this.popup = undefined;
            }
            if (content !== undefined) {
                //this.popup = L.popup();
                this.marker.bindPopup(content); //.openPopup();


            }
        }

        Show(highlight: boolean = false): void {
            this.marker.setOpacity(this.marker.Waypoint.IsDummy() ? 0.5 : 1);
            if (highlight)
                $(this.marker._icon).addClass("expose");
        }

        Hide(): void {
            this.marker.setOpacity(0.1);
            $(this.marker._icon).removeClass("expose");
        }

        Redraw(updatePolylines = true): void {
            this.marker.setLatLng(this.LatLng);
            if (updatePolylines)
                for (let i = 0; i < this.polylines.length; i++)
                    redrawPolyline(this.polylines[i]);
        }

        GetConnectedWaypoints(): Waypoint[] {
            const ret = new Array<Waypoint>();
            for (let line of this.polylines) {
                for (let waypoint of line.Waypoints) {
                    if (waypoint !== this)
                        ret.push(waypoint);
                }
            }
            return ret.sort((w1, w2) => {
                return w1.LatLng.distanceTo(this.LatLng) - w2.LatLng.distanceTo(this.LatLng);
            });
        }

        convertFromDummyHandle() {
            this.marker.setOpacity(1);
            var w1 = this.polylines[0].Waypoints[0];
            var w2 = this.polylines[0].Waypoints[1];
            splitPolyline(this.polylines[0]);
            this.markerType = MarkerType.Waypoint;
            this.SaveToServer()
                .done((w) => {
                    const wCA = ServerApi.WaypointConnectionApi.GetDefault();
                    wCA.Disconnect(w1.Id(), w2.Id());
                    wCA.Connect(w1.Id(), w.Id);
                    wCA.Connect(w2.Id(), w.Id);
                });
        }

        IsInPolyline(polyline: L.Polyline): boolean {
            for (const currentPolyline of this.polylines) {
                if (polyline === currentPolyline)
                    return true;
            }
            return false;
        }

        RemoveFromMap() {
            if (this.markerType !== MarkerType.Dummy)
                for (let polyline of this.polylines)
                    removePolyline(polyline);
            this.Map.removeLayer(this.marker);
        }

        AddToPolyline(polyline: L.Polyline): boolean {
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
        }

        RemoveFromPolyline(polyline: L.Polyline): boolean {
            if (!this.IsInPolyline(polyline))
                return false;
            removeFromArray(polyline.Waypoints, this);
            removeFromArray(this.polylines, polyline);
            removeFromArray(this.LatLng.Polylines, polyline);
            removeFromArray(polyline.getLatLngs(), this.LatLng);
            polyline.redraw();
            return true;
        }

        RemoveIfHasZeroOrOnePolylines(): boolean {
            return true;
        }

        WaypointNumber = ko.observable<number>();
        private polylines = new Array<L.Polyline>();


        SetLatLng(latLng: L.LatLng, updatePolylines=true): void {
            this.LatLng.lat = latLng.lat;
            this.LatLng.lng = latLng.lng;
            this.Latitude(latLng.lat);
            this.Longitude(latLng.lng);
            this.Redraw(updatePolylines);
        }

        CenterOnMap() {
            this.Map.setView(this.LatLng);
        }

        IsDummy(): boolean {
            return this.markerType === MarkerType.Dummy;
        }

        Latitude = ko.observable<number>();
        Longitude = ko.observable<number>();
        Distance = ko.observable<number>();
        Precessor = ko.observable<Waypoint>();
        RouteDistance = ko.observable<number>();
        RoutePrecessor = ko.observable<Waypoint>();
        protected popup: L.Popup;
        LatLng: L.LatLng;
        protected markerType: MarkerType;
        marker: L.Marker;

        Name = ko.observable<string>();
        Description = ko.observable<string>();

        CreateServerEntity(): ServerModel.Waypoint {
            return new ServerModel.Waypoint();
        }

        ServerApi = ServerApi.WaypointApi.GetDefault();
    }

    export class Harbour extends Waypoint {

        constructor(name: string, latLng: L.LatLng, map: L.mapbox.Map) {
            super(latLng, MarkerType.Harbour, map);
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
            this.Name.subscribe((d) => {
                const label = this.marker.getLabel();
                if (label !== undefined) {
                    this.marker.updateLabelContent(d);
                } else {
                    this.marker.bindLabel(d,
                        {
                            direction: "auto"
                        } as any);
                }
            });
        }

        Album = ko.observable(new Album());
        CommentList = ko.observable(new CommentList());

        RemoveIfHasZeroOrOnePolylines(): boolean {
            return false;
        }

        CreateServerEntity(): ServerModel.Harbour {
            return new ServerModel.Harbour();
        }

        ServerApi = ServerApi.HarbourApi.GetDefault();

        Locations = ko.observableArray<Location>();
        Rating = ko.observable<number>();
        Content = ko.observable<string>();
        Website = ko.observable<string>();
    }

    export class Trip extends Entity<STrip> {
        Name = ko.observable<string>();
        Start = ko.observable<Date>();
        End = ko.observable<Date>();
        Content = ko.observable<string>();
        Tacks = ko.observableArray<Tack>();

        ServerApi = ServerApi.TripApi.GetDefault();

        CreateServerEntity(): ServerModel.Trip {
            return new ServerModel.Trip();
        }
    }

    export class Address extends Entity<SAddress> {
        Street = ko.observable<string>();
        Zip = ko.observable<string>();
        Town = ko.observable<string>();
        Comment = ko.observable<string>();

        CreateServerEntity(): SAddress {
            return new SAddress;
        }

        ServerApi = ServerApi.AddressApi.GetDefault();
    }

    export class Image extends Entity<SImage> {

        ParentAlbumId = ko.observable<number>();
        Path = ko.observable<string>();
        Height = ko.observable<number>();
        Width = ko.observable<number>();
        Width2 = ko.observable<number>();

        CreateServerEntity(): ServerModel.Image {
            return new ServerModel.Image();
        }

        ServerApi = ServerApi.ImageApi.GetDefault();
    }

    export class Tack extends Entity<STack> {
        StartDate = ko.observable<Date>();
        EndDate = ko.observable<Date>();
        Start = ko.observable<Harbour>();
        End = ko.observable<Harbour>();
        Waypoints = ko.observableArray<ServerModel.WaypointTack>();
        Crew = ko.observableArray<Person>();
        Distance = ko.observable<number>();

        CreateServerEntity(): ServerModel.Tack {
            return new ServerModel.Tack();
        }

        ServerApi = ServerApi.TackApi.GetDefault();



        CanRemoveTack = ko.computed({
            read: () => {
                if (mapViewModel.SelectedTrip() === undefined)
                    return false;
                const tacks = mapViewModel.SelectedTrip().Tacks;
                const index = tacks.indexOf(this);
                const prevTack = tacks()[index - 1];
                const nextTack = tacks()[index + 1];
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

        ComputePlaceholder=ko.computed(():string=> {
            if (this.StartDate() !== undefined)
                return moment(this.StartDate()).format("L");
            return "";
        });

    }

    export class Location extends Entity<SLocation> {
        HarbourId = ko.observable<number>();
        Website = ko.observable<string>();
        Name = ko.observable<string>();
        Rating = ko.observable<number>();
        Address = ko.observable<Address>();
        AddressId = ko.observable<number>();

        CreateServerEntity(): ServerModel.Location {
            return new ServerModel.Location();
        }

        ServerApi = ServerApi.LocationApi.GetDefault();
    }

    export class Restaurant extends Location {

        CreateServerEntity(): ServerModel.Restaurant {
            return new ServerModel.Restaurant();
        }

        ServerApi = ServerApi.RestaurantApi.GetDefault();
    }


    export class Supermarket extends Location {

        CreateServerEntity(): ServerModel.Supermarket {
            return new ServerModel.Supermarket();
        }

        ServerApi = ServerApi.SupermarketApi.GetDefault();
    }


    export class WaypointDistance {
        constructor(public Precessor: Waypoint,
            public Waypoint: Waypoint,
            public Distance: number,
            calculaterdWaypoints: Waypoint[],
            calculateRoute: boolean) {
            this.LatLng = Waypoint.LatLng;
            calculaterdWaypoints.push(Waypoint);
            for (let wp of Waypoint.GetConnectedWaypoints()) {
                if (calculaterdWaypoints.indexOf(wp) === -1)
                    this.ConnectedWayPoints.push(wp);
            }
            if (calculateRoute) {
                Waypoint.RoutePrecessor(Precessor);
                Waypoint.RouteDistance(Distance);
            } else {
                Waypoint.Precessor(Precessor);
                Waypoint.Distance(Distance);
            }
        }

        ConnectedWayPoints = new Array<Waypoint>();
        ConnectedDistances = new Array<WaypointDistance>();
        LatLng: L.LatLng;
    }

}

interface KnockoutObservable<T> extends KnockoutSubscribable<T>, KnockoutObservableFunctions<T> {
    Block: boolean;
}

enum MarkerType {
    Harbour,
    Dummy,
    Waypoint,
    WeatherStation
}