module ClientModel {

    import SEntity = ServerModel.Entity
    import SWaypointConnection = ServerModel.WaypointConnection

    export interface IEntity {
        Id: KnockoutObservable<number>;
        ClientId: number;
    }

    export abstract class Entity implements IEntity {
        constructor() {
            Entity.entityDb[this.ClientId.toString()] = this;
        }

        AlbumId = ko.observable<number>();
        CommentListId = ko.observable<number>();
        Album = CreateObservable<Album>({ AddTransferMode: TransferMode.Include, UpdateTransferMode: TransferMode.Include });
        InsertDate = ko.observable<number>(<any>Date);
        UpdateDate = ko.observable<number>(<any>Date);


        private static clientIdCounter = 0;
        private static entityDb = {};

        ClientId = ++Entity.clientIdCounter;

        protected ServerApi = ServerApi.GetApi(this);

        DeleteOnServer(): JQueryPromise<SEntity> {
            return this.ServerApi.Delete(this.Id());
        };

        SaveToServer(): JQueryPromise<SEntity> {
            if (this.Id() === undefined)
                return this.ServerApi.Create(this.ConvertToServerEntity())
                    .done(data => {
                        this.savedState = undefined;
                        this.LoadFromServerEntity(data);
                    });
            return this.ServerApi.Update(this.ConvertToServerEntity())
                .done((data) => {
                    this.savedState = undefined;
                    this.LoadFromServerEntity(data);
                });;
        }

        LoadFromServerEntity(serverEntity: SEntity): this {
            for (let prop of this.GetObservableNames()) {
                const sVal = serverEntity[prop];
                if (sVal !== undefined && sVal !== null) {
                    if (sVal instanceof Array) {
                        for (let obj of sVal) {
                            var entity = Entity.entityDb[obj.ClientId.toString()];
                            if (entity !== undefined)
                                entity.LoadFromServerEntity(obj);
                        }
                    } else {
                        const cVal = this[prop]();
                        if (cVal instanceof Entity)
                            cVal.LoadFromServerEntity(sVal);
                        //else if (cVal === Date)
                        //    this[prop](new Date(sVal));
                        //else if (cVal instanceof Date)
                        //    (<Date>cVal).setTime(new Date(sVal).getTime());
                        else
                            this[prop](sVal);
                    }
                }
            }
            return this;
        }

        ConvertToServerEntity(idOnly: boolean = false): SEntity {
            const serverEntity = { ClientId: this.ClientId };
            const entity = this;
            for (let propName of this.GetObservableNames()) {
                const prop = entity[propName];
                const val = prop();
                if (val !== undefined && val !== Date) {
                    if (val instanceof Array) {
                        const arr = new Array();
                        for (let elem of val) {
                            arr.push(elem.ConvertToServerEntity());
                        }
                        serverEntity[propName] = arr;
                    }
                    //else if (val instanceof Date) {
                    //    serverEntity[propName] = (<Date>val).toJSON();
                    //}
                    else
                        serverEntity[propName] = val instanceof Entity ? val.ConvertToServerEntity() : val;
                }
            }
            return <any>serverEntity;
        }

        CopyTo(entity: this) {
            entity.Id(this.Id());
        }

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

        Id: KnockoutObservable<number> = ko.observable<number>();
    }

    export class Album extends Entity {
        Images = ko.observableArray<Image>();
    }

    export class Person extends Entity {
        LastName = ko.observable<string>();
        FirstName = ko.observable<string>();
        FullName = ko.computed(() => this.FirstName() + " " + this.LastName());

    }

    export class Job extends Entity {
        DueTo = ko.observable<Date>(<any>Date);
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
    }

