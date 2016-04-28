module ServerApi {
    import Entity = ServerModel.Entity;
    import Harbour = ServerModel.Harbour;
    import Waypoint = ServerModel.Waypoint;
    import Person = ServerModel.Person;
    import Job = ServerModel.Job;
    import Trip = ServerModel.Trip;
    import WaypointConnection = ServerModel.WaypointConnection;
    import Tack = ServerModel.Tack;
    import Address = ServerModel.Address;
    import Image = ServerModel.Image;
    import Album = ServerModel.Album;
    import WaypointTack = ServerModel.WaypointTack;
    import Location = ServerModel.Location;
    import Restaurant = ServerModel.Restaurant;
    import Supermarket = ServerModel.Supermarket;
    import Comment = ServerModel.Comment;
    import CommentList = ServerModel.CommentList;

    export enum HttpMethod {
        POST,
        GET,
        PUT,
        DELETE
    }

    export abstract class Api<T extends Entity> {
        static conntectionCount = ko.observable(0);

        static connectionOpen=ko.computed(() => {
            return Api.conntectionCount() > 0;
        });

        constructor(public baseUrl: string) {

        }

        BuildRequestBody(url: string, method: HttpMethod, data?: any): JQueryAjaxSettings {
            return {
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                method: HttpMethod[method],
                url: this.baseUrl + "/" + url,
                data: JSON.stringify(data)
            };
        }

        Get(): JQueryPromise<T[]>;
        Get(id: number): JQueryPromise<T>;
        Get(id?: number): any {
            if (id === undefined)
                return this.CreateRequest<T[]>((this.BuildRequestBody("", HttpMethod.GET)));
            else
                return this.CreateRequest<T>((this.BuildRequestBody(id.toString(), HttpMethod.GET)));
        }

        Delete(id: number): JQueryPromise<T> {
            return this.CreateRequest<T>((this.BuildRequestBody(id.toString(), HttpMethod.DELETE)));
        }

        Create(entity: T): JQueryPromise<T> {
            return this.CreateRequest<T>((this.BuildRequestBody("", HttpMethod.POST, entity)));
        }

        Update(entity: T): JQueryPromise<T> {
            return this.CreateRequest<T>(this.BuildRequestBody(entity.Id.toString(), HttpMethod.PUT, entity));
        }

        protected CreateRequest<T>(body: JQueryAjaxSettings): JQueryPromise<T> {
            Api.conntectionCount(Api.conntectionCount() + 1);
            return $.ajax(body).fail(d => {
                alert(d);
                console.log(d);
            }).always(() => {
                Api.conntectionCount(Api.conntectionCount() - 1);
            });
        }
    }

    export abstract class ConnectionApi<T> {

        Get(): JQueryPromise<T[]> {
            return this.CreateRequest<T[]>(this.BuildRequestBody("", HttpMethod.GET));
        }

        Connect(id1: number, id2: number): JQueryPromise<T> {
            return this.CreateRequest<T>(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.POST));
        }

        Disconnect(id1: number, id2: number): JQueryPromise<T> {
            return this.CreateRequest<T>(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.DELETE));
        }


        protected CreateRequest<T>(body: JQueryAjaxSettings): JQueryPromise<T> {
             Api.conntectionCount(Api.conntectionCount() + 1);
            return $.ajax(body).fail(d => {
                alert(d);
                console.log(d);
            }).always(() => {
                Api.conntectionCount(Api.conntectionCount() - 1);
            });
        }


        constructor(public baseUrl: string) {

        }

        BuildRequestBody(url: string, method: HttpMethod, data?: any): JQueryAjaxSettings {
            return {
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                method: HttpMethod[method],
                url: this.baseUrl + "/" + url,
                data: JSON.stringify(data)
            };
        }

    }

    export class HarbourApi extends Api<Harbour> {

        private static default: HarbourApi;

        static GetDefault(): HarbourApi {
            if (this.default === undefined)
                this.default = new HarbourApi("/api/Harbours");
            return this.default;
        }

    }

    export class AlbumApi extends Api<Album> {

        private static default: AlbumApi;

        static GetDefault(): AlbumApi {
            if (this.default === undefined)
                this.default = new AlbumApi("/api/Albums");
            return this.default;
        }

    }

    export class CommentApi extends Api<Comment> {

        private static default: CommentApi;

        static GetDefault(): CommentApi {
            if (this.default === undefined)
                this.default = new CommentApi("/api/Comments");
            return this.default;
        }

    }

    export class CommentListApi extends Api<CommentList> {

        private static default: CommentListApi;

        static GetDefault(): CommentListApi {
            if (this.default === undefined)
                this.default = new CommentListApi("/api/CommentLists");
            return this.default;
        }

    }

    export class WaypointApi extends Api<Waypoint> {

        private static default: WaypointApi;

        static GetDefault(): WaypointApi {
            if (this.default === undefined)
                this.default = new WaypointApi("/api/Waypoints");
            return this.default;
        }
    }

    export class WaypointConnectionApi {

        private static default: WaypointConnectionApi;

        static GetDefault(): WaypointConnectionApi {
            if (this.default === undefined)
                this.default = new WaypointConnectionApi("/api/WaypointConnections");
            return this.default;
        }

        Get(): JQueryPromise<WaypointConnection[]> {
            return this.CreateRequest<WaypointConnection[]>(this.BuildRequestBody("", HttpMethod.GET));
        }

        Connect(id1: number, id2: number): JQueryPromise<WaypointConnection> {
            return this.CreateRequest<WaypointConnection>(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.POST));
        }
        Disconnect(id: number): JQueryPromise<WaypointConnection>;
        Disconnect(id1: number, id2: number): JQueryPromise<WaypointConnection>;
        Disconnect(id1: number, id2?: number): JQueryPromise<WaypointConnection> {
            if (id2 !== undefined)
                return this.CreateRequest<WaypointConnection>(this.BuildRequestBody(id1 + "/" + id2, HttpMethod.DELETE));
            return this.CreateRequest<WaypointConnection>(this.BuildRequestBody(id1.toString(), HttpMethod.DELETE));
        }

        protected CreateRequest<T>(body: JQueryAjaxSettings): JQueryPromise<T> {
            Api.conntectionCount(Api.conntectionCount() + 1);
            return $.ajax(body).fail(d => {
                alert(d);
                console.log(d);
            }).always(() => {
                Api.conntectionCount(Api.conntectionCount() - 1);
            });
        }


        constructor(public baseUrl: string) {

        }

        BuildRequestBody(url: string, method: HttpMethod, data?: any): JQueryAjaxSettings {
            return {
                contentType: "application/json; charset=utf-8",
                dataType: "json",
                method: HttpMethod[method],
                url: this.baseUrl + "/" + url,
                data: JSON.stringify(data)
            };
        }

    }

    export class PersonApi extends Api<Person> {

        private static default: PersonApi;

        static GetDefault(): PersonApi {
            if (this.default === undefined)
                this.default = new PersonApi("/api/People");
            return this.default;
        }

    }

    export class JobApi extends Api<Job> {

        private static default: JobApi;

        static GetDefault(): JobApi {
            if (this.default === undefined)
                this.default = new JobApi("/api/Jobs");
            return this.default;
        }

    }

    export class TripApi extends Api<Trip> {

        private static default: TripApi;

        static GetDefault(): TripApi {
            if (this.default === undefined)
                this.default = new TripApi("/api/Trips");
            return this.default;
        }

    }

    export class TackApi extends Api<Tack> {

        private static default: TackApi;

        static GetDefault(): TackApi {
            if (this.default === undefined)
                this.default = new TackApi("/api/Tacks");
            return this.default;
        }

    }

    export class AddressApi extends Api<Address> {

        private static default: AddressApi;

        static GetDefault(): AddressApi {
            if (this.default === undefined)
                this.default = new AddressApi("/api/Addresses");
            return this.default;
        }

    }

    export class ImageApi extends Api<Image> {

        private static default: ImageApi;

        static GetDefault(): ImageApi {
            if (this.default === undefined)
                this.default = new ImageApi("/api/Images");
            return this.default;
        }

    }

    export class LocationApi extends Api<Location> {

        private static default: LocationApi;

        static GetDefault(): LocationApi {
            if (this.default === undefined)
                this.default = new LocationApi("/api/Locations");
            return this.default;
        }

    }

    export class SupermarketApi extends Api<Supermarket> {

        private static default: SupermarketApi;

        static GetDefault(): SupermarketApi {
            if (this.default === undefined)
                this.default = new SupermarketApi("/api/Supermarkets");
            return this.default;
        }

    }

    export class RestaurantApi extends Api<Restaurant> {

        private static default: RestaurantApi;

        static GetDefault(): RestaurantApi {
            if (this.default === undefined)
                this.default = new RestaurantApi("/api/Restaurants");
            return this.default;
        }

    }

    //export class WaypointTackApi extends Api<WaypointTack> {

    //    private static default: WaypointTackApi;

    //    static GetDefault(): WaypointTackApi {
    //        if (this.default === undefined)
    //            this.default = new WaypointTackApi("/api/WaypointTacks");
    //        return this.default;
    //    }

    //}

}