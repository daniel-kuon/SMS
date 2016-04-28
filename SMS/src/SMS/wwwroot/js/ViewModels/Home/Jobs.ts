var m:any="";

module HomeJobsViewModel {
    var model: Model;

    import Job=ClientModel.Job;
    import Trip=ClientModel.Trip;
    import Person = ClientModel.Person;

    class Model
    {
        constructor() {
            ServerApi.PersonApi.GetDefault()
                .Get()
                .done(d => {
                    for (let sPerson of d) {
                        this.Persons.push(new Person().LoadFromServerEntity(sPerson));
                    }
                });
            ServerApi.JobApi.GetDefault()
                .Get()
                .done(d => {
                    for (let sJobList of d) {
                        this.JobLists.push(new Job().LoadFromServerEntity(sJobList));
                    }
                });
            this.Seed();
        }

        JobLists = ko.observableArray<Job>();
        Persons = ko.observableArray<Person>();
       
    }

    model = new Model();
    m = model;
    ko.applyBindings(model);

    var ApplyChangeToViewModel = e => {
            let context = ko.contextFor(e.target.bodyElement);
            console.log(context);
        }
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
            toolbar:
                "insertfile undo redo | styleselect | bold italic | alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | link image",
            setup: editor => {
                editor.on("blur", ApplyChangeToViewModel);
            }
        });
}





