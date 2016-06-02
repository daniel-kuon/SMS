window.ParsleyConfig = {
    errorClass: 'has-error',
    successClass: 'has-success',
    showErrors: false,
    classHandler: function(ParsleyField) {
        return ParsleyField.$element.parents('.form-group');
    },
    errorsContainer: function(ParsleyField) {
        return ParsleyField.$element.parents('.form-group');
    },
    errorsWrapper: '<span class="help-block">',
    errorTemplate: '<div></div>',
    excluded: 'input[type=button], input[type=submit], input[type=reset], input[type=hidden], button'
};