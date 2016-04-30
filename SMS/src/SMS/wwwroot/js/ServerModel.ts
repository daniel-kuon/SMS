module ServerModel {
    export abstract class Entity {

        Id: number;

    }

    export class CommentList extends Entity{}
    export class Comment extends Entity{}

    export class Waypoint extends Entity {
        WaypointNumber: number;
        Name: string;
        Description: string;
        Latitude: number;
        Longitude: number;
        Type: string;

        static GetType(): string {
            return "Waypoint";
        }
    }

    export class WaypointConnection {
        Waypoint1Id:number;
        Waypoint2Id:number;
    }

    export class Harbour extends Waypoint {
        Locations: Array<Location>;
        Album: Album;
        AlbumId: number;
        Rating: number;
        Content:string;

        static GetType(): string {
            return "Harbour";
        }
    }

    export class Person extends Entity {

        LastName: string;
        FirstName: string;
    }

    export class Job extends Entity {
        DueTo: Date;
        AssignedTo: Person;
        AssignedToId:number;
        Title: String;
        Content: String;
        Done: boolean;
        SuperJob: Job;
        SuperJobId:number;
        Trip:Trip;
        TripId:number;
        SubJobs:Job[];
    }
    

    export class Trip extends Entity {
        Name: string;
        Start: Date;
        End: Date;
        Content: string;
        Tacks: Array<Tack>;
    }

    export class Address extends Entity {
        Street: string;
        Zip: string;
        Town: string;
        Comment: string;
    }

    export class Image extends Entity {
        AlbumId: number;
        Path: string;
        Comment: string;
        Height: number;
        Width: number;
    }

    export class Album extends Entity {
        StandAlone: boolean;
        Comment: string;
        Images: Array<Image>;
    }

    export class AlbumImage {
        ImageId: number;
        AlbumId:number;
    }

    export class WaypointTack {
        WaypointId: number;
        Index: number;
        TackId: number;
    }

    export class Tack extends Entity {
        Start: Date;
        End: Date;
        Waypoints: Array<WaypointTack>;
        Crew: Array<Person>;
    }

    export class Location extends Entity {
         HarbourId:number;
        Website:string;
        Name:string;
        Rating:number;
        Type: string;
        AddressId:number;

        static GetType() {
             return "Location";
        }
    }

    export class Restaurant extends Location{ 

        static GetType() {
             return "Restaurant";
        }}
    export class Supermarket extends Location {

        static GetType() {
             return "Supermarket";
        }}
}