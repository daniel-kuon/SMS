var m = "";
var HomeJobsViewModel;
(function (HomeJobsViewModel) {
    var model;
    var Job = ClientModel.Job;
    var Person = ClientModel.Person;
    var Model = (function () {
        function Model() {
            var _this = this;
            this.JobLists = ko.observableArray();
            this.Persons = ko.observableArray();
            ServerApi.PersonApi.GetDefault()
                .Get()
                .done(function (d) {
                for (var _i = 0, d_1 = d; _i < d_1.length; _i++) {
                    var sPerson = d_1[_i];
                    _this.Persons.push(new Person().LoadFromServerEntity(sPerson));
                }
            });
            ServerApi.JobApi.GetDefault()
                .Get()
                .done(function (d) {
                for (var _i = 0, d_2 = d; _i < d_2.length; _i++) {
                    var sJobList = d_2[_i];
                    _this.JobLists.push(new Job().LoadFromServerEntity(sJobList));
                }
            });
            this.Seed();
        }
        return Model;
    }());
    model = new Model();
    m = model;
    ko.applyBindings(model);
    var ApplyChangeToViewModel = function (e) {
        var context = ko.contextFor(e.target.bodyElement);
        console.log(context);
    };
    tinymce.init({
        selector: "h2.editable",
        inline: true,
        toolbar: "undo redo",
        menubar: false
    });
    tinymce.init({
        selector: "div.editable",
        inline: true,
        plugins: [
            "advlist autolink lists link image charmap print preview anchor",
            "searchreplace visualblocks code fullscreen",
            "insertdatetime media table contextmenu paste"
        ],
        toolbar: "insertfile undo redo | styleselect | bold italic | alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | link image",
        setup: function (editor) {
            editor.on("blur", ApplyChangeToViewModel);
        }
    });
})(HomeJobsViewModel || (HomeJobsViewModel = {}));
