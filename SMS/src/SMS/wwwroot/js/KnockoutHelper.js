function CreateObservable(options, val) {
    return $.extend(ko.observable(val), new KoOptions(), options);
}
function CreateObservableArray(options, val) {
    return $.extend(ko.observableArray(val), new KoOptions(), options);
}
var TransferMode;
(function (TransferMode) {
    TransferMode[TransferMode["Exclude"] = 0] = "Exclude";
    TransferMode[TransferMode["IdOnly"] = 1] = "IdOnly";
    TransferMode[TransferMode["Include"] = 2] = "Include";
})(TransferMode || (TransferMode = {}));
var KoOptions = (function () {
    function KoOptions() {
        this.AddTransferMode = TransferMode.Exclude;
        this.UpdateTransferMode = TransferMode.Exclude;
        this.IncludeInDelete = false;
    }
    return KoOptions;
}());
