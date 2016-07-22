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

//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJzb3VyY2VzIjpbIlZpZXdNb2RlbHMvSG9tZS9Kb2JzLnRzIl0sIm5hbWVzIjpbXSwibWFwcGluZ3MiOiJBQUFBLElBQUksQ0FBQyxHQUFLLEVBQUUsQ0FBQztBQUViLElBQU8saUJBQWlCLENBNER2QjtBQTVERCxXQUFPLGlCQUFpQixFQUFDLENBQUM7SUFDdEIsSUFBSSxLQUFZLENBQUM7SUFFakIsSUFBTyxHQUFHLEdBQUMsV0FBVyxDQUFDLEdBQUcsQ0FBQztJQUUzQixJQUFPLE1BQU0sR0FBRyxXQUFXLENBQUMsTUFBTSxDQUFDO0lBRW5DO1FBRUk7WUFGSixpQkF1QkM7WUFIRyxhQUFRLEdBQUcsRUFBRSxDQUFDLGVBQWUsRUFBTyxDQUFDO1lBQ3JDLFlBQU8sR0FBRyxFQUFFLENBQUMsZUFBZSxFQUFVLENBQUM7WUFsQm5DLFNBQVMsQ0FBQyxTQUFTLENBQUMsVUFBVSxFQUFFO2lCQUMzQixHQUFHLEVBQUU7aUJBQ0wsSUFBSSxDQUFDLFVBQUEsQ0FBQztnQkFDSCxHQUFHLENBQUMsQ0FBZ0IsVUFBQyxFQUFELE9BQUMsRUFBRCxlQUFDLEVBQUQsSUFBQyxDQUFDO29CQUFqQixJQUFJLE9BQU8sVUFBQTtvQkFDWixLQUFJLENBQUMsT0FBTyxDQUFDLElBQUksQ0FBQyxJQUFJLE1BQU0sRUFBRSxDQUFDLG9CQUFvQixDQUFDLE9BQU8sQ0FBQyxDQUFDLENBQUM7aUJBQ2pFO1lBQ0wsQ0FBQyxDQUFDLENBQUM7WUFDUCxTQUFTLENBQUMsTUFBTSxDQUFDLFVBQVUsRUFBRTtpQkFDeEIsR0FBRyxFQUFFO2lCQUNMLElBQUksQ0FBQyxVQUFBLENBQUM7Z0JBQ0gsR0FBRyxDQUFDLENBQWlCLFVBQUMsRUFBRCxPQUFDLEVBQUQsZUFBQyxFQUFELElBQUMsQ0FBQztvQkFBbEIsSUFBSSxRQUFRLFVBQUE7b0JBQ2IsS0FBSSxDQUFDLFFBQVEsQ0FBQyxJQUFJLENBQUMsSUFBSSxHQUFHLEVBQUUsQ0FBQyxvQkFBb0IsQ0FBQyxRQUFRLENBQUMsQ0FBQyxDQUFDO2lCQUNoRTtZQUNMLENBQUMsQ0FBQyxDQUFDO1lBQ1AsSUFBSSxDQUFDLElBQUksRUFBRSxDQUFDO1FBQ2hCLENBQUM7UUFLTCxZQUFDO0lBQUQsQ0F2QkEsQUF1QkMsSUFBQTtJQUVELEtBQUssR0FBRyxJQUFJLEtBQUssRUFBRSxDQUFDO0lBQ3BCLENBQUMsR0FBRyxLQUFLLENBQUM7SUFDVixFQUFFLENBQUMsYUFBYSxDQUFDLEtBQUssQ0FBQyxDQUFDO0lBRXhCLElBQUksc0JBQXNCLEdBQUcsVUFBQSxDQUFDO1FBQ3RCLElBQUksT0FBTyxHQUFHLEVBQUUsQ0FBQyxVQUFVLENBQUMsQ0FBQyxDQUFDLE1BQU0sQ0FBQyxXQUFXLENBQUMsQ0FBQztRQUNsRCxPQUFPLENBQUMsR0FBRyxDQUFDLE9BQU8sQ0FBQyxDQUFDO0lBQ3pCLENBQUMsQ0FBQTtJQUNMLE9BQU8sQ0FBQyxJQUFJLENBQUM7UUFDTCxRQUFRLEVBQUUsYUFBYTtRQUN2QixNQUFNLEVBQUUsSUFBSTtRQUNaLE9BQU8sRUFBRSxXQUFXO1FBQ3BCLE9BQU8sRUFBRSxLQUFLO0tBQ2pCLENBQUMsQ0FBQztJQUNQLE9BQU8sQ0FBQyxJQUFJLENBQUM7UUFDTCxRQUFRLEVBQUUsY0FBYztRQUN4QixNQUFNLEVBQUUsSUFBSTtRQUNaLE9BQU8sRUFBRTtZQUNMLGdFQUFnRTtZQUNoRSw0Q0FBNEM7WUFDNUMsOENBQThDO1NBQ2pEO1FBQ0QsT0FBTyxFQUNILGdKQUFnSjtRQUNwSixLQUFLLEVBQUUsVUFBQSxNQUFNO1lBQ1QsTUFBTSxDQUFDLEVBQUUsQ0FBQyxNQUFNLEVBQUUsc0JBQXNCLENBQUMsQ0FBQztRQUM5QyxDQUFDO0tBQ0osQ0FBQyxDQUFDO0FBQ1gsQ0FBQyxFQTVETSxpQkFBaUIsS0FBakIsaUJBQWlCLFFBNER2QiIsImZpbGUiOiJWaWV3TW9kZWxzL0hvbWUvSm9icy5qcyIsInNvdXJjZXNDb250ZW50IjpbInZhciBtOmFueT1cIlwiO1xyXG5cclxubW9kdWxlIEhvbWVKb2JzVmlld01vZGVsIHtcclxuICAgIHZhciBtb2RlbDogTW9kZWw7XHJcblxyXG4gICAgaW1wb3J0IEpvYj1DbGllbnRNb2RlbC5Kb2I7XHJcbiAgICBpbXBvcnQgVHJpcD1DbGllbnRNb2RlbC5UcmlwO1xyXG4gICAgaW1wb3J0IFBlcnNvbiA9IENsaWVudE1vZGVsLlBlcnNvbjtcclxuXHJcbiAgICBjbGFzcyBNb2RlbFxyXG4gICAge1xyXG4gICAgICAgIGNvbnN0cnVjdG9yKCkge1xyXG4gICAgICAgICAgICBTZXJ2ZXJBcGkuUGVyc29uQXBpLkdldERlZmF1bHQoKVxyXG4gICAgICAgICAgICAgICAgLkdldCgpXHJcbiAgICAgICAgICAgICAgICAuZG9uZShkID0+IHtcclxuICAgICAgICAgICAgICAgICAgICBmb3IgKGxldCBzUGVyc29uIG9mIGQpIHtcclxuICAgICAgICAgICAgICAgICAgICAgICAgdGhpcy5QZXJzb25zLnB1c2gobmV3IFBlcnNvbigpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNQZXJzb24pKTtcclxuICAgICAgICAgICAgICAgICAgICB9XHJcbiAgICAgICAgICAgICAgICB9KTtcclxuICAgICAgICAgICAgU2VydmVyQXBpLkpvYkFwaS5HZXREZWZhdWx0KClcclxuICAgICAgICAgICAgICAgIC5HZXQoKVxyXG4gICAgICAgICAgICAgICAgLmRvbmUoZCA9PiB7XHJcbiAgICAgICAgICAgICAgICAgICAgZm9yIChsZXQgc0pvYkxpc3Qgb2YgZCkge1xyXG4gICAgICAgICAgICAgICAgICAgICAgICB0aGlzLkpvYkxpc3RzLnB1c2gobmV3IEpvYigpLkxvYWRGcm9tU2VydmVyRW50aXR5KHNKb2JMaXN0KSk7XHJcbiAgICAgICAgICAgICAgICAgICAgfVxyXG4gICAgICAgICAgICAgICAgfSk7XHJcbiAgICAgICAgICAgIHRoaXMuU2VlZCgpO1xyXG4gICAgICAgIH1cclxuXHJcbiAgICAgICAgSm9iTGlzdHMgPSBrby5vYnNlcnZhYmxlQXJyYXk8Sm9iPigpO1xyXG4gICAgICAgIFBlcnNvbnMgPSBrby5vYnNlcnZhYmxlQXJyYXk8UGVyc29uPigpO1xyXG4gICAgICAgXHJcbiAgICB9XHJcblxyXG4gICAgbW9kZWwgPSBuZXcgTW9kZWwoKTtcclxuICAgIG0gPSBtb2RlbDtcclxuICAgIGtvLmFwcGx5QmluZGluZ3MobW9kZWwpO1xyXG5cclxuICAgIHZhciBBcHBseUNoYW5nZVRvVmlld01vZGVsID0gZSA9PiB7XHJcbiAgICAgICAgICAgIGxldCBjb250ZXh0ID0ga28uY29udGV4dEZvcihlLnRhcmdldC5ib2R5RWxlbWVudCk7XHJcbiAgICAgICAgICAgIGNvbnNvbGUubG9nKGNvbnRleHQpO1xyXG4gICAgICAgIH1cclxuICAgIHRpbnltY2UuaW5pdCh7XHJcbiAgICAgICAgICAgIHNlbGVjdG9yOiBcImgyLmVkaXRhYmxlXCIsXHJcbiAgICAgICAgICAgIGlubGluZTogdHJ1ZSxcclxuICAgICAgICAgICAgdG9vbGJhcjogXCJ1bmRvIHJlZG9cIixcclxuICAgICAgICAgICAgbWVudWJhcjogZmFsc2VcclxuICAgICAgICB9KTtcclxuICAgIHRpbnltY2UuaW5pdCh7XHJcbiAgICAgICAgICAgIHNlbGVjdG9yOiBcImRpdi5lZGl0YWJsZVwiLFxyXG4gICAgICAgICAgICBpbmxpbmU6IHRydWUsXHJcbiAgICAgICAgICAgIHBsdWdpbnM6IFtcclxuICAgICAgICAgICAgICAgIFwiYWR2bGlzdCBhdXRvbGluayBsaXN0cyBsaW5rIGltYWdlIGNoYXJtYXAgcHJpbnQgcHJldmlldyBhbmNob3JcIixcclxuICAgICAgICAgICAgICAgIFwic2VhcmNocmVwbGFjZSB2aXN1YWxibG9ja3MgY29kZSBmdWxsc2NyZWVuXCIsXHJcbiAgICAgICAgICAgICAgICBcImluc2VydGRhdGV0aW1lIG1lZGlhIHRhYmxlIGNvbnRleHRtZW51IHBhc3RlXCJcclxuICAgICAgICAgICAgXSxcclxuICAgICAgICAgICAgdG9vbGJhcjpcclxuICAgICAgICAgICAgICAgIFwiaW5zZXJ0ZmlsZSB1bmRvIHJlZG8gfCBzdHlsZXNlbGVjdCB8IGJvbGQgaXRhbGljIHwgYWxpZ25sZWZ0IGFsaWduY2VudGVyIGFsaWducmlnaHQgYWxpZ25qdXN0aWZ5IHwgYnVsbGlzdCBudW1saXN0IG91dGRlbnQgaW5kZW50IHwgbGluayBpbWFnZVwiLFxyXG4gICAgICAgICAgICBzZXR1cDogZWRpdG9yID0+IHtcclxuICAgICAgICAgICAgICAgIGVkaXRvci5vbihcImJsdXJcIiwgQXBwbHlDaGFuZ2VUb1ZpZXdNb2RlbCk7XHJcbiAgICAgICAgICAgIH1cclxuICAgICAgICB9KTtcclxufVxyXG5cclxuXHJcblxyXG5cclxuXHJcbiJdLCJzb3VyY2VSb290IjoiL3NvdXJjZS8ifQ==
