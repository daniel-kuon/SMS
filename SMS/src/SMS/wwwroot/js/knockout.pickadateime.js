
ko.bindingHandlers.pickadate = {
    init: function(element, valueAccessor, allBindingsAccessor) {
        var options = ko.utils.unwrapObservable(valueAccessor()) || {};
        $(element)
            .pickadate(Object.assign({},
                options,
                {
                    onSet: function() {
                        if (valueBinding) {
                            valueBinding(element.value);
                        }
                    }
                }));
    }
};

ko.bindingHandlers.pickatime = {
    init: function(element, valueAccessor) {
        var options = ko.utils.unwrapObservable(valueAccessor()) || {};
        $(element).pickatime(options);
    }
};

ko.bindingHandlers.pickadatetime = {
    init: function(element, valueAccessor) {
        var $element = $(element);
        var valueBinding;
        var timeOptions;
        var dateOptions;
        var initialValue;
        if (ko.isObservable(valueAccessor())) {
            valueBinding = valueAccessor();
            initialValue = valueAccessor()();
        } else if (valueAccessor() !== undefined) {
            if (ko.isObservable(valueAccessor().value)) {
                valueBinding = valueAccessor().value;
                initialValue = valueAccessor().value();
            } else
                initialValue = valueAccessor().value;
            timeOptions = valueAccessor().time;
            dateOptions = valueAccessor().date;
        }
        //if (ko.utils.unwrapObservable(valueBinding()) !== undefined)
        //    //$element.val(moment(valueBinding()).format("dddd, dd. mmmm yyyy"));
        //else
        //    $element.val("");
        var dateField = $("<input />");
        var timeField = $("<input />");
        var inputContainer =
            $("<div style='display:none;position: absolute; top: 0; left: 0; width: 1px; height: 1px; overflow: hidden;'></div>");
        inputContainer.append(dateField).append(timeField);
        $("body").append(inputContainer);
        var datepicker;
        var timepicker = timeField.pickatime($.extend({
                    //container: $element.parent()[0],
                    container: $("body")[0],
                    onRender: function() {
                        $('<button class="picker__button--clear" type="button">Zur&uuml;ck zum Datum</button>')
                            .on("click",
                                function() {
                                    timepicker.close();
                                    datepicker.open();
                                })
                            .prependTo(this.$root.find(".picker__box"));
                    },
                    onSet: function (item) {
                        if (item.select!==undefined) {
                            $element.off("focus")
                                .val(datepicker.get() + " " + timepicker.get())
                                .focus()
                                .on("focus", datepicker.open);
                            if (valueBinding !== undefined) {
                                var date = datepicker.get("select").obj;
                                date.setHours(Math.floor(timepicker.get("select").time / 60));
                                date.setMinutes(timepicker.get("select").time % 60);
                                valueBinding(date.toJSON());
                            }
                        } else if (valueBinding !== undefined) {
                            valueBinding(undefined);
                            $element.off("focus")
                                .val("")
                                .focus()
                                .on("focus", datepicker.open);
                        }
                    }
                },
                timeOptions))
            .pickatime("picker");
        datepicker = dateField.pickadate($.extend({
                    //container: $element.parent()[0]
                    container: $("body")[0]
                },
                dateOptions))
            .pickadate("picker");
        $element
            .on("focus", datepicker.open)
            .on("click",
                function(event) {
                    event.stopPropagation();
                    datepicker.open();
                });
        if (initialValue !== undefined) {
            var initialDate = new Date(initialValue);
            datepicker.set("select", new Date(initialValue));
            timepicker.set("select", initialDate.getHours()*60 + initialDate.getMinutes());
        }
        datepicker.on("set",
            function(item) {
                if ("select" in item)
                    setTimeout(timepicker.open, 0);
                else if (valueBinding !== undefined)
                    valueBinding(undefined);
            });
    }
};