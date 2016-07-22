module ClientModel {

    import SEntity = ServerModel.Entity

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
        Album = CreateObservable<Album>({
            ForeignKey: (t: this) => t.AlbumId
        });
        InsertDate = ko.observable<number>(Date as any);
        UpdateDate = ko.observable<number>(Date as any);

        private static clientIdCounter = 0;
        private static entityDb = {};

        ClientId = Entity.clientIdCounter++;

        protected ServerApi = ServerApi.GetApi(this);

        DeleteOnServer(): JQueryPromise<SEntity> {
            return this.ServerApi.Delete(this.Id()).done(() => { this.removeFromContext(); });
        };

        SaveToServer(): JQueryPromise<SEntity> {
            if (this.Id() === undefined)
                return this.ServerApi.Create(this.ConvertToServerEntity())
                    .done(data => {
                        this.savedStates=[];
                        this.LoadFromServerEntity(data);
                        this.addToContext();
                    });
            return this.ServerApi.Update(this.ConvertToServerEntity())
                .done((data) => {
                    this.savedStates=[];
                    this.LoadFromServerEntity(data);
                });;
        }

        private LoadNavigationProperties(): void {
            for (let propName in this.GetObservableNames()) {
                const prop = <KnockoutObservable<any>>this[propName];
                //if (prop.)
            }
        }

        private addToContext() {
            if (this.Context().indexOf(this) !== -1)
                return;
            this.Context().push(this);
        }

        private removeFromContext() {
            this.Context().remove(this);
        }
        
        LoadFromServerEntity(serverEntity: SEntity): this {
            if (serverEntity.ProcessOnServer === false)
                return this;
            for (let prop of this.GetObservableNames()) {
                const sVal = serverEntity[prop];
                if (sVal !== undefined && sVal !== null) {
                    if (sVal instanceof Array) {
                        for (let obj of sVal) {
                            const entity = Entity.entityDb[obj.ClientId.toString()];
                            if (entity !== undefined)
                                entity.LoadFromServerEntity(obj);
                        }
                    } else {
                        const cVal = this[prop]();
                        if (cVal instanceof Entity)
                            cVal.LoadFromServerEntity(sVal);
                        else
                            this[prop](sVal);
                    }
                }
            }
            return this;
        }

        ConvertToServerEntity(idOnly: boolean = false): SEntity {
            if (idOnly) {
                return { Id: this.Id(), ProcessOnServer: false };
            }
            const isUpdate = this.Id() !== undefined;
            const serverEntity = { ClientId: this.ClientId };
            const entity = this;
            for (let propName of this.GetObservableNames()) {
                const prop = entity[propName];
                const val = prop();
                if (val !== undefined) {
                    if (val instanceof Array) {
                        const arr = new Array();
                        for (let elem of val as Entity[]) {
                            if (elem.Id() === undefined ||
                                isUpdate && prop.UpdateTransferMode === TransferMode.Include ||
                                !isUpdate && prop.AddTransferMode === TransferMode.Include)
                                arr.push(elem.ConvertToServerEntity());
                        }
                        serverEntity[propName] = arr;
                    } else
                        serverEntity[propName] = val instanceof Entity
                            ? (val.Id() === undefined ||
                                isUpdate && prop.UpdateTransferMode === TransferMode.Include ||
                                !isUpdate && prop.AddTransferMode === TransferMode.Include
                                ? val.ConvertToServerEntity()
                                : undefined)
                            : val;
                }
            }
            return serverEntity as any;
        }

        CopyTo(entity: this) {
            entity.Id(this.Id());
        }

        private savedStates=new Array();

        SaveState= (alreadySavedEntities = new Array<Entity>()) => {
            const savedState = new Object();
            if (alreadySavedEntities.indexOf(this) !== -1)
                return;
            alreadySavedEntities.push(this);
            for (let prop of this.GetObservableNames()) {
                let val = ko.unwrap(this[prop]);
                if (val instanceof Array) {
                    val = val.slice(0);
                    for (let elem of val) {
                        if (elem instanceof Entity)
                            elem.SaveState(alreadySavedEntities);
                    }
                } else if (val instanceof Entity)
                    val.SaveState(alreadySavedEntities);
                savedState[prop] = val;
            }
            this.savedStates.push(savedState);
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

        RevertState = (ignoreError: boolean = false, alreadyRevertedEntities = new Array<Entity>()) => {
            if (alreadyRevertedEntities.indexOf(this) !== -1)
                return;
            alreadyRevertedEntities.push(this);
            if (this.savedStates.length=== 0)
                if (ignoreError)
                    return;
                else
                    throw "No saved state";
            for (let prop of this.GetObservableNames()) {
                const val = this.savedStates[0][prop];
                //this.savedState[prop] = val;
                this[prop](val);
                if (val instanceof Entity)
                    val.RevertState(ignoreError, alreadyRevertedEntities);
                else if (val instanceof Array)
                    for (let elem of val) {
                        if (elem instanceof Entity)
                            elem.RevertState(ignoreError, alreadyRevertedEntities);
                    }
            }
            this.savedStates.shift();
        }

        OnSaving(): boolean {
            return true;
        }

        OnSaved(): boolean {
            return true;
        }

        OnDeleted(): boolean {
            return true;
        }

        Id = ko.observable<number>();

        abstract Context(): KnockoutObservableArray<this>;
    }

    export class Album extends Entity {
        Images = ko.observableArray<Image>();

        Context() {
            return mapViewModel.Albums as any;
        }
    }

    export class Person extends Entity {
        LastName = ko.observable<string>();
        FirstName = ko.observable<string>();
        FullName = ko.computed(() => this.FirstName() + " " + this.LastName());

        Context() {
            return mapViewModel.Persons as any;
        }

    }

    export class Job extends Entity {
        DueTo = ko.observable<Date>(Date as any);
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

        Context() {
            return mapViewModel.Jobs as any;
        }
    }

    export class Waypoint extends Entity {
        constructor(latLng: L.LatLng, markerType: MarkerType, map: L.mapbox.Map);
        constructor(markerType: MarkerType, map: L.mapbox.Map);
        constructor(latLng: L.LatLng | MarkerType,
            markerType: MarkerType | L.mapbox.Map,
            protected Map?: L.mapbox.
            Map) {
            super();
            if (Map === undefined) {
                if (typeof markerType == "number") {
                    Map = ((latLng as any) as L.mapbox.Map);
                    latLng = (markerType as MarkerType);
                    markerType = Map;
                }
                this.Map = ((markerType) as L.mapbox.Map);
                this.LatLng = new L.LatLng(0, 0);
                markerType = (latLng as MarkerType);
            } else {
                this.Latitude((latLng as L.LatLng).lat);
                this.Longitude((latLng as L.LatLng).lng);
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
            if (this.markerType !== MarkerType.Dummy)
                return;
            this.marker.setOpacity(1);
            var w1 = this.polylines[0].Waypoints[0];
            var w2 = this.polylines[0].Waypoints[1];
            this.markerType = MarkerType.Waypoint;
            splitPolyline(this.polylines[0]);
            this.SaveToServer()
                .done((w) => {
                    const wCA = ServerApi.WaypointConnections;
                    wCA.Disconnect(w1.Id(), w2.Id());
                    wCA.Connect(w1.Id(), w.Id);
                    wCA.Connect(w2.Id(), w.Id);
                });
        }

        IsInPolyline(polyline: L.Polyline): boolean {
            for (const wp of polyline.Waypoints) {
                if (this === wp)
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
        }

        RemoveFromPolyline(polyline: L.Polyline): boolean {
            //if (!this.IsInPolyline(polyline))
            //    return false;
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
        Wifis = ko.observableArray<Wifi>();

        Context() {
            return mapViewModel.Waypoints as any;
        }

        DeleteOnServer(): JQueryPromise<Object> {
            return super.DeleteOnServer().done(() => this.RemoveFromMap());
        }
    }

    export class Harbour extends Waypoint {

        constructor(map: L.mapbox.Map);
        constructor(latLng: L.LatLng, map: L.mapbox.Map);
        constructor(latLng: L.LatLng | L.mapbox.Map, map?: L.mapbox.Map) {
            super(latLng as L.LatLng, MarkerType.Harbour, map);
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
                if (this.marker !== undefined) {
                    const label = this.marker.getLabel();
                    if (label !== undefined) {
                        this.marker.updateLabelContent(d);
                    } else {
                        this.marker.bindLabel(d,
                        {
                            direction: "auto"
                        } as any);
                    }
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

        Context() {
            return mapViewModel.Harbours as any;
        }
    }

    export class Address extends Entity {
        Street = ko.observable<string>();
        Zip = ko.observable<string>();
        Town = ko.observable<string>();
        Comment = ko.observable<string>();

        Context() {
            return mapViewModel.Addresses as any;
        }
    }

    export class Image extends Entity {

        Path = ko.observable<string>();
        Height = ko.observable<number>();
        Width = ko.observable<number>();

        Context() {
            return mapViewModel.Images as any;
        }
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
        Album = ko.observable(new Album());

        CrewList = ko.computed({
            read: () => {
                var persons = this.Persons();
                var first = persons[0];
                if (first === undefined)
                    return "";
                if (persons.length === 1)
                    return first.FullName();
                else {
                    let list = first.FullName();
                    for (let i = 1; i < persons.length; i++) {
                        list += `, ${persons[i].FullName()}`;
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

        ConvertToServerEntity(idOnly?: boolean): SEntity {
            this.EndId(this.End().Id());
            this.StartId(this.Start().Id());
            var sEntity = super.ConvertToServerEntity(idOnly);
            const crew = new Array<ServerModel.Crew>();
            for (let person of this.Persons()) {
                crew.push({ PersonId: person.Id(), TackId: this.Id() });
            }
            (<any>sEntity).Crew = crew;
            return sEntity;
        }
    }

    export class Trip extends TackBase {
        Name = ko.observable<string>();
        Content = ko.observable<string>();
        Tacks = ko.observableArray<Tack>();
        IsDummy = ko.observable<boolean>();

        Context() {
            return mapViewModel.Trips as any;
        }
    }

    export class LogBookEntry extends TackBase {
        MotorHoursStart = ko.observable<number>();
        MotorHoursEnd = ko.observable<number>();
        LogStart = ko.observable<number>();
        LogEnd = ko.observable<number>();
        WindSpeed = ko.observable<number>();
        WindDirection = ko.observable<string>();
        SpecialOccurences = ko.observable<string>();

        Context() {
            return mapViewModel.LogBookEntries as any;
        }

    }

    export class Tack extends TackBase {
        Waypoints = ko.observableArray<ServerModel.WaypointTack>();

        CanRemoveTack = ko.computed({
            read: () => {
                if (mapViewModel.TripHelper.Editing() === undefined)
                    return false;
                const tacks = mapViewModel.TripHelper.Editing().Tacks;
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

        Context() {
            return mapViewModel.Tacks as any;
        }

    }

    export class Comment extends Entity {
        Title = ko.observable<string>();
        Content = ko.observable<string>();
        Rating = ko.observable<number>();
        ParentId = ko.observable<number>();

        Context():KnockoutObservableArray<Comment> {
            throw "not implemented";
        }

    }

    export class Location extends Entity {
        HarbourId = ko.observable<number>();
        Website = ko.observable<string>();
        Name = ko.observable<string>();
        Rating = ko.observable<number>();
        Address = ko.observable<Address>();
        AddressId = ko.observable<number>();

        Context() {
            return mapViewModel.Locations as any;
        }
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

    export class Wifi extends Entity {
        Name = ko.observable<string>();
        Password = ko.observable<string>();
        Speed = ko.observable<number>();
        Free = ko.observable<boolean>();
        HarbourId = ko.observable<number>();
        Harbour = ko.observable<Harbour>();

        Context() {
            return mapViewModel.Wifis as any;
        }
    }

    export class ContentPage extends Entity {
        Title = ko.observable<string>();
        Content = ko.observable<string>();

        Context() {
            return mapViewModel.ContentPages as any;
        }
    }

}

enum MarkerType {
    Harbour,
    Dummy,
    Waypoint,
    WeatherStation
}