module ServerModel {
    export interface Entity {

        Id: number;
        ClientId?: number;
        Type?: string;
        ProcessOnServer?:boolean;

    }
    
   export class WaypointConnection {
        Waypoint1Id:number;
        Waypoint2Id:number;
    }
    
    export class AlbumImage {
        ImageId: number;
        AlbumId: number;
        Album: Entity;
        Image:Entity;
    }

    export class WaypointTack {
        WaypointId: number;
        Index: number;
        TackId: number;
    }

    export class Crew {
        TackId:number;
        PersonId:number;
    }

}