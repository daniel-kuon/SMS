var personDeails = $("#personDetails");
var Person = ClientModel.Person;
var deletePerson = $("#deletePerson");
var adminPersonViewModel;
var AdminPersonViewModel;
(function (AdminPersonViewModel) {
    var Person = ClientModel.Person;
    var Model = (function () {
        function Model() {
            var _this = this;
            this.People = ko.observableArray();
            this.CurrentPerson = ko.observable(new Person());
            this.PersonApi = ServerApi.Persons;
            this.NewPerson = ko.computed(function () { return _this.CurrentPerson().Id() === undefined; });
            this.PersonApi.Get()
                .done(function (data) {
                for (var _i = 0, data_1 = data; _i < data_1.length; _i++) {
                    var person = data_1[_i];
                    _this.People.push(new Person().LoadFromServerEntity(person));
                }
            });
        }
        Model.prototype.SavePerson = function () {
            adminPersonViewModel.CurrentPerson().SaveToServer().done(function () {
                personDeails.modal("hide");
                adminPersonViewModel.People.push(adminPersonViewModel.CurrentPerson());
                adminPersonViewModel.CurrentPerson(new Person());
            });
        };
        Model.prototype.CancelEditPerson = function () {
            adminPersonViewModel.CurrentPerson().RevertState(true);
            adminPersonViewModel.CurrentPerson(new Person());
            personDeails.modal("hide");
        };
        Model.prototype.DeletePerson = function () {
            adminPersonViewModel.CurrentPerson()
                .DeleteOnServer()
                .done(function () {
                adminPersonViewModel.People.remove(adminPersonViewModel.CurrentPerson());
                deletePerson.modal("hide");
                adminPersonViewModel.CurrentPerson(undefined);
            });
        };
        Model.prototype.AddPerson = function () {
            adminPersonViewModel.CurrentPerson(new Person());
            adminPersonViewModel.People.push(adminPersonViewModel.CurrentPerson());
            adminPersonViewModel.SavePerson();
        };
        Model.prototype.SelectPerson = function (person) {
            adminPersonViewModel.CurrentPerson(person);
            person.SaveState();
        };
        return Model;
    }());
    AdminPersonViewModel.Model = Model;
})(AdminPersonViewModel || (AdminPersonViewModel = {}));
adminPersonViewModel = new AdminPersonViewModel.Model();
ko.applyBindings(adminPersonViewModel);
