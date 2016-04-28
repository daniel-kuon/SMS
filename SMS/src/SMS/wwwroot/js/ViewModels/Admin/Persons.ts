
    var personDeails = $("#personDetails");
    import Person=ClientModel.Person;


    var deletePerson = $("#deletePerson");
    var adminPersonViewModel: AdminPersonViewModel.Model;


module AdminPersonViewModel {
    export class Model {
        constructor() {
            
            this.NewPerson = ko.computed<boolean>(() => this.CurrentPerson().Id() === undefined);
            this.PersonApi.Get()
                .done(data => {
                    for (let person of data) {
                        this.People.push(new Person().LoadFromServerEntity(person));
                    }
                });
        }
        People = ko.observableArray<Person>();
        CurrentPerson=ko.observable(new Person());
        PersonApi=ServerApi.PersonApi.GetDefault();

        SavePerson() {
            adminPersonViewModel.CurrentPerson().SaveToServer().done(() => {
                personDeails.modal("hide");
                adminPersonViewModel.People.push(adminPersonViewModel.CurrentPerson());
                adminPersonViewModel.CurrentPerson(new Person());
            });

        }

        CancelEditPerson() {
            adminPersonViewModel.CurrentPerson().RevertState(true);
            adminPersonViewModel.CurrentPerson(new Person());
                personDeails.modal("hide");
        }

        DeletePerson() {
            adminPersonViewModel.CurrentPerson()
                .DeleteOnServer()
                .done(() => {
                    adminPersonViewModel.People.remove(adminPersonViewModel.CurrentPerson());
                    deletePerson.modal("hide");
                    adminPersonViewModel.CurrentPerson(undefined);
                });
        }

        AddPerson() {
            adminPersonViewModel.CurrentPerson(new Person());
            adminPersonViewModel.People.push(adminPersonViewModel.CurrentPerson());
            adminPersonViewModel.SavePerson();
        }

        SelectPerson(person: Person) {
            adminPersonViewModel.CurrentPerson(person);
            person.SaveState();
        }

        NewPerson: KnockoutComputed<boolean>;
    }
}

    adminPersonViewModel = new AdminPersonViewModel.Model();
    ko.applyBindings(adminPersonViewModel);