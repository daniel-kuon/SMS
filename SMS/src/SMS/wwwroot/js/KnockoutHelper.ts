/// <reference path="clientmodel.ts" />


function CreateObservable<T extends ClientModel.Entity>(val?: T, options?:IKoOptions):KnockoutObservable<T>;
function CreateObservable<T>(options:IKoOptions):KnockoutObservable<T>;

interface IKoOptions {
    Block: boolean;
    ForeignKeyFor: KnockoutObservable<ClientModel.Entity<>>;

}