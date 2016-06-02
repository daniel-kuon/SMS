window.tinymceOptions = {
    default: {
        theme: 'modern',
        language_url: '/lib/tinymce-i18n/langs/de.js',
        plugins: [
            'advlist autolink lists link image charmap hr anchor pagebreak',
            'searchreplace wordcount visualblocks visualchars code',
            'insertdatetime media nonbreaking save table contextmenu directionality',
            'emoticons template paste textcolor colorpicker textpattern imagetools'
        ],
        toolbar1:
            'insertfile undo redo | styleselect | bold italic | alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | link image',
        toolbar2: 'media | forecolor backcolor emoticons',
        image_advtab: true,
        statusbar: false
    }
};