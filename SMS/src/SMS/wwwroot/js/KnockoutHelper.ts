function CreateObservable<T extends ClientModel.IEntity>(options: IKoOptions, val?: T): KnockoutObservable<T> {
    return $.extend(ko.observable(val), new KoOptions(), options);
}


function CreateObservableArray<T extends ClientModel.IEntity>(options: IKoOptions, val?: T[]): KnockoutObservableArray<T> {
    return $.extend(ko.observableArray(val), new KoOptions(), options);
}

interface KnockoutObservable<T> extends IKoOptions {
}

enum TransferMode {
    Exclude,
    IdOnly,
    Include
}

interface IKoOptions {
    NavigationProperty?: <T extends ClientModel.Entity>(o: T) => KnockoutObservable<ClientModel.Entity>;
    ForeignKey?: <T extends ClientModel.Entity>(o: T) => KnockoutObservable<number>;
    ReverseNavigationProperty?: <T extends ClientModel.Entity>(o: T) => KnockoutObservable<ClientModel.Entity>;
    AddTransferMode?: TransferMode;
    UpdateTransferMode?: TransferMode;
    IncludeInDelete?: boolean;
    Block?: boolean;
}

class KoOptions {
    AddTransferMode = TransferMode.Exclude;
    UpdateTransferMode = TransferMode.Exclude;
    IncludeInDelete = false;
}