// UI strings in the browser (window.omniText), from the same files as the server (Resources/i18n/{lang}.json).
// The layout sets window.omniTextConfig = { lang, terms } (the request's language and the client's business-type words)
// and loads /i18n/{lang}.js, which sets window.omniStrings. Usage: omniText.t('signup.businessType.label'),
// omniText.t('contacts.errors.unknownStage', ['vip']). {0}, {1}… take the arguments; {contact}, {Contacts}… take the
// business type's words. A missing key comes back as the key; a placeholder with no value is left as written.
(function () {
    'use strict';
    var config = window.omniTextConfig || {};
    var terms = config.terms || {};
    var has = Object.prototype.hasOwnProperty;

    function t(key, args) {
        var strings = window.omniStrings || {};
        var text = has.call(strings, key) ? strings[key] : null;
        if (text === null) return key;
        return text.replace(/\{([0-9]+|[A-Za-z]+)\}/g, function (match, name) {
            if (/^[0-9]+$/.test(name)) {
                var value = args ? args[Number(name)] : undefined;
                return value === undefined || value === null ? match : String(value);
            }
            return has.call(terms, name) ? terms[name] : match;
        });
    }

    window.omniText = {
        lang: config.lang || 'en',
        terms: terms,
        t: t,
        has: function (key) { return has.call(window.omniStrings || {}, key); }
    };
})();
