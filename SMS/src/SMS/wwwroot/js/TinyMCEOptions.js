﻿window.tinymceOptions = {
    default: {
        theme: 'modern',
        language_url: '/lib/tinymce-i18n/langs/de.js',
        plugins: [
            'advlist autolink lists link image charmap hr anchor pagebreak',
            'searchreplace wordcount visualblocks visualchars code',
            'insertdatetime media nonbreaking table contextmenu directionality',
            'emoticons template paste textcolor colorpicker textpattern imagetools'
        ],
        toolbar1:
            'insertfile undo redo | styleselect | bold italic | alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | link image',
        toolbar2: 'media | forecolor backcolor emoticons',
        image_advtab: true,
        statusbar: false,
    extended_valid_elements : "script[charset|defer|language|src|type],object[classid|codebase|height|width|id|align],param[name|wmode|value],embed[language|type|src|height|width|flashvars|quality|name|align|allowScriptAccess|pluginspage|autostart|loop|volume],a[name|href|target|title],img[class|src|border=0|alt|title|hspace|vspace|width|height|align|name|style],hr[class|width|size|noshade],font[face|size|color|style],span[class|align|style]"
    }
};