    export class Waypoint extends Entity {
        constructor(latLng: L.LatLng, markerType: MarkerType, map: L.mapbox.Map);
        constructor(markerType: MarkerType, map: L.mapbox.Map);
        constructor(latLng: L.LatLng | MarkerType, markerType: MarkerType | L.mapbox.Map, protected Map?: L.mapbox.Map) {
            super();
            if (Map === undefined) {
                if (typeof markerType == "number") {
                    Map = <L.mapbox.Map><any>latLng;
                    latLng = <MarkerType>markerType;
                    markerType = Map;
                }
                this.Map = <L.mapbox.Map>(markerType);
                this.LatLng = new L.LatLng(0, 0);
                markerType = <MarkerType>latLng;
            } else {
                this.Latitude((<L.LatLng>latLng).lat);
                this.Longitude((<L.LatLng>latLng).lng);
            }
            this.LatLng = new L.LatLng(this.Latitude(), this.Longitude());
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
            if (this.marker !== undefined)
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
                    const wCA = ServerApi.WaypointConnections;
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


        SetLatLng(latLng: L.LatLng, updatePolylines = true): void {
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

        Latitude = ko.observable<number>(0);
        Longitude = ko.observable<number>(0);
        Distance = ko.observable<number>();
        Precessor = ko.observable<Waypoint>();
        RouteDistance = ko.observable<number>();
        RoutePrecessor = ko.observable<Waypoint>();
        protected popup: L.Popup;
        LatLng: L.LatLng;
        protected markerType: MarkerType | L.mapbox.Map;
        marker: L.Marker;

        Name = ko.observable<string>();
        Description = ko.observable<string>();
    }

    export class Harbour extends Waypoint {

        constructor(map: L.mapbox.Map);
        constructor(latLng: L.LatLng, map: L.mapbox.Map);
        constructor(latLng: L.LatLng | L.mapbox.Map, map?: L.mapbox.Map) {
            super(<L.LatLng>latLng, MarkerType.Harbour, map);
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

        RemoveIfHasZeroOrOnePolylines(): boolean {
            return false;
        }

        Locations = ko.observableArray<Location>();
        Rating = ko.observable<number>();
        Content = ko.observable<string>();
        Website = ko.observable<string>();
    }

    export class Address extends Entity {
        Street = ko.observable<string>();
        Zip = ko.observable<string>();
        Town = ko.observable<string>();
        Comment = ko.observable<string>();
    }

    export class Image extends Entity {

        Path = ko.observable<string>();
        Height = ko.observable<number>();
        Width = ko.observable<number>();
    }

    export abstract class TackBase extends Entity {
        StartDate = ko.observable<string>();
        EndDate = ko.observable<string>();
        Start = ko.observable<Harbour>();
        StartId = ko.observable<number>();
        EndId = ko.observable<number>();
        End = ko.observable<Harbour>();
        Persons = ko.observableArray<Person>();
        Distance = ko.observable<number>(0);

        CrewList = ko.computed({
            read: () => {
                var persons = this.Persons();
                var first = persons[0];
                if (first === undefined)
                    return "";
                if (persons.length === 1)
                    return first.FullName();
                else {
                    var list = first.FullName();
                    for (let i = 1; i < persons.length; i++) {
                        list += ", " + persons[i].FullName();
                    }
                    return list;
                }
            },
            deferEvaluation: true
            
        });

        SaillingTime = ko.computed(() => {
            const startDate = this.StartDate();
            const endDate = this.EndDate();
            if (startDate === undefined || endDate === undefined || renderTime === undefined)
                return "";
            return renderTime(new Date(startDate), new Date(endDate));
        });
    }

    export class Trip extends TackBase {
        Name = ko.observable<string>();
        Content = ko.observable<string>();
        Tacks = ko.observableArray<Tack>();
        IsDummy = ko.observable<boolean>();
    }

    export class LogBookEntry extends TackBase {
        MotorHoursStart = ko.observable<number>();
        MotorHoursEnd = ko.observable<number>();
        LogStart = ko.observable<number>();
        LogEnd = ko.observable<number>();
        SpecialOccurences = ko.observable<string>();

        }

    export class Tack extends TackBase {
        Waypoints = ko.observableArray<ServerModel.WaypointTack>();

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

        ComputePlaceholder = ko.computed((): string => {
            if (this.StartDate() !== undefined)
                return moment(this.StartDate()).format("L");
            return "";
        });

    }
    export class Comment extends Entity {
        Title = ko.observable<string>();
        Content = ko.observable<string>();
        Rating = ko.observable<number>();
        ParentId = ko.observable<number>();

    }

    export class Location extends Entity {
        HarbourId = ko.observable<number>();
        Website = ko.observable<string>();
        Name = ko.observable<string>();
        Rating = ko.observable<number>();
        Address = ko.observable<Address>();
        AddressId = ko.observable<number>();
    }

    export class Restaurant extends Location {
    }


    export class Supermarket extends Location {
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

enum MarkerType {
    Harbour,
    Dummy,
    Waypoint,
    WeatherStation
}