// UI strings in the browser (window.omniText), from the same files as the server (Resources/i18n/{lang}.json).
// The layout sets window.omniTextConfig = { lang, locale, dir, terms, showKeys } (the request's language, the locale tag
// dates and numbers are formatted with, the text direction and the client's business-type words) and loads
// /i18n/{lang}.js, which sets window.omniStrings (a translation carries English for keys it doesn't have yet).
// Usage: omniText.t('signup.businessType.label'), omniText.t('contacts.errors.unknownStage', ['vip']),
// omniText.plural('services.count', n). {0}, {1}… take the arguments ({0:N0} formats a number in the language);
// {contact}, {Contacts}… take the business type's words. A missing key comes back as the key; a placeholder with no
// value is left as written. Dates and numbers: new Intl.DateTimeFormat(omniText.locale, …), never the browser default.
(function () {
    'use strict';
    var config = window.omniTextConfig || {};
    var terms = config.terms || {};
    var locale = config.locale || 'en-US';
    var has = Object.prototype.hasOwnProperty;

    function formatNumber(value, format) {
        var digits = /^N(\d*)$/i.exec(format || '');
        if (typeof value !== 'number' || !digits) return String(value);
        var places = digits[1] === '' ? 2 : Number(digits[1]);
        try {
            return new Intl.NumberFormat(locale, { minimumFractionDigits: places, maximumFractionDigits: places }).format(value);
        } catch (e) {
            return value.toFixed(places);
        }
    }

    function t(key, args) {
        var strings = window.omniStrings || {};
        var text = has.call(strings, key) ? strings[key] : null;
        if (text === null) return key;
        text = text.replace(/\{([0-9]+|[A-Za-z]+)(?::([A-Za-z0-9#.,]+))?\}/g, function (match, name, format) {
            if (/^[0-9]+$/.test(name)) {
                var value = args ? args[Number(name)] : undefined;
                if (value === undefined || value === null) return match;
                return format ? formatNumber(value, format) : String(value);
            }
            if (format) return match;
            return has.call(terms, name) ? terms[name] : match;
        });
        return config.showKeys ? text + ' ⟦' + key + '⟧' : text;
    }

    // Plural rules of the page's language (en: one/other; fr: one for 0 and 1; ar: zero/one/two/few/many/other), the same
    // as Common/Helpers/PluralRules on the server. A form the strings don't have falls back to .other.
    var rules = null;
    try { rules = new Intl.PluralRules(locale); } catch (e) { rules = null; }

    function plural(key, count, args) {
        var strings = window.omniStrings || {};
        var n = Number(count);
        var category = isNaN(n) ? 'other' : rules ? rules.select(n) : (n === 1 ? 'one' : 'other');
        var exact = key + '.' + category;
        return t(has.call(strings, exact) ? exact : key + '.other', args || [count]);
    }

    window.omniText = {
        lang: config.lang || 'en',
        locale: locale,
        dir: config.dir || 'ltr',
        terms: terms,
        t: t,
        plural: plural,
        has: function (key) { return has.call(window.omniStrings || {}, key); }
    };
})();
