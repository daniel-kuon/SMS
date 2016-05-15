function CreateObservable<T extends ClientModel.IEntity>(options: IKoOptions): KnockoutObservable<T>;
function CreateObservable<T extends ClientModel.IEntity>(val?: T, options?: IKoOptions): KnockoutObservable<T>;
function CreateObservable<T extends ClientModel.IEntity>(val?: T | IKoOptions, options?: IKoOptions): KnockoutObservable<T> {
    var ob: KnockoutObservable<T>;
    if (val instanceof ClientModel.Entity)
        ob = ko.observable<T>(<any>val);
    else {
        ob = ko.observable<T>();
        if (options === undefined)
            options = val;
    }
    if (options !== undefined) {
        ob.ForeignKeyFor = options.ForeignKeyFor;
        ob.AddTransferMode = options.AddTransferMode;
        ob.UpdateTransferMode = options.UpdateTransferMode;
    }
    return ob;
}


function CreateObservableArray<T extends ClientModel.IEntity>(options: IKoOptions): KnockoutObservableArray<T>;
function CreateObservableArray<T extends ClientModel.IEntity>(val?: T[], options?: IKoOptions): KnockoutObservableArray<T>;
function CreateObservableArray<T extends ClientModel.IEntity>(val?: T[]|IKoOptions, options?: IKoOptions): KnockoutObservableArray<T> {
    var ob: KnockoutObservableArray<T>;
    if (val instanceof Array)
        ob = ko.observableArray<T>(<T[]>val);
    else {
        ob = ko.observableArray<T>();
        if (options === undefined)
            options = val;
    }
    if (options !== undefined) {
        ob.ForeignKeyFor = options.ForeignKeyFor;
        ob.AddTransferMode = options.AddTransferMode;
        ob.UpdateTransferMode = options.UpdateTransferMode;
    }
    return ob;
}

interface KnockoutObservable<T> extends IKoOptions {
}

enum TransferMode {
    Exclude,
    IdOnly,
    Include
}

interface IKoOptions {
    ForeignKeyFor?: KnockoutObservable<ClientModel.IEntity>;
    AddTransferMode?: TransferMode;
    UpdateTransferMode?: TransferMode;
    Block?: boolean;
}