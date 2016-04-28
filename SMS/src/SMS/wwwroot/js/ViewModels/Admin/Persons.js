var personDeails = $("#personDetails");
var Person = ClientModel.Person;
var deletePerson = $("#deletePerson");
var adminPersonViewModel;
var AdminPersonViewModel;
(function (AdminPersonViewModel) {
    var Model = (function () {
        function Model() {
            var _this = this;
            this.People = ko.observableArray();
            this.CurrentPerson = ko.observable(new Person());
            this.PersonApi = ServerApi.PersonApi.GetDefault();
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

//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIlZpZXdNb2RlbHMvQWRtaW4vUGVyc29ucy50cyJdLCJuYW1lcyI6W10sIm1hcHBpbmdzIjoiQUFDSSxJQUFJLFlBQVksR0FBRyxDQUFDLENBQUMsZ0JBQWdCLENBQUMsQ0FBQztBQUN2QyxJQUFPLE1BQU0sR0FBQyxXQUFXLENBQUMsTUFBTSxDQUFDO0FBR2pDLElBQUksWUFBWSxHQUFHLENBQUMsQ0FBQyxlQUFlLENBQUMsQ0FBQztBQUN0QyxJQUFJLG9CQUFnRCxDQUFDO0FBR3pELElBQU8sb0JBQW9CLENBc0QxQjtBQXRERCxXQUFPLG9CQUFvQixFQUFDLENBQUM7SUFDekI7UUFDSTtZQURKLGlCQW9EQztZQXpDRyxXQUFNLEdBQUcsRUFBRSxDQUFDLGVBQWUsRUFBVSxDQUFDO1lBQ3RDLGtCQUFhLEdBQUMsRUFBRSxDQUFDLFVBQVUsQ0FBQyxJQUFJLE1BQU0sRUFBRSxDQUFDLENBQUM7WUFDMUMsY0FBUyxHQUFDLFNBQVMsQ0FBQyxTQUFTLENBQUMsVUFBVSxFQUFFLENBQUM7WUFWdkMsSUFBSSxDQUFDLFNBQVMsR0FBRyxFQUFFLENBQUMsUUFBUSxDQUFVLGNBQU0sT0FBQSxLQUFJLENBQUMsYUFBYSxFQUFFLENBQUMsRUFBRSxFQUFFLEtBQUssU0FBUyxFQUF2QyxDQUF1QyxDQUFDLENBQUM7WUFDckYsSUFBSSxDQUFDLFNBQVMsQ0FBQyxHQUFHLEVBQUU7aUJBQ2YsSUFBSSxDQUFDLFVBQUEsSUFBSTtnQkFDTixHQUFHLENBQUMsQ0FBZSxVQUFJLEVBQUosYUFBSSxFQUFKLGtCQUFJLEVBQUosSUFBSSxDQUFDO29CQUFuQixJQUFJLE1BQU0sYUFBQTtvQkFDWCxLQUFJLENBQUMsTUFBTSxDQUFDLElBQUksQ0FBQyxJQUFJLE1BQU0sRUFBRSxDQUFDLG9CQUFvQixDQUFDLE1BQU0sQ0FBQyxDQUFDLENBQUM7aUJBQy9EO1lBQ0wsQ0FBQyxDQUFDLENBQUM7UUFDWCxDQUFDO1FBS0QsMEJBQVUsR0FBVjtZQUNJLG9CQUFvQixDQUFDLGFBQWEsRUFBRSxDQUFDLFlBQVksRUFBRSxDQUFDLElBQUksQ0FBQztnQkFDckQsWUFBWSxDQUFDLEtBQUssQ0FBQyxNQUFNLENBQUMsQ0FBQztnQkFDM0Isb0JBQW9CLENBQUMsTUFBTSxDQUFDLElBQUksQ0FBQyxvQkFBb0IsQ0FBQyxhQUFhLEVBQUUsQ0FBQyxDQUFDO2dCQUN2RSxvQkFBb0IsQ0FBQyxhQUFhLENBQUMsSUFBSSxNQUFNLEVBQUUsQ0FBQyxDQUFDO1lBQ3JELENBQUMsQ0FBQyxDQUFDO1FBRVAsQ0FBQztRQUVELGdDQUFnQixHQUFoQjtZQUNJLG9CQUFvQixDQUFDLGFBQWEsRUFBRSxDQUFDLFdBQVcsQ0FBQyxJQUFJLENBQUMsQ0FBQztZQUN2RCxvQkFBb0IsQ0FBQyxhQUFhLENBQUMsSUFBSSxNQUFNLEVBQUUsQ0FBQyxDQUFDO1lBQzdDLFlBQVksQ0FBQyxLQUFLLENBQUMsTUFBTSxDQUFDLENBQUM7UUFDbkMsQ0FBQztRQUVELDRCQUFZLEdBQVo7WUFDSSxvQkFBb0IsQ0FBQyxhQUFhLEVBQUU7aUJBQy9CLGNBQWMsRUFBRTtpQkFDaEIsSUFBSSxDQUFDO2dCQUNGLG9CQUFvQixDQUFDLE1BQU0sQ0FBQyxNQUFNLENBQUMsb0JBQW9CLENBQUMsYUFBYSxFQUFFLENBQUMsQ0FBQztnQkFDekUsWUFBWSxDQUFDLEtBQUssQ0FBQyxNQUFNLENBQUMsQ0FBQztnQkFDM0Isb0JBQW9CLENBQUMsYUFBYSxDQUFDLFNBQVMsQ0FBQyxDQUFDO1lBQ2xELENBQUMsQ0FBQyxDQUFDO1FBQ1gsQ0FBQztRQUVELHlCQUFTLEdBQVQ7WUFDSSxvQkFBb0IsQ0FBQyxhQUFhLENBQUMsSUFBSSxNQUFNLEVBQUUsQ0FBQyxDQUFDO1lBQ2pELG9CQUFvQixDQUFDLE1BQU0sQ0FBQyxJQUFJLENBQUMsb0JBQW9CLENBQUMsYUFBYSxFQUFFLENBQUMsQ0FBQztZQUN2RSxvQkFBb0IsQ0FBQyxVQUFVLEVBQUUsQ0FBQztRQUN0QyxDQUFDO1FBRUQsNEJBQVksR0FBWixVQUFhLE1BQWM7WUFDdkIsb0JBQW9CLENBQUMsYUFBYSxDQUFDLE1BQU0sQ0FBQyxDQUFDO1lBQzNDLE1BQU0sQ0FBQyxTQUFTLEVBQUUsQ0FBQztRQUN2QixDQUFDO1FBR0wsWUFBQztJQUFELENBcERBLEFBb0RDLElBQUE7SUFwRFksMEJBQUssUUFvRGpCLENBQUE7QUFDTCxDQUFDLEVBdERNLG9CQUFvQixLQUFwQixvQkFBb0IsUUFzRDFCO0FBRUcsb0JBQW9CLEdBQUcsSUFBSSxvQkFBb0IsQ0FBQyxLQUFLLEVBQUUsQ0FBQztBQUN4RCxFQUFFLENBQUMsYUFBYSxDQUFDLG9CQUFvQixDQUFDLENBQUMiLCJmaWxlIjoiVmlld01vZGVscy9BZG1pbi9QZXJzb25zLmpzIiwic291cmNlc0NvbnRlbnQiOlsiXHJcbiAgICB2YXIgcGVyc29uRGVhaWxzID0gJChcIiNwZXJzb25EZXRhaWxzXCIpO1xyXG4gICAgaW1wb3J0IFBlcnNvbj1DbGllbnRNb2RlbC5QZXJzb247XHJcblxyXG5cclxuICAgIHZhciBkZWxldGVQZXJzb24gPSAkKFwiI2RlbGV0ZVBlcnNvblwiKTtcclxuICAgIHZhciBhZG1pblBlcnNvblZpZXdNb2RlbDogQWRtaW5QZXJzb25WaWV3TW9kZWwuTW9kZWw7XHJcblxyXG5cclxubW9kdWxlIEFkbWluUGVyc29uVmlld01vZGVsIHtcclxuICAgIGV4cG9ydCBjbGFzcyBNb2RlbCB7XHJcbiAgICAgICAgY29uc3RydWN0b3IoKSB7XHJcbiAgICAgICAgICAgIFxyXG4gICAgICAgICAgICB0aGlzLk5ld1BlcnNvbiA9IGtvLmNvbXB1dGVkPGJvb2xlYW4+KCgpID0+IHRoaXMuQ3VycmVudFBlcnNvbigpLklkKCkgPT09IHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgIHRoaXMuUGVyc29uQXBpLkdldCgpXHJcbiAgICAgICAgICAgICAgICAuZG9uZShkYXRhID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBwZXJzb24gb2YgZGF0YSkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLlBlb3BsZS5wdXNoKG5ldyBQZXJzb24oKS5Mb2FkRnJvbVNlcnZlckVudGl0eShwZXJzb24pKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcbiAgICAgICAgUGVvcGxlID0ga28ub2JzZXJ2YWJsZUFycmF5PFBlcnNvbj4oKTtcclxuICAgICAgICBDdXJyZW50UGVyc29uPWtvLm9ic2VydmFibGUobmV3IFBlcnNvbigpKTtcclxuICAgICAgICBQZXJzb25BcGk9U2VydmVyQXBpLlBlcnNvbkFwaS5HZXREZWZhdWx0KCk7XHJcblxyXG4gICAgICAgIFNhdmVQZXJzb24oKSB7XHJcbiAgICAgICAgICAgIGFkbWluUGVyc29uVmlld01vZGVsLkN1cnJlbnRQZXJzb24oKS5TYXZlVG9TZXJ2ZXIoKS5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgIHBlcnNvbkRlYWlscy5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgICAgICAgICBhZG1pblBlcnNvblZpZXdNb2RlbC5QZW9wbGUucHVzaChhZG1pblBlcnNvblZpZXdNb2RlbC5DdXJyZW50UGVyc29uKCkpO1xyXG4gICAgICAgICAgICAgICAgYWRtaW5QZXJzb25WaWV3TW9kZWwuQ3VycmVudFBlcnNvbihuZXcgUGVyc29uKCkpO1xyXG4gICAgICAgICAgICB9KTtcclxuXHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBDYW5jZWxFZGl0UGVyc29uKCkge1xyXG4gICAgICAgICAgICBhZG1pblBlcnNvblZpZXdNb2RlbC5DdXJyZW50UGVyc29uKCkuUmV2ZXJ0U3RhdGUodHJ1ZSk7XHJcbiAgICAgICAgICAgIGFkbWluUGVyc29uVmlld01vZGVsLkN1cnJlbnRQZXJzb24obmV3IFBlcnNvbigpKTtcclxuICAgICAgICAgICAgICAgIHBlcnNvbkRlYWlscy5tb2RhbChcImhpZGVcIik7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBEZWxldGVQZXJzb24oKSB7XHJcbiAgICAgICAgICAgIGFkbWluUGVyc29uVmlld01vZGVsLkN1cnJlbnRQZXJzb24oKVxyXG4gICAgICAgICAgICAgICAgLkRlbGV0ZU9uU2VydmVyKClcclxuICAgICAgICAgICAgICAgIC5kb25lKCgpID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBhZG1pblBlcnNvblZpZXdNb2RlbC5QZW9wbGUucmVtb3ZlKGFkbWluUGVyc29uVmlld01vZGVsLkN1cnJlbnRQZXJzb24oKSk7XHJcbiAgICAgICAgICAgICAgICAgICAgZGVsZXRlUGVyc29uLm1vZGFsKFwiaGlkZVwiKTtcclxuICAgICAgICAgICAgICAgICAgICBhZG1pblBlcnNvblZpZXdNb2RlbC5DdXJyZW50UGVyc29uKHVuZGVmaW5lZCk7XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICB9XHJcblxyXG4gICAgICAgIEFkZFBlcnNvbigpIHtcclxuICAgICAgICAgICAgYWRtaW5QZXJzb25WaWV3TW9kZWwuQ3VycmVudFBlcnNvbihuZXcgUGVyc29uKCkpO1xyXG4gICAgICAgICAgICBhZG1pblBlcnNvblZpZXdNb2RlbC5QZW9wbGUucHVzaChhZG1pblBlcnNvblZpZXdNb2RlbC5DdXJyZW50UGVyc29uKCkpO1xyXG4gICAgICAgICAgICBhZG1pblBlcnNvblZpZXdNb2RlbC5TYXZlUGVyc29uKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBTZWxlY3RQZXJzb24ocGVyc29uOiBQZXJzb24pIHtcclxuICAgICAgICAgICAgYWRtaW5QZXJzb25WaWV3TW9kZWwuQ3VycmVudFBlcnNvbihwZXJzb24pO1xyXG4gICAgICAgICAgICBwZXJzb24uU2F2ZVN0YXRlKCk7XHJcbiAgICAgICAgfVxyXG5cclxuICAgICAgICBOZXdQZXJzb246IEtub2Nrb3V0Q29tcHV0ZWQ8Ym9vbGVhbj47XHJcbiAgICB9XHJcbn1cclxuXHJcbiAgICBhZG1pblBlcnNvblZpZXdNb2RlbCA9IG5ldyBBZG1pblBlcnNvblZpZXdNb2RlbC5Nb2RlbCgpO1xyXG4gICAga28uYXBwbHlCaW5kaW5ncyhhZG1pblBlcnNvblZpZXdNb2RlbCk7Il0sInNvdXJjZVJvb3QiOiIvc291cmNlLyJ9
