// Wires the "Continue with Facebook" buttons on Settings → Channels & Integrations. Expects
// window.PS_META_CONFIG to be set (inline in Integrations.cshtml) with
// { appId, version, clinicId, whatsappConfigId, facebookConfigId }.
(function () {
  var cfg = window.PS_META_CONFIG || {};

  window.fbAsyncInit = function () {
    FB.init({
      appId: cfg.appId,
      cookie: true,
      xfbml: true, // needed so FB parses the <fb:login-button> plugin used by Facebook Messenger
      version: cfg.version
    });
  };

  (function (d, s, id) {
    var js, fjs = d.getElementsByTagName(s)[0];
    if (d.getElementById(id)) { return; }
    js = d.createElement(s); js.id = id;
    js.src = 'https://connect.facebook.net/en_US/sdk.js';
    fjs.parentNode.insertBefore(js, fjs);
  }(document, 'script', 'facebook-jssdk'));

  function setStatus(channel, message, isError) {
    var el = document.querySelector('[data-status-for="' + channel + '"]');
    if (!el) { return; }
    el.textContent = message;
    el.className = isError ? 'text-subtle connect-error' : 'text-subtle';
  }

  function postConnect(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    }).then(function (res) {
      if (!res.ok) {
        return res.json().catch(function () { return {}; }).then(function (problem) {
          throw new Error(problem.detail || ('Request failed (' + res.status + ')'));
        });
      }
      return res.json();
    });
  }

  // ---------------------------------------------------------------------
  // WhatsApp — full-page OAuth redirect (not the JS SDK popup).
  //
  // Why: FB.login()'s popup hands back a code, but that code is tied to a redirect_uri internal
  // to Facebook's SDK (used to bridge the popup back to the opener window) that we have no way to
  // know or reproduce — every server-side exchange attempt against it fails with OAuthException
  // subcode 36008 ("redirect_uri ... not identical"), regardless of what we pass. A full-page
  // redirect sidesteps this entirely: we choose the redirect_uri, Facebook redirects the whole
  // browser back to that exact URL with ?code=..., and we exchange it using that same URL —
  // provably matching, since there's no popup/bridge involved.
  //
  // Trade-off: no popup means no postMessage, so we can't read the selected WABA/phone number the
  // way Embedded Signup normally delivers them. The backend auto-discovers them instead via the
  // Graph API (client/owned_whatsapp_business_accounts → phone_numbers) once it has a token — see
  // ChannelIntegrationService.ConnectWhatsAppAsync.
  // ---------------------------------------------------------------------
  var WHATSAPP_REDIRECT_URI = window.location.origin + '/settings/integrations';

  window.psConnectWhatsApp = function () {
    if (!cfg.whatsappConfigId) {
      setStatus('whatsapp', 'WhatsApp Login Configuration ID is not set yet.', true);
      return;
    }
    var authUrl = 'https://www.facebook.com/' + cfg.version + '/dialog/oauth'
      + '?client_id=' + encodeURIComponent(cfg.appId)
      + '&config_id=' + encodeURIComponent(cfg.whatsappConfigId)
      + '&response_type=code'
      + '&redirect_uri=' + encodeURIComponent(WHATSAPP_REDIRECT_URI)
      + '&extras=' + encodeURIComponent(JSON.stringify({ setup: {} }));
    window.location.href = authUrl;
  };

  // On page load, if Meta just redirected back here with ?code=..., finish the connection.
  (function checkForWhatsAppRedirectReturn() {
    var params = new URLSearchParams(window.location.search);
    var code = params.get('code');
    var oauthError = params.get('error_description') || params.get('error');

    if (code || oauthError) {
      // Clean the query string immediately so a page refresh doesn't resend a stale/used code.
      window.history.replaceState({}, document.title, window.location.pathname);
    }
    if (oauthError) {
      setStatus('whatsapp', 'Facebook returned an error: ' + oauthError, true);
      return;
    }
    if (!code) {
      return;
    }

    setStatus('whatsapp', 'Finishing connection…', false);
    postConnect('/api/channel-integrations/whatsapp/connect', {
      code: code,
      redirectUri: WHATSAPP_REDIRECT_URI
    })
      .then(function () { window.location.reload(); })
      .catch(function (err) { setStatus('whatsapp', err.message, true); });
  })();

  // Facebook Messenger uses the fb:login-button plugin instead of a JS-driven FB.login() call
  // (see Integrations.cshtml) — it calls this via its onlogin="" attribute once the popup closes,
  // and we then fetch the actual result with FB.getLoginStatus(), per Meta's own Quickstart. This
  // one isn't affected by the WhatsApp issue above because it uses the implicit token flow
  // (no code, no server-side exchange, no redirect_uri to match).
  window.psFacebookOnLogin = function () {
    setStatus('facebook', 'Checking login status…', false);

    FB.getLoginStatus(function (response) {
      if (response.status !== 'connected' || !response.authResponse || !response.authResponse.accessToken) {
        setStatus('facebook', 'Login was cancelled or did not complete.', true);
        return;
      }

      setStatus('facebook', 'Finishing connection…', false);
      postConnect('/api/channel-integrations/facebook/connect', {
        accessToken: response.authResponse.accessToken
      })
        .then(function () { window.location.reload(); })
        .catch(function (err) { setStatus('facebook', err.message, true); });
    });
  };
})();
