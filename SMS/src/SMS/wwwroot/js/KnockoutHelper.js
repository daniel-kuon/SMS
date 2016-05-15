function CreateObservable(val, options) {
    var ob;
    if (val instanceof ClientModel.Entity)
        ob = ko.observable(val);
    else {
        ob = ko.observable();
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
function CreateObservableArray(val, options) {
    var ob;
    if (val instanceof Array)
        ob = ko.observableArray(val);
    else {
        ob = ko.observableArray();
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
var TransferMode;
(function (TransferMode) {
    TransferMode[TransferMode["Exclude"] = 0] = "Exclude";
    TransferMode[TransferMode["IdOnly"] = 1] = "IdOnly";
    TransferMode[TransferMode["Include"] = 2] = "Include";
})(TransferMode || (TransferMode = {}));